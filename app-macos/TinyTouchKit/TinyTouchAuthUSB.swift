import Foundation
import Darwin

public final class TinyTouchAuthCancellation: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false
    public init() {}
    public func cancel() { lock.lock(); cancelled = true; lock.unlock() }
    func check() throws {
        lock.lock(); let value = cancelled; lock.unlock()
        if value { throw TinyTouchAuthError.cancelled }
    }
}

/// Shares the existing helper's schema-1 crash-safe foreground lease. No
/// launchd registration is changed; the matching acknowledgement is required
/// before opening CDC while the USB helper is running.
final class TinyTouchAuthLease {
    private var lockFD: Int32 = -1
    private var nonce: String?
    private let directory: URL
    private var lease: URL { directory.appendingPathComponent("helper-suspend") }
    private var ack: URL { directory.appendingPathComponent("helper-suspend-ack") }
    init(directory: URL) { self.directory = directory }
    deinit { release() }
    func acquire(waitForAck: Bool, cancellation: TinyTouchAuthCancellation, timeout: TimeInterval = 8) throws {
        guard lockFD == -1 else { throw TinyTouchAuthError.busy }
        try cancellation.check()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let path = directory.appendingPathComponent("helper-suspend.lock").path
        let fd = Darwin.open(path, O_CREAT | O_RDWR | O_NOFOLLOW, 0o600)
        guard fd >= 0 else { throw TinyTouchAuthError.lease }
        var info = stat()
        guard fstat(fd, &info) == 0, info.st_uid == geteuid(), (info.st_mode & S_IFMT) == S_IFREG,
              info.st_nlink == 1, flock(fd, LOCK_EX | LOCK_NB) == 0 else {
            close(fd); throw TinyTouchAuthError.busy
        }
        lockFD = fd; nonce = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        do {
            if FileManager.default.fileExists(atPath: ack.path) { try FileManager.default.removeItem(at: ack) }
            let bytes = try JSONSerialization.data(withJSONObject: ["schema": 1, "pid": Int(getpid()),
                "nonce": nonce!, "acquired_at": Date().timeIntervalSince1970])
            let stage = directory.appendingPathComponent(".native-lease-" + UUID().uuidString)
            let output = Darwin.open(stage.path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
            guard output >= 0 else { throw TinyTouchAuthError.lease }
            let written = bytes.withUnsafeBytes { Darwin.write(output, $0.baseAddress!, bytes.count) }
            let synced = fsync(output); close(output)
            defer { try? FileManager.default.removeItem(at: stage) }
            guard written == bytes.count, synced == 0, Darwin.rename(stage.path, lease.path) == 0 else { throw TinyTouchAuthError.lease }
            if waitForAck {
                let deadline = DispatchTime.now().uptimeNanoseconds + UInt64(timeout * 1_000_000_000)
                while DispatchTime.now().uptimeNanoseconds < deadline {
                    try cancellation.check()
                    if Self.record(ack)?["nonce"] as? String == nonce { return }
                    Thread.sleep(forTimeInterval: 0.05)
                }
                throw TinyTouchAuthError.lease
            }
        } catch { release(); throw error }
    }
    private static func record(_ path: URL) -> [String: Any]? {
        guard let size = try? FileManager.default.attributesOfItem(atPath: path.path)[.size] as? NSNumber,
              size.intValue <= 4096, let data = try? Data(contentsOf: path),
              let fields = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return fields
    }
    func release() {
        guard lockFD >= 0 else { return }
        if let nonce {
            for path in [lease, ack] where Self.record(path)?["nonce"] as? String == nonce {
                try? FileManager.default.removeItem(at: path)
            }
        }
        nonce = nil; flock(lockFD, LOCK_UN); close(lockFD); lockFD = -1
    }
}

final class TinyTouchAuthSerial {
    private var fd: Int32
    private var buffer = Data()
    private let cancellation: TinyTouchAuthCancellation
    init(device: TinyTouchUSBDevice, cancellation: TinyTouchAuthCancellation) throws {
        guard TinyTouchUSBDevice.connected().contains(device) else { throw TinyTouchError.wrongDevice }
        let handle = Darwin.open(device.path, O_RDWR | O_NOCTTY | O_NONBLOCK | O_NOFOLLOW)
        guard handle >= 0 else { throw TinyTouchAuthError.transport(stage: "open", code: errno) }
        var configured = false
        var exclusive = false
        defer {
            if !configured {
                if exclusive { ioctl(handle, UInt(TIOCNXCL)) }
                close(handle)
            }
        }
        var attributes = termios()
        guard tcgetattr(handle, &attributes) == 0 else { throw TinyTouchAuthError.transport(stage: "read configuration", code: errno) }
        cfmakeraw(&attributes)
        guard cfsetspeed(&attributes, speed_t(B115200)) == 0 else { throw TinyTouchAuthError.transport(stage: "baud rate", code: errno) }
        withUnsafeMutableBytes(of: &attributes.c_cc) { bytes in
            bytes[Int(VMIN)] = 1; bytes[Int(VTIME)] = 0
        }
        attributes.c_cflag |= tcflag_t(CLOCAL | CREAD)
        guard tcsetattr(handle, TCSANOW, &attributes) == 0 else { throw TinyTouchAuthError.transport(stage: "configuration", code: errno) }
        guard ioctl(handle, UInt(TIOCEXCL)) == 0 else { throw TinyTouchAuthError.transport(stage: "exclusive access", code: errno) }
        exclusive = true
        guard ioctl(handle, UInt(TIOCSDTR)) == 0 else { throw TinyTouchAuthError.transport(stage: "DTR", code: errno) }
        tcflush(handle, TCIOFLUSH)
        fd = handle; self.cancellation = cancellation
        configured = true
        Thread.sleep(forTimeInterval: 0.2)
    }
    deinit { closePort() }
    func closePort() {
        if fd >= 0 {
            ioctl(fd, UInt(TIOCCDTR)) // Explicitly cancel pending device requests.
            ioctl(fd, UInt(TIOCNXCL)) // Release only our exclusive CDC ownership.
            close(fd); fd = -1
        }
    }
    func exchange(_ command: String, prefix: String, timeout: TimeInterval,
                  progress: (String) -> Void) throws -> String {
        let deadline = DispatchTime.now().uptimeNanoseconds + UInt64(timeout * 1_000_000_000)
        let bytes = Data((command + "\n").utf8)
        var offset = 0
        while offset < bytes.count {
            try wait(Int16(POLLOUT), deadline: deadline)
            let written = bytes.withUnsafeBytes { Darwin.write(fd, $0.baseAddress!.advanced(by: offset), bytes.count - offset) }
            if written < 0 && (errno == EAGAIN || errno == EINTR) { continue }
            guard written > 0 else { throw TinyTouchAuthError.transport(stage: "write", code: errno) }
            offset += written
        }
        var total = 0
        while true {
            try cancellation.check()
            while let end = buffer.firstIndex(of: 10) {
                let bytes = buffer.prefix(upTo: end); buffer.removeSubrange(...end)
                guard bytes.count <= 1024, let line = String(data: bytes, encoding: .utf8)?.trimmingCharacters(in: .newlines) else { throw TinyTouchAuthError.invalidResponse }
                if line.hasPrefix("ERR ") { throw TinyTouchAuthError.rejected }
                if line == "EVENT TOUCH" { progress("Touch an enrolled finger now, then lift it."); continue }
                if line.hasPrefix(prefix) { return line }
                if !line.isEmpty { throw TinyTouchAuthError.invalidResponse }
            }
            guard buffer.count <= 1024, total <= 65536 else { throw TinyTouchAuthError.invalidResponse }
            try wait(Int16(POLLIN), deadline: deadline)
            var chunk = [UInt8](repeating: 0, count: 512)
            let count = Darwin.read(fd, &chunk, chunk.count)
            if count < 0 && (errno == EAGAIN || errno == EINTR) { continue }
            // A nonblocking CDC read can be empty without a hangup. Poll again
            // within the same deadline; POLLHUP is rejected by wait().
            if count == 0 { Thread.sleep(forTimeInterval: 0.01); continue }
            guard count > 0 else { throw TinyTouchAuthError.transport(stage: "read", code: errno) }
            total += count; buffer.append(contentsOf: chunk.prefix(count))
        }
    }
    private func wait(_ event: Int16, deadline: UInt64) throws {
        while DispatchTime.now().uptimeNanoseconds < deadline {
            try cancellation.check()
            var descriptor = pollfd(fd: fd, events: event, revents: 0)
            let result = poll(&descriptor, 1, 100)
            if result < 0 && errno == EINTR { continue }
            guard result >= 0 else { throw TinyTouchAuthError.transport(stage: "poll", code: errno) }
            guard descriptor.revents & Int16(POLLERR | POLLHUP | POLLNVAL) == 0 else { throw TinyTouchAuthError.disconnected }
            if descriptor.revents & event != 0 { return }
        }
        throw TinyTouchAuthError.expired
    }
}

public enum TinyTouchAuthUSB {
    private static func reserve(cancellation: TinyTouchAuthCancellation) throws -> TinyTouchAuthLease {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let agent = home.appendingPathComponent("Library/LaunchAgents/com.tinytouch.helper.plist")
        if FileManager.default.fileExists(atPath: agent.path) {
            guard let data = try? Data(contentsOf: agent),
                  let plist = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any],
                  let environment = plist["EnvironmentVariables"] as? [String: String], environment["TINYTOUCH_SERVICE_SCHEMA"] == "3" else { throw TinyTouchAuthError.lease }
        }
        let lease = TinyTouchAuthLease(directory: home.appendingPathComponent("Library/Application Support/tinyTouch"))
        try lease.acquire(waitForAck: TinyTouchHelperHealth.read(label: "com.tinytouch.helper") == .running, cancellation: cancellation)
        return lease
    }
    public static func checkLease() throws {
        let lease = try reserve(cancellation: TinyTouchAuthCancellation())
        lease.release()
    }
    /// Read-only hardware diagnosis; never opens Keychain or prompts the sensor.
    public static func checkTransport(device: TinyTouchUSBDevice) throws -> TinyTouchStatus {
        let cancellation = TinyTouchAuthCancellation()
        let lease = try reserve(cancellation: cancellation)
        defer { lease.release() }
        let serial = try TinyTouchAuthSerial(device: device, cancellation: cancellation)
        defer { serial.closePort() }
        return try readStatus(serial: serial, progress: { _ in })
    }
    private static func readStatus(serial: TinyTouchAuthSerial, progress: (String) -> Void) throws -> TinyTouchStatus {
        let pong = try serial.exchange("PING", prefix: "PONG 6", timeout: 3, progress: progress)
        guard pong == "PONG 6" else { throw TinyTouchAuthError.invalidResponse }
        let line = try serial.exchange("STATUS", prefix: "OK STATUS ", timeout: 4, progress: progress)
        var fields: [String: String] = [:]
        for field in line.dropFirst(10).split(separator: " ") {
            let parts = field.split(separator: "=", maxSplits: 1)
            guard parts.count == 2, fields[String(parts[0])] == nil else { throw TinyTouchAuthError.invalidResponse }
            fields[String(parts[0])] = String(parts[1])
        }
        return try TinyTouchStatus(data: JSONEncoder().encode(fields))
    }
    public static func test(device: TinyTouchUSBDevice, cancellation: TinyTouchAuthCancellation,
                            progress: (String) -> Void) throws {
        let lease = try reserve(cancellation: cancellation)
        defer { lease.release() }
        progress("Opening the exclusive USB connection…")
        let serial = try TinyTouchAuthSerial(device: device, cancellation: cancellation)
        defer { serial.closePort() } // Release CDC before resuming the helper.
        progress("Checking firmware support for this fingerprint request…")
        let status = try readStatus(serial: serial, progress: progress)
        guard status.authenticationSupported, status.sensorReady, status.fingerprints > 0 else { throw TinyTouchAuthError.unsupported }
        progress("Allow the pairing-key read if Mac Keychain asks. Your saved password is not read.")
        let key = try TinyTouchPairingKey.read(serial: device.serial)
        try cancellation.check()
        let request = try TinyTouchAuthRequest(key: key, serial: device.serial,
            context: Data(("native-auth-test-v1|" + device.serial).utf8))
        defer { request.invalidate() }
        let challenge = try serial.exchange(request.beginCommand, prefix: "OK AUTH2 CHALLENGE ", timeout: 3, progress: progress)
        let prove = try request.proveCommand(challenge: challenge)
        let result = try serial.exchange(prove, prefix: "OK AUTH2 MATCH ", timeout: 12, progress: progress)
        try request.verifyMatch(result)
        try cancellation.check()
        guard TinyTouchUSBDevice.connected().contains(device) else { throw TinyTouchError.wrongDevice }
    }
}
