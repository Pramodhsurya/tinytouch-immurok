import Foundation
import Darwin
import TinyTouchKit
import Security

// Root-only setup; no password/PIV/login service or other PAM provider is altered.
// The package supplies this build's signed app hash. Keys are transferred over
// a root-only socket after audit-token signature verification, never a user file.
func trustedFile(_ path: String, mode: mode_t? = nil) -> Bool {
    var info = stat()
    return lstat(path, &info) == 0 && info.st_uid == 0 && info.st_nlink == 1 &&
        (info.st_mode & S_IFMT) == S_IFREG && (info.st_mode & 0o022) == 0 &&
        (mode == nil || (info.st_mode & 0o777) == mode!)
}
func pathExists(_ path: String) throws -> Bool {
    var info = stat()
    if lstat(path, &info) == 0 { return true }
    guard errno == ENOENT else { throw TinyTouchAuthError.invalidResponse }
    return false
}
func ensureDirectory(_ path: String) throws {
    let url = URL(fileURLWithPath: path)
    if path != "/" { try ensureDirectory(url.deletingLastPathComponent().path) }
    var info = stat()
    if lstat(path, &info) != 0 {
        guard errno == ENOENT else { throw TinyTouchAuthError.invalidResponse }
        guard mkdir(path, 0o755) == 0 else { throw TinyTouchAuthError.invalidResponse }
    }
    guard lstat(path, &info) == 0, info.st_uid == 0, (info.st_mode & S_IFMT) == S_IFDIR,
          (info.st_mode & 0o022) == 0 else { throw TinyTouchAuthError.invalidResponse }
}
func atomicWrite(_ bytes: Data, path: String, mode: mode_t) throws {
    let parent = URL(fileURLWithPath: path).deletingLastPathComponent().path
    try ensureDirectory(parent)
    let temporary = parent + "/.tinytouch-" + UUID().uuidString
    let fd = open(temporary, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC, mode)
    guard fd >= 0 else { throw TinyTouchAuthError.invalidResponse }
    defer { close(fd); unlink(temporary) }
    let n = bytes.withUnsafeBytes { write(fd, $0.baseAddress, bytes.count) }
    guard n == bytes.count, fchown(fd, 0, 0) == 0, fchmod(fd, mode) == 0,
          fsync(fd) == 0, rename(temporary, path) == 0 else { throw TinyTouchAuthError.invalidResponse }
}
func socketKey(home: String, expectedHash: String, uid: UInt32) throws -> Data {
    let path = home + "/Library/Application Support/tinyTouch/pam.sock"
    var address = sockaddr_un(); address.sun_family = sa_family_t(AF_UNIX)
    address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
    guard path.utf8.count < MemoryLayout.size(ofValue: address.sun_path) else { throw TinyTouchAuthError.invalidResponse }
    withUnsafeMutableBytes(of: &address.sun_path) { $0.copyBytes(from: path.utf8); $0[path.utf8.count] = 0 }
    let fd = socket(AF_UNIX, SOCK_STREAM, 0)
    guard fd >= 0 else { throw TinyTouchAuthError.invalidResponse }; defer { close(fd) }
    var timeout = timeval(tv_sec: 3, tv_usec: 0); var noPipe: Int32 = 1
    _ = setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
    _ = setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
    _ = setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size))
    let connected = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) } }
    var peer: uid_t = 0; var group: gid_t = 0
    guard connected == 0, getpeereid(fd, &peer, &group) == 0, peer == uid else { throw TinyTouchAuthError.invalidResponse }
    try TinyTouchPAMTrust.verifyPeer(fd: fd, expectedHash: expectedHash)
    let request = Array("INSTALL\n".utf8)
    guard request.withUnsafeBytes({ send(fd, $0.baseAddress, request.count, 0) }) == request.count else { throw TinyTouchAuthError.invalidResponse }
    var bytes = Data()
    while bytes.count < 70 {
        var chunk = [UInt8](repeating: 0, count: 70)
        let n = recv(fd, &chunk, chunk.count, 0)
        guard n > 0 else { throw TinyTouchAuthError.invalidResponse }
        bytes.append(contentsOf: chunk.prefix(n))
        if bytes.contains(10) { break }
    }
    guard bytes.count == 69, let text = String(data: bytes, encoding: .utf8), text.hasPrefix("KEY:"), text.hasSuffix("\n") else { throw TinyTouchAuthError.invalidResponse }
    let hex = text.dropFirst(4).dropLast()
    guard hex.range(of: "^[0-9a-f]{64}$", options: .regularExpression) != nil else { throw TinyTouchAuthError.invalidResponse }
    var key = Data(); var i = hex.startIndex
    while i < hex.endIndex { let j = hex.index(i, offsetBy: 2); key.append(UInt8(hex[i..<j], radix: 16)!); i = j }
    return key
}
do {
    let args = Array(CommandLine.arguments.dropFirst())
    guard args.count >= 2, ["install", "remove"].contains(args[0]), let uid = UInt32(args[1]), uid > 0,
          geteuid() == 0, let passwd = getpwuid(uid), let home = passwd.pointee.pw_dir else { throw TinyTouchAuthError.invalidResponse }
    let base = TinyTouchPAMConfiguration.directory
    try ensureDirectory("/private/etc/pam.d")
    guard trustedFile("/private/etc/pam.d/sudo") else { throw TinyTouchAuthError.invalidResponse }
    let sudo = try String(contentsOfFile: "/private/etc/pam.d/sudo", encoding: .utf8)
    let target = sudo.split(separator: "\n").contains { $0.split(whereSeparator: \.isWhitespace).map(String.init) == ["auth", "include", "sudo_local"] }
        ? "/private/etc/pam.d/sudo_local" : "/private/etc/pam.d/sudo"
    let present = try pathExists(target)
    guard !present || trustedFile(target) else { throw TinyTouchAuthError.invalidResponse }
    let original = present ? try String(contentsOfFile: target, encoding: .utf8) : "# tinyTouch optional sudo provider\n"
    if args[0] == "install" {
        try ensureDirectory(URL(fileURLWithPath: TinyTouchPAMConfiguration.module).deletingLastPathComponent().path)
        guard args.count == 3, args[2].range(of: "^[0-9a-f]{40}$", options: .regularExpression) != nil,
              trustedFile(TinyTouchPAMConfiguration.module, mode: 0o644) else { throw TinyTouchAuthError.invalidResponse }
        let key = try socketKey(home: String(cString: home), expectedHash: args[2], uid: uid)
        let updated = try TinyTouchPAMConfiguration.updated(original, enabled: true)
        try ensureDirectory(base)
        let backup = base + "/\(uid).sudo-backup"
        if try !pathExists(backup) { try atomicWrite(Data(original.utf8), path: backup, mode: 0o600) }
        else { guard trustedFile(backup, mode: 0o600) else { throw TinyTouchAuthError.invalidResponse } }
        try atomicWrite(key, path: base + "/\(uid).key", mode: 0o600)
        try atomicWrite(Data(args[2].utf8), path: base + "/\(uid).cdhash", mode: 0o644)
        let keyID = TinyTouchPAMConfiguration.keyID(key)
        try atomicWrite(Data(keyID.utf8), path: base + "/\(uid).keyid", mode: 0o644)
        // Enable last, so an interrupted install falls back to the password.
        try atomicWrite(Data(updated.utf8), path: target, mode: 0o444)
        print("tinyTouch sudo provider installed. Enable it in the native app's Features tab.")
    } else {
        guard args.count == 2 else { throw TinyTouchAuthError.invalidResponse }
        try ensureDirectory(base)
        let otherKeys = try FileManager.default.contentsOfDirectory(atPath: base).filter { $0.hasSuffix(".key") && $0 != "\(uid).key" }
        if otherKeys.isEmpty { try atomicWrite(Data(try TinyTouchPAMConfiguration.updated(original, enabled: false).utf8), path: target, mode: 0o444) }
        for suffix in ["key", "cdhash", "keyid"] {
            let path = base + "/\(uid).\(suffix)"
            if try pathExists(path) { guard trustedFile(path) else { throw TinyTouchAuthError.invalidResponse }; guard unlink(path) == 0 else { throw TinyTouchAuthError.invalidResponse } }
        }
        print("tinyTouch sudo provider removed for this user. Existing password providers are preserved.")
    }
} catch {
    fputs("tinyTouch sudo setup did not complete. Open the current native app, click Prepare sudo setup, and retry the installer. System authorization was not configured by this helper.\n", stderr)
    exit(1)
}
