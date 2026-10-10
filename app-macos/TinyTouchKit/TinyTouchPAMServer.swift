import Foundation
import Darwin

/// Dedicated ESP32 PAM endpoint. The imported server and its pre-authorization
/// windows are never used. Only root peers, framed requests and fresh proofs
/// can produce a nonce-bound receipt.
public final class TinyTouchPAMServer: @unchecked Sendable {
    public typealias Authorize = (TinyTouchPAMRequest, TinyTouchAuthCancellation, @escaping (Data?) -> Void) -> Void
    private let path: String
    private let uid: UInt32
    private let user: String
    private let peerUID: uid_t
    private let authorize: Authorize
    private let installationKey: (@escaping (Data?) -> Void) -> Void
    private let queue = DispatchQueue(label: "tinyTouch.pam.socket")
    private let replay = TinyTouchPAMReplayGuard()
    private let lock = NSLock()
    private var listener: Int32 = -1
    private var generation: UInt64 = 0
    private var socketInode: ino_t = 0
    private var activeCancellation: TinyTouchAuthCancellation?
    public convenience init(directory: URL, authorize: @escaping Authorize,
                            installationKey: @escaping (@escaping (Data?) -> Void) -> Void) {
        self.init(directory: directory, uid: getuid(), user: NSUserName(), peerUID: 0,
                  authorize: authorize, installationKey: installationKey)
    }
    init(directory: URL, uid: UInt32, user: String, peerUID: uid_t, authorize: @escaping Authorize,
         installationKey: @escaping (@escaping (Data?) -> Void) -> Void) {
        self.path = directory.appendingPathComponent("pam.sock").path
        self.uid = uid; self.user = user; self.peerUID = peerUID
        self.authorize = authorize; self.installationKey = installationKey
    }
    deinit { stop() }
    public func start() throws {
        let directory = URL(fileURLWithPath: path).deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        var directoryInfo = stat()
        guard lstat(directory.path, &directoryInfo) == 0, (directoryInfo.st_mode & S_IFMT) == S_IFDIR,
              directoryInfo.st_uid == getuid(), (directoryInfo.st_mode & 0o022) == 0,
              path.utf8.count < MemoryLayout.size(ofValue: sockaddr_un().sun_path) else { throw TinyTouchAuthError.invalidResponse }
        lock.lock(); let alreadyRunning = listener >= 0; lock.unlock()
        guard !alreadyRunning else { throw TinyTouchAuthError.busy }
        // Recover a crashed listener, but never remove a live server, symlink or
        // regular file. Recheck the inode after probing the old endpoint.
        var old = stat()
        if lstat(path, &old) == 0 {
            guard (old.st_mode & S_IFMT) == S_IFSOCK, old.st_uid == getuid() else { throw TinyTouchAuthError.busy }
            let probe = socket(AF_UNIX, SOCK_STREAM, 0)
            guard probe >= 0 else { throw TinyTouchAuthError.busy }
            var noPipe: Int32 = 1
            guard setsockopt(probe, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size)) == 0 else { close(probe); throw TinyTouchAuthError.busy }
            var address = sockaddr_un(); address.sun_family = sa_family_t(AF_UNIX)
            address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
            withUnsafeMutableBytes(of: &address.sun_path) { $0.copyBytes(from: path.utf8); $0[path.utf8.count] = 0 }
            let result = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(probe, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) } }
            let failure = errno; close(probe)
            var current = stat()
            guard result != 0, failure == ECONNREFUSED, lstat(path, &current) == 0,
                  current.st_ino == old.st_ino, current.st_dev == old.st_dev,
                  unlink(path) == 0 else { throw TinyTouchAuthError.busy }
        } else if errno != ENOENT { throw TinyTouchAuthError.invalidResponse }
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else { throw TinyTouchAuthError.busy }
        var noPipe: Int32 = 1
        guard setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size)) == 0 else { close(fd); throw TinyTouchAuthError.busy }
        var address = sockaddr_un(); address.sun_family = sa_family_t(AF_UNIX)
        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        withUnsafeMutableBytes(of: &address.sun_path) { bytes in
            bytes.copyBytes(from: path.utf8); bytes[path.utf8.count] = 0
        }
        let bound = withUnsafePointer(to: &address) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) }
        }
        guard bound == 0 else { close(fd); throw TinyTouchAuthError.busy }
        guard chmod(path, 0o600) == 0, listen(fd, 4) == 0 else { close(fd); unlink(path); throw TinyTouchAuthError.busy }
        var info = stat(); _ = lstat(path, &info)
        lock.lock(); listener = fd; socketInode = info.st_ino; generation &+= 1; let token = generation; lock.unlock()
        queue.async { [weak self] in self?.acceptLoop(fd, generation: token) }
    }
    public func stop() {
        lock.lock(); let fd = listener; listener = -1; generation &+= 1; let inode = socketInode; let cancellation = activeCancellation; lock.unlock()
        cancellation?.cancel()
        if fd >= 0 {
            shutdown(fd, SHUT_RDWR); close(fd)
            var info = stat()
            if lstat(path, &info) == 0, info.st_ino == inode, (info.st_mode & S_IFMT) == S_IFSOCK { unlink(path) }
        }
    }
    private func acceptLoop(_ fd: Int32, generation token: UInt64) {
        while true {
            lock.lock(); let running = listener == fd && generation == token; lock.unlock()
            if !running { return }
            let client = accept(fd, nil, nil)
            if client < 0 { if errno == EINTR { continue }; return }
            handle(client); close(client)
        }
    }
    private func handle(_ fd: Int32) {
        var noPipe: Int32 = 1
        guard setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size)) == 0 else { return }
        var peer: uid_t = 0; var group: gid_t = 0
        var pid: Int32 = 0; var size = socklen_t(MemoryLayout<Int32>.size)
        guard getpeereid(fd, &peer, &group) == 0, peer == peerUID,
              getsockopt(fd, SOL_LOCAL, LOCAL_PEERPID, &pid, &size) == 0,
              let line = readLine(fd) else { sendLine("DENY\n", fd); return }
        if line == "INSTALL\n" {
            let reply = Pending()
            installationKey { reply.finish($0) }
            guard reply.signal.wait(timeout: .now() + 3) == .success, let key = reply.value, key.count == 32 else {
                sendLine("DENY\n", fd); return
            }
            sendLine("KEY:" + TinyTouchAuthRequest.hex(key) + "\n", fd); return
        }
        guard let request = try? TinyTouchPAMRequest(line: line, expectedUID: uid, expectedUser: user, peerPID: pid),
              replay.consume(request.nonce) else { sendLine("DENY\n", fd); return }
        let cancellation = TinyTouchAuthCancellation()
        lock.lock(); activeCancellation = cancellation; lock.unlock()
        defer { cancellation.cancel(); lock.lock(); activeCancellation = nil; lock.unlock() }
        let reply = Pending()
        authorize(request, cancellation) { reply.finish($0) }
        let deadline = DispatchTime.now().uptimeNanoseconds + 45_000_000_000
        while DispatchTime.now().uptimeNanoseconds < deadline {
            if !peerAlive(fd) || (try? cancellation.check()) == nil { return }
            if reply.signal.wait(timeout: .now() + 0.05) == .success {
                guard DispatchTime.now().uptimeNanoseconds < deadline, peerAlive(fd),
                      (try? cancellation.check()) != nil, let key = reply.value,
                      let receipt = try? request.receipt(key: key) else { sendLine("DENY\n", fd); return }
                sendLine(receipt, fd); return
            }
        }
        sendLine("DENY\n", fd)
    }
    private func readLine(_ fd: Int32) -> String? {
        var data = Data(); let deadline = DispatchTime.now().uptimeNanoseconds + 3_000_000_000
        while DispatchTime.now().uptimeNanoseconds < deadline {
            var poller = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
            if poll(&poller, 1, 100) <= 0 { continue }
            var bytes = [UInt8](repeating: 0, count: 256)
            let n = recv(fd, &bytes, bytes.count, MSG_DONTWAIT)
            guard n > 0 else { return nil }
            data.append(contentsOf: bytes.prefix(n))
            guard data.count <= 256 else { return nil }
            if let newline = data.firstIndex(of: 10) {
                guard newline == data.count - 1 else { return nil }
                return String(data: data, encoding: .utf8)
            }
        }
        return nil
    }
    private func peerAlive(_ fd: Int32) -> Bool {
        var poller = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
        _ = poll(&poller, 1, 0)
        if poller.revents & Int16(POLLERR | POLLHUP | POLLNVAL) != 0 { return false }
        // No additional data is permitted after a complete request.
        if poller.revents & Int16(POLLIN) != 0 { return false }
        return true
    }
    private func sendLine(_ line: String, _ fd: Int32) {
        let bytes = Data(line.utf8)
        var offset = 0
        let deadline = DispatchTime.now().uptimeNanoseconds + 1_000_000_000
        while offset < bytes.count && DispatchTime.now().uptimeNanoseconds < deadline {
            let n = bytes.withUnsafeBytes { send(fd, $0.baseAddress!.advanced(by: offset), bytes.count - offset, MSG_DONTWAIT) }
            if n > 0 { offset += n } else if errno == EAGAIN || errno == EINTR { Thread.sleep(forTimeInterval: 0.01) } else { return }
        }
    }
    private final class Pending: @unchecked Sendable {
        let signal = DispatchSemaphore(value: 0)
        private let lock = NSLock()
        private var completed = false
        private var key: Data?
        var value: Data? { lock.lock(); defer { lock.unlock() }; return key }
        func finish(_ value: Data?) {
            lock.lock(); guard !completed else { lock.unlock(); return }; completed = true; key = value; lock.unlock()
            signal.signal()
        }
    }
}
