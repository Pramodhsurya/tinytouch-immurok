import Foundation
import CryptoKit
import Security

public enum TinyTouchAuthError: Error, LocalizedError {
    case invalidResponse, rejected, expired, unsupported, keychain, busy, lease, cancelled, disconnected
    case transport(stage: String, code: Int32)
    public var errorDescription: String? {
        switch self {
        case .invalidResponse: return "The authentication response failed verification."
        case .rejected: return "Fingerprint authentication was rejected. Lift your finger and try again."
        case .expired: return "This authentication request expired. Start a new request."
        case .unsupported: return "Update the ESP32 to firmware with authenticated request support first."
        case .keychain: return "The pairing key could not be read. Allow access in the Mac Keychain prompt, or check the existing tinyTouch pairing."
        case .busy: return "Another tinyTouch operation is using USB. Wait for it to finish."
        case .lease: return "The USB helper did not acknowledge exclusive access. Check helper health and try again."
        case .cancelled: return "Authentication cancelled."
        case .disconnected: return "USB disconnected during this authentication request. Reconnect the device and start a new test."
        case .transport(let stage, let code): return "USB \(stage) failed (system error \(code)). Reconnect the device and try again."
        }
    }
}

/// A single request, never a cached 'device authenticated' flag. Challenge,
/// host and fingerprint-result tags use distinct domains and include the
/// device serial, host ID, both nonces and the exact requested context hash.
public final class TinyTouchAuthRequest {
    private enum Phase { case new, waiting, consumed }
    private var phase: Phase = .new
    private let key: SymmetricKey
    private let serial: String
    public let hostID: String
    public let clientNonce: String
    public let contextHash: String
    private var deviceNonce = ""
    private let created: UInt64
    private let now: () -> UInt64
    private var lifetimeNanoseconds: UInt64 = 60_000_000_000

    public convenience init(key: Data, serial: String, context: Data) throws {
        var nonce = Data(count: 32)
        let result = nonce.withUnsafeMutableBytes { SecRandomCopyBytes(kSecRandomDefault, 32, $0.baseAddress!) }
        guard result == errSecSuccess else { throw TinyTouchAuthError.invalidResponse }
        try self.init(key: key, serial: serial, contextHash: Data(SHA256.hash(data: context)), nonce: nonce)
    }
    // Explicit vectors/clock are internal to the library's tests.
    init(key: Data, serial: String, contextHash: Data, nonce: Data,
         now: @escaping () -> UInt64 = { DispatchTime.now().uptimeNanoseconds }) throws {
        guard key.count == 32, contextHash.count == 32, nonce.count == 32,
              TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: serial) else { throw TinyTouchAuthError.invalidResponse }
        self.key = SymmetricKey(data: key); self.serial = serial
        hostID = String(Self.hex(Data(SHA256.hash(data: key))).prefix(16))
        clientNonce = Self.hex(nonce); self.contextHash = Self.hex(contextHash)
        self.now = now; created = now()
    }
    public var beginCommand: String { "AUTH2 BEGIN \(hostID) \(clientNonce) \(contextHash)" }
    public func invalidate() { phase = .consumed }
    private func unexpired() throws {
        let current = now()
        guard current >= created, current - created < lifetimeNanoseconds else { throw TinyTouchAuthError.expired }
    }
    public func proveCommand(challenge: String) throws -> String {
        guard phase == .new else { throw TinyTouchAuthError.invalidResponse }
        phase = .consumed // Every failure consumes this client request.
        try unexpired()
        let fields = try Self.fields(challenge, prefix: "OK AUTH2 CHALLENGE", keys: ["nonce", "mac", "ttl_ms"])
        guard let ttl = fields["ttl_ms"], ["30000", "60000"].contains(ttl),
              let nonce = fields["nonce"], Self.hexData(nonce)?.count == 32,
              let mac = fields["mac"].flatMap(Self.hexData), mac.count == 32 else { throw TinyTouchAuthError.invalidResponse }
        lifetimeNanoseconds = UInt64(ttl)! * 1_000_000
        try unexpired()
        deviceNonce = nonce
        guard HMAC<SHA256>.isValidAuthenticationCode(mac, authenticating: material("challenge"), using: key) else { throw TinyTouchAuthError.invalidResponse }
        let hostTag = Data(HMAC<SHA256>.authenticationCode(for: material("host"), using: key))
        phase = .waiting
        return "AUTH2 PROVE \(clientNonce) \(Self.hex(hostTag))"
    }
    public func verifyMatch(_ response: String) throws {
        guard phase == .waiting else { throw TinyTouchAuthError.invalidResponse }
        phase = .consumed
        try unexpired()
        let fields = try Self.fields(response, prefix: "OK AUTH2 MATCH", keys: ["nonce", "context", "mac"])
        guard fields["nonce"] == clientNonce, fields["context"] == contextHash,
              let mac = fields["mac"].flatMap(Self.hexData), mac.count == 32,
              HMAC<SHA256>.isValidAuthenticationCode(mac, authenticating: material("match"), using: key) else { throw TinyTouchAuthError.invalidResponse }
    }
    private func material(_ role: String) -> Data {
        Data(["tinyTouch-auth2-fresh-v1", role, serial, hostID, clientNonce, deviceNonce, contextHash].joined(separator: "|").utf8)
    }
    private static func fields(_ line: String, prefix: String, keys: Set<String>) throws -> [String: String] {
        guard line.utf8.count <= 512, line.hasPrefix(prefix + " ") else { throw TinyTouchAuthError.invalidResponse }
        var result: [String: String] = [:]
        for field in line.dropFirst(prefix.count + 1).split(separator: " ", omittingEmptySubsequences: false) {
            let parts = field.split(separator: "=", omittingEmptySubsequences: false)
            guard parts.count == 2, keys.contains(String(parts[0])), result[String(parts[0])] == nil else { throw TinyTouchAuthError.invalidResponse }
            result[String(parts[0])] = String(parts[1])
        }
        guard Set(result.keys) == keys else { throw TinyTouchAuthError.invalidResponse }
        return result
    }
    static func hex(_ bytes: Data) -> String { bytes.map { String(format: "%02x", $0) }.joined() }
    static func hexData(_ text: String) -> Data? {
        guard text.count == 64, text.utf8.count == 64,
              text.range(of: "^[0-9a-f]{64}$", options: .regularExpression) != nil else { return nil }
        var bytes = Data(); var index = text.startIndex
        while index < text.endIndex {
            let next = text.index(index, offsetBy: 2)
            guard let byte = UInt8(text[index..<next], radix: 16) else { return nil }
            bytes.append(byte); index = next
        }
        return bytes
    }
}

/// Read only the existing pairing key on an explicit foreground user action.
/// Password items and Keychain ACLs are never read/changed here.
enum TinyTouchPairingKey {
    static func read(serial: String) throws -> Data {
        guard TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: serial) else { throw TinyTouchAuthError.keychain }
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "tinyTouch-pairing", kSecAttrAccount as String: serial,
            kSecMatchLimit as String: kSecMatchLimitOne, kSecReturnData as String: true,
            kSecUseAuthenticationUI as String: kSecUseAuthenticationUIAllow]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let encoded = item as? Data, let text = String(data: encoded, encoding: .utf8),
              let key = TinyTouchAuthRequest.hexData(text) else { throw TinyTouchAuthError.keychain }
        return key
    }
}
