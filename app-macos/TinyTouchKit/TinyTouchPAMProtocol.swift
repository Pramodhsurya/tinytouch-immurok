import Foundation
import CryptoKit
import Darwin
import Security

public struct TinyTouchPAMRequest: Equatable {
    public let uid: UInt32
    public let user: String
    public let service: String
    public let pid: Int32
    public let nonce: String
    public init(line: String, expectedUID: UInt32, expectedUser: String, peerPID: Int32) throws {
        guard line.utf8.count <= 256, line.hasSuffix("\n") else { throw TinyTouchAuthError.invalidResponse }
        let fields = line.dropLast().split(separator: ":", omittingEmptySubsequences: false).map(String.init)
        guard fields.count == 6, fields[0] == "AUTH", fields[1] == String(expectedUID),
              fields[2] == expectedUser, Self.validUser(fields[2]),
              ["sudo", "sudo_local"].contains(fields[3]), peerPID > 0, fields[4] == String(peerPID),
              TinyTouchAuthRequest.hexData(fields[5]) != nil else { throw TinyTouchAuthError.invalidResponse }
        uid = expectedUID; user = fields[2]; service = fields[3]; pid = peerPID; nonce = fields[5]
    }
    static func validUser(_ text: String) -> Bool {
        !text.isEmpty && text.utf8.count <= 64 && text.range(of: "^[A-Za-z0-9_.-]+$", options: .regularExpression) != nil
    }
    public var context: Data { Data(("tinyTouch-pam-action-v1|\(uid)|\(user)|\(service)|\(pid)|\(nonce)").utf8) }
    public func receipt(key: Data) throws -> String {
        guard key.count == 32, let nonceBytes = TinyTouchAuthRequest.hexData(nonce) else { throw TinyTouchAuthError.invalidResponse }
        var material = Data("tinyTouch-pam-receipt-v1".utf8)
        material.append(nonceBytes)
        for value in [uid, UInt32(pid)] {
            var big = value.bigEndian
            withUnsafeBytes(of: &big) { material.append(contentsOf: $0) }
        }
        material.append(Data(user.utf8)); material.append(0)
        material.append(Data(service.utf8)); material.append(0)
        return "OK:" + TinyTouchAuthRequest.hex(Data(HMAC<SHA256>.authenticationCode(for: material, using: SymmetricKey(data: key)))) + "\n"
    }
}

final class TinyTouchPAMReplayGuard {
    private var seen: [String: UInt64] = [:]
    private let now: () -> UInt64
    init(now: @escaping () -> UInt64 = { DispatchTime.now().uptimeNanoseconds }) { self.now = now }
    // Called only by the serial socket queue. Even failed requests consume a nonce.
    func consume(_ nonce: String) -> Bool {
        let current = now()
        seen = seen.filter { current < $0.value || current - $0.value < 120_000_000_000 }
        guard seen[nonce] == nil, seen.count < 256 else { return false }
        seen[nonce] = current
        return true
    }
}

public enum TinyTouchPAMTrust {
    public static func ownCodeHash() throws -> String {
        var code: SecCode?
        guard SecCodeCopySelf([], &code) == errSecSuccess, let code else { throw TinyTouchAuthError.invalidResponse }
        return try codeHash(code)
    }
    private static func codeHash(_ code: SecCode) throws -> String {
        guard SecCodeCheckValidity(code, SecCSFlags(rawValue: kSecCSStrictValidate), nil) == errSecSuccess else { throw TinyTouchAuthError.invalidResponse }
        var staticCode: SecStaticCode?
        guard SecCodeCopyStaticCode(code, [], &staticCode) == errSecSuccess, let staticCode else { throw TinyTouchAuthError.invalidResponse }
        var info: CFDictionary?
        guard SecCodeCopySigningInformation(staticCode, SecCSFlags(rawValue: kSecCSSigningInformation), &info) == errSecSuccess,
              let data = (info as? [String: Any])?[kSecCodeInfoUnique as String] as? Data, data.count == 20 else {
            throw TinyTouchAuthError.invalidResponse
        }
        return TinyTouchAuthRequest.hex(data)
    }
    public static func verifyPeer(fd: Int32, expectedHash: String) throws {
        guard expectedHash.range(of: "^[0-9a-f]{40}$", options: .regularExpression) != nil else { throw TinyTouchAuthError.invalidResponse }
        var token = audit_token_t(); var length = socklen_t(MemoryLayout<audit_token_t>.size)
        guard getsockopt(fd, SOL_LOCAL, LOCAL_PEERTOKEN, &token, &length) == 0,
              length == socklen_t(MemoryLayout<audit_token_t>.size) else { throw TinyTouchAuthError.invalidResponse }
        let bytes = withUnsafeBytes(of: &token) { Data($0) }
        var code: SecCode?
        guard SecCodeCopyGuestWithAttributes(nil, [kSecGuestAttributeAudit: bytes] as CFDictionary, [], &code) == errSecSuccess,
              let code, try codeHash(code) == expectedHash else { throw TinyTouchAuthError.invalidResponse }
    }
}
