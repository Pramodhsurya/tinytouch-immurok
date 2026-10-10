import Foundation
import CryptoKit

public enum TinyTouchPAMConfiguration {
    public static let directory = "/Library/Application Support/tinyTouch/PAM"
    public static let module = "/Library/Security/tinyTouch/pam_tinytouch.so"
    public static let line = "auth sufficient " + module
    public static func keyID(_ key: Data) -> String {
        String(TinyTouchAuthRequest.hex(Data(SHA256.hash(data: key))).prefix(16))
    }
    public static func updated(_ text: String, enabled: Bool) throws -> String {
        guard text.utf8.count <= 16384, !text.contains("\0") else { throw TinyTouchAuthError.invalidResponse }
        let lines = text.split(separator: "\n", omittingEmptySubsequences: false)
            .map(String.init).filter { $0.trimmingCharacters(in: .whitespaces) != line }
        let retained = lines.joined(separator: "\n")
        let prefix = enabled ? line + "\n" : ""
        let combined = prefix + retained
        return combined.hasSuffix("\n") ? combined : combined + "\n"
    }
}
