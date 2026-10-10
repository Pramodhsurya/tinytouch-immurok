import Foundation
import Combine
import CryptoKit
import Security
import Darwin
import TinyTouchKit

/// A sudo request is an explicit foreground action. The legacy PAM server,
/// password-output helpers and authentication-test results never grant it.
@MainActor
final class TinyTouchSudoModel: ObservableObject {
    @Published private(set) var enabled = UserDefaults.standard.bool(forKey: "tinyTouch.sudo.enabled")
    @Published private(set) var installed = false
    @Published private(set) var busy = false
    @Published private(set) var message = "Install the sudo adapter, then enable fingerprint approval here."
    @Published private(set) var setupReady = false
    private let connection: TinyTouchConnection
    private var server: TinyTouchPAMServer?
    private var channelKey: Data?
    private var exportDeadline: UInt64 = 0
    private var cancellation: TinyTouchAuthCancellation?
    private var activeRequest: UUID?
    init(connection: TinyTouchConnection) {
        self.connection = connection
        let endpoint = TinyTouchPAMServer(
            directory: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/tinyTouch"),
            authorize: { [weak self] request, cancellation, completion in
                Task { @MainActor in
                    if let self { self.authorize(request, cancellation: cancellation, completion: completion) }
                    else { completion(nil) }
                }
            }, installationKey: { [weak self] completion in
                Task { @MainActor in
                    guard let self, self.setupReady, DispatchTime.now().uptimeNanoseconds < self.exportDeadline else { completion(nil); return }
                    self.setupReady = false; self.exportDeadline = 0
                    completion(self.channelKey)
                    self.message = "Setup key transferred to the administrator installer. Refresh setup status when installation finishes."
                }
            })
        do { try endpoint.start(); server = endpoint; refresh() }
        catch { message = "The sudo approval service could not start. Quit any other tinyTouch Native instance and reopen the app." }
    }
    deinit { cancellation?.cancel(); server?.stop() }
    private func rootFile(_ path: String, length: Int) -> Data? {
        let fd = open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
        guard fd >= 0 else { return nil }; defer { close(fd) }
        var info = stat()
        guard fstat(fd, &info) == 0, info.st_uid == 0, info.st_nlink == 1,
              (info.st_mode & S_IFMT) == S_IFREG, (info.st_mode & 0o777) == 0o644,
              info.st_size == length else { return nil }
        var bytes = [UInt8](repeating: 0, count: length)
        guard read(fd, &bytes, length) == length else { return nil }
        return Data(bytes)
    }
    private func installationMatches() -> Bool {
        let base = TinyTouchPAMConfiguration.directory + "/\(getuid())"
        guard let hash = try? TinyTouchPAMTrust.ownCodeHash(),
              rootFile(base + ".cdhash", length: 40) == Data(hash.utf8),
              let keyID = rootFile(base + ".keyid", length: 16) else { return false }
        var info = stat()
        guard lstat(TinyTouchPAMConfiguration.module, &info) == 0, info.st_uid == 0,
              (info.st_mode & S_IFMT) == S_IFREG, (info.st_mode & 0o777) == 0o644 else { return false }
        guard lstat(base + ".key", &info) == 0, info.st_uid == 0, info.st_nlink == 1,
              (info.st_mode & S_IFMT) == S_IFREG, (info.st_mode & 0o777) == 0o600, info.st_size == 32 else { return false }
        guard let sudo = try? String(contentsOfFile: "/private/etc/pam.d/sudo", encoding: .utf8), sudo.utf8.count <= 16384 else { return false }
        let usesLocal = sudo.split(separator: "\n").contains { $0.split(whereSeparator: \.isWhitespace).map(String.init) == ["auth", "include", "sudo_local"] }
        let policy = usesLocal ? "/private/etc/pam.d/sudo_local" : "/private/etc/pam.d/sudo"
        guard lstat(policy, &info) == 0, info.st_uid == 0, (info.st_mode & S_IFMT) == S_IFREG,
              (info.st_mode & 0o022) == 0, let contents = try? String(contentsOfFile: policy, encoding: .utf8),
              contents.utf8.count <= 16384,
              contents.split(separator: "\n").contains(where: { $0.trimmingCharacters(in: .whitespaces) == TinyTouchPAMConfiguration.line }) else { return false }
        if let key = channelKey {
            return keyID == Data(TinyTouchPAMConfiguration.keyID(key).utf8)
        }
        return true
    }
    func refresh() {
        installed = installationMatches()
        if setupReady && DispatchTime.now().uptimeNanoseconds >= exportDeadline { setupReady = false }
        guard !busy else { return }
        message = installed ? (enabled ? "Fingerprint approval for sudo is enabled. Keep tinyTouch Native open and connect USB." : "Sudo adapter installed for this app build. Enable fingerprint approval when ready.")
            : "Sudo adapter is not installed for this app build. Prepare setup, then run the administrator installer."
    }
    private func loadChannelKey(create: Bool) throws -> Data {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "tinyTouch.pam-channel-key", kSecAttrAccount as String: String(getuid()),
            kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        if status == errSecSuccess, let key = item as? Data, key.count == 32 { return key }
        // A denial, unreadable item or malformed existing key is never replaced.
        guard create, status == errSecItemNotFound else { throw TinyTouchAuthError.keychain }
        var bytes = [UInt8](repeating: 0, count: 32)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else { throw TinyTouchAuthError.invalidResponse }
        let key = Data(bytes)
        var access: SecAccess?
        guard SecAccessCreate("tinyTouch sudo approval key" as CFString, nil, &access) == errSecSuccess, let access else { throw TinyTouchAuthError.keychain }
        let record: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "tinyTouch.pam-channel-key", kSecAttrAccount as String: String(getuid()),
            kSecAttrLabel as String: "tinyTouch sudo approval", kSecAttrAccess as String: access,
            kSecValueData as String: key]
        guard SecItemAdd(record as CFDictionary, nil) == errSecSuccess else { throw TinyTouchAuthError.keychain }
        return key
    }
    func prepare() {
        guard !busy, server != nil else { return }
        do {
            channelKey = try loadChannelKey(create: true)
            exportDeadline = DispatchTime.now().uptimeNanoseconds + 300_000_000_000
            setupReady = true
            message = "Ready for the administrator installer for five minutes. Keep this app open. Setup requires your Mac administrator password."
        } catch { setupReady = false; message = "Could not access the sudo approval key. Allow the current app in Keychain and prepare setup again." }
    }
    func setEnabled(_ value: Bool) {
        if !value {
            cancellation?.cancel(); enabled = false; channelKey = nil; setupReady = false; exportDeadline = 0
            UserDefaults.standard.set(false, forKey: "tinyTouch.sudo.enabled"); refresh(); return
        }
        guard !busy, server != nil else { return }
        do {
            channelKey = try loadChannelKey(create: false)
            guard installationMatches() else { throw TinyTouchAuthError.invalidResponse }
            enabled = true; UserDefaults.standard.set(true, forKey: "tinyTouch.sudo.enabled"); refresh()
        } catch { enabled = false; message = "Enable failed. Install the adapter for this app build and allow its sudo key in Keychain." }
    }
    func cancel() { cancellation?.cancel() }
    private func authorize(_ request: TinyTouchPAMRequest, cancellation: TinyTouchAuthCancellation, completion: @escaping (Data?) -> Void) {
        guard enabled, !busy, connection.usbStatus?.authenticationSupported == true else { completion(nil); return }
        do { if channelKey == nil { channelKey = try loadChannelKey(create: false) } }
        catch { completion(nil); message = "Sudo approval key unavailable. Use the terminal password prompt."; return }
        guard installationMatches(), let key = channelKey, let device = connection.beginManagement() else {
            completion(nil); message = "USB fingerprint approval is unavailable. Use the terminal password prompt."; return
        }
        busy = true; self.cancellation = cancellation
        let identifier = UUID(); activeRequest = identifier
        message = "Approve sudo for \(request.user) · process \(request.pid). Reserving USB…"
        NotificationCenter.default.post(name: .openSettingsWindow, object: SettingsTab.permissions)
        let connection = self.connection
        Task { @MainActor [weak self] in
            let result = await Task.detached { () -> Result<Void, Error> in
                do {
                    try TinyTouchAuthUSB.authorize(device: device, context: request.context, cancellation: cancellation) { text in
                        Task { @MainActor [weak self] in
                            guard let self, self.activeRequest == identifier else { return }
                            self.message = "Sudo · \(request.user) · process \(request.pid)\n" + text
                        }
                    }
                    return .success(())
                } catch { return .failure(error) }
            }.value
            connection.endManagement(status: nil)
            guard let self, self.activeRequest == identifier else { completion(nil); return }
            self.activeRequest = nil; self.busy = false; self.cancellation = nil
            switch result {
            case .success where self.enabled && self.installationMatches():
                self.message = "Fresh fingerprint proof verified for this sudo authentication request."
                completion(key)
            case .failure(let error):
                self.message = error.localizedDescription + " Use the terminal password prompt."
                completion(nil)
            default:
                self.message = "Sudo approval was disabled. Use the terminal password prompt."
                completion(nil)
            }
        }
    }
}
