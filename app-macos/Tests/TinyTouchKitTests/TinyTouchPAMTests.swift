import XCTest
import Darwin
@testable import TinyTouchKit

final class TinyTouchPAMTests: XCTestCase {
    private let nonce = String(repeating: "a1", count: 32)
    private let tag = "04178544916311c05fb201200846e857b6f8028074de518747d2f4611020a8a2"
    private func request(_ line: String? = nil, uid: UInt32 = 501, user: String = "fixture", pid: Int32 = 1234) throws -> TinyTouchPAMRequest {
        try TinyTouchPAMRequest(line: line ?? "AUTH:\(uid):\(user):sudo:\(pid):\(nonce)\n", expectedUID: uid, expectedUser: user, peerPID: pid)
    }
    func testStrictRequestIdentityAndService() throws {
        let valid = try request()
        XCTAssertEqual(valid.context, Data("tinyTouch-pam-action-v1|501|fixture|sudo|1234|\(nonce)".utf8))
        for bad in ["AUTH:502:fixture:sudo:1234:\(nonce)\n", "AUTH:501:other:sudo:1234:\(nonce)\n",
                    "AUTH:501:fixture:sudo:1235:\(nonce)\n", "AUTH:501:fixture:login:1234:\(nonce)\n",
                    "AUTH:501:fixture:sudo:01234:\(nonce)\n", "AUTH:501:fixture:sudo:1234:\(nonce.uppercased())\n",
                    "AUTH:501:fixture:sudo:1234:aa\n", "AUTH:501:fixture:sudo:1234:\(nonce)",
                    "AUTH:501:fixture:sudo:1234:\(nonce)\nextra\n", "OK\n", String(repeating: "a", count: 257)] {
            XCTAssertThrowsError(try request(bad))
        }
        XCTAssertThrowsError(try request(uid: 501, user: "bad user"))
        XCTAssertThrowsError(try request(pid: 0))
        _ = try request("AUTH:501:fixture:sudo_local:1234:\(nonce)\n")
    }
    func testCrossLanguageReceiptAndAllContextFields() throws {
        let key = Data(0..<32), original = try request().receipt(key: key)
        XCTAssertEqual(original, "OK:\(tag)\n") // Independent Python/C fixture.
        XCTAssertNotEqual(try request(uid: 502).receipt(key: key), original)
        XCTAssertNotEqual(try request(user: "other").receipt(key: key), original)
        XCTAssertNotEqual(try request(pid: 1235).receipt(key: key), original)
        XCTAssertNotEqual(try request("AUTH:501:fixture:sudo_local:1234:\(nonce)\n").receipt(key: key), original)
        XCTAssertNotEqual(try request("AUTH:501:fixture:sudo:1234:\(String(repeating: "a2", count: 32))\n").receipt(key: key), original)
        XCTAssertNotEqual(try request().receipt(key: Data(repeating: 1, count: 32)), original)
        XCTAssertThrowsError(try request().receipt(key: Data()))
        XCTAssertEqual(TinyTouchPAMConfiguration.keyID(key), "630dcd2966c43366")
    }
    func testReplayGuardConsumesFailuresBoundsMemoryAndHandlesClockRollback() {
        var now: UInt64 = 100
        let guarder = TinyTouchPAMReplayGuard(now: { now })
        XCTAssertTrue(guarder.consume(nonce)); XCTAssertFalse(guarder.consume(nonce))
        now = 99; XCTAssertFalse(guarder.consume(nonce))
        for i in 0..<255 { XCTAssertTrue(guarder.consume(String(i))) }
        XCTAssertFalse(guarder.consume("overflow"))
        now = 120_000_000_101
        XCTAssertTrue(guarder.consume(nonce))
    }
    func testPAMConfigurationPreservesProvidersAndIsIdempotent() throws {
        let original = "# custom\nauth sufficient pam_smartcard.so\nauth required pam_opendirectory.so\naccount required pam_permit.so\n"
        let enabled = try TinyTouchPAMConfiguration.updated(original, enabled: true)
        XCTAssertEqual(enabled, TinyTouchPAMConfiguration.line + "\n" + original)
        XCTAssertEqual(try TinyTouchPAMConfiguration.updated(enabled, enabled: true), enabled)
        XCTAssertEqual(try TinyTouchPAMConfiguration.updated(enabled, enabled: false), original)
        let custom = original + "auth optional /other/pam.so\n"
        XCTAssertEqual(try TinyTouchPAMConfiguration.updated(custom, enabled: false), custom)
        XCTAssertThrowsError(try TinyTouchPAMConfiguration.updated("a\0b", enabled: true))
        XCTAssertThrowsError(try TinyTouchPAMConfiguration.updated(String(repeating: "a", count: 16385), enabled: true))
    }
    private func directory() throws -> URL {
        let path = URL(fileURLWithPath: "/private/tmp/tt-pam-" + UUID().uuidString.prefix(8))
        try FileManager.default.createDirectory(at: path, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        addTeardownBlock { try? FileManager.default.removeItem(at: path) }
        return path
    }
    private func connectTo(_ directory: URL) throws -> Int32 {
        let path = directory.appendingPathComponent("pam.sock").path
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else { throw TinyTouchAuthError.busy }
        var noPipe: Int32 = 1
        _ = setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size))
        var address = sockaddr_un(); address.sun_family = sa_family_t(AF_UNIX); address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        withUnsafeMutableBytes(of: &address.sun_path) { $0.copyBytes(from: path.utf8); $0[path.utf8.count] = 0 }
        let rc = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) } }
        guard rc == 0 else { close(fd); throw TinyTouchAuthError.busy }
        var timeout = timeval(tv_sec: 3, tv_usec: 0)
        _ = setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
        _ = setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size))
        return fd
    }
    private func sendText(_ text: String, fd: Int32) { let bytes = Array(text.utf8); _ = bytes.withUnsafeBytes { send(fd, $0.baseAddress, bytes.count, 0) } }
    private func receive(_ fd: Int32) -> String {
        var data = Data(), bytes = [UInt8](repeating: 0, count: 256)
        while true {
            let n = recv(fd, &bytes, bytes.count, 0)
            if n <= 0 { return String(data: data, encoding: .utf8) ?? "" }
            data.append(contentsOf: bytes.prefix(n))
            if data.contains(10) { return String(data: data, encoding: .utf8) ?? "" }
        }
    }
    func testProductionEndpointRejectsNonRootBeforeAnyCallback() throws {
        guard getuid() != 0 else { throw XCTSkip("Non-root fixture required") }
        let path = try directory()
        let server = TinyTouchPAMServer(directory: path, authorize: { _, _, _ in XCTFail("untrusted approval") }, installationKey: { _ in XCTFail("untrusted key export") })
        try server.start(); defer { server.stop() }
        for text in ["INSTALL\n", "AUTH:\(getuid()):\(NSUserName()):sudo:\(getpid()):\(nonce)\n"] {
            let fd = try connectTo(path); sendText(text, fd: fd)
            XCTAssertEqual(receive(fd), "DENY\n"); close(fd)
        }
    }
    func testFragmentedSocketRequestFreshReceiptAndReplayDenial() throws {
        let path = try directory(), approved = expectation(description: "fresh request")
        let server = TinyTouchPAMServer(directory: path, uid: getuid(), user: "fixture", peerUID: getuid(), authorize: { _, _, completion in
            approved.fulfill(); completion(Data(0..<32))
        }, installationKey: { $0(nil) })
        try server.start(); defer { server.stop() }
        let line = "AUTH:\(getuid()):fixture:sudo:\(getpid()):\(nonce)\n"
        let fd = try connectTo(path)
        sendText(String(line.prefix(8)), fd: fd); sendText(String(line.dropFirst(8)), fd: fd)
        let expected = try request(line, uid: getuid(), user: "fixture", pid: getpid()).receipt(key: Data(0..<32))
        XCTAssertEqual(receive(fd), expected); close(fd)
        wait(for: [approved], timeout: 1)
        let repeated = try connectTo(path); sendText(line, fd: repeated)
        XCTAssertEqual(receive(repeated), "DENY\n"); close(repeated)
    }
    func testCancelledOrDisconnectedSocketNeverProducesReceipt() throws {
        let path = try directory(), disconnected = expectation(description: "disconnected request cancelled")
        let server = TinyTouchPAMServer(directory: path, uid: getuid(), user: "fixture", peerUID: getuid(), authorize: { request, cancellation, completion in
            if request.nonce == String(repeating: "a1", count: 32) { cancellation.cancel(); completion(Data(0..<32)); return }
            DispatchQueue.global().async {
                for _ in 0..<100 {
                    if (try? cancellation.check()) == nil { disconnected.fulfill(); completion(Data(0..<32)); return }
                    Thread.sleep(forTimeInterval: 0.01)
                }
                XCTFail("Disconnect must cancel fresh proof"); completion(nil)
            }
        }, installationKey: { $0(nil) })
        try server.start(); defer { server.stop() }
        let cancelled = try connectTo(path)
        sendText("AUTH:\(getuid()):fixture:sudo:\(getpid()):\(nonce)\n", fd: cancelled)
        XCTAssertFalse(receive(cancelled).hasPrefix("OK:")); close(cancelled)
        let fd = try connectTo(path)
        sendText("AUTH:\(getuid()):fixture:sudo:\(getpid()):\(String(repeating: "b2", count: 32))\n", fd: fd)
        Thread.sleep(forTimeInterval: 0.05); close(fd)
        wait(for: [disconnected], timeout: 2)
    }
    func testSocketCannotReplaceRegularFileSymlinkOrLiveServer() throws {
        let path = try directory(), socketPath = path.appendingPathComponent("pam.sock")
        let make = { TinyTouchPAMServer(directory: path, authorize: { _, _, complete in complete(nil) }, installationKey: { $0(nil) }) }
        try Data("keep".utf8).write(to: socketPath)
        XCTAssertThrowsError(try make().start()); XCTAssertEqual(try Data(contentsOf: socketPath), Data("keep".utf8))
        try FileManager.default.removeItem(at: socketPath)
        try FileManager.default.createSymbolicLink(at: socketPath, withDestinationURL: path.appendingPathComponent("target"))
        XCTAssertThrowsError(try make().start()); try FileManager.default.removeItem(at: socketPath)
        let one = make(); try one.start(); defer { one.stop() }
        XCTAssertThrowsError(try make().start())
    }
    func testStaleSocketRecoveredAfterCrash() throws {
        let path = try directory(), fd = socket(AF_UNIX, SOCK_STREAM, 0)
        var address = sockaddr_un(); address.sun_family = sa_family_t(AF_UNIX); address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        let name = path.appendingPathComponent("pam.sock").path
        withUnsafeMutableBytes(of: &address.sun_path) { $0.copyBytes(from: name.utf8); $0[name.utf8.count] = 0 }
        XCTAssertEqual(withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) } }, 0)
        close(fd)
        let server = TinyTouchPAMServer(directory: path, authorize: { _, _, complete in complete(nil) }, installationKey: { $0(nil) })
        try server.start(); server.stop()
        XCTAssertFalse(FileManager.default.fileExists(atPath: name))
    }
    func testAuditTokenPeerSignatureRejectsWrongBuild() throws {
        let path = try directory()
        let server = TinyTouchPAMServer(directory: path, uid: getuid(), user: "fixture", peerUID: getuid(), authorize: { _, _, complete in complete(nil) }, installationKey: { $0(nil) })
        try server.start(); defer { server.stop() }
        let fd = try connectTo(path); defer { close(fd) }
        let hash = try TinyTouchPAMTrust.ownCodeHash()
        XCTAssertEqual(hash.count, 40)
        try TinyTouchPAMTrust.verifyPeer(fd: fd, expectedHash: hash)
        XCTAssertThrowsError(try TinyTouchPAMTrust.verifyPeer(fd: fd, expectedHash: String(repeating: "0", count: 40)))
        XCTAssertThrowsError(try TinyTouchPAMTrust.verifyPeer(fd: fd, expectedHash: "bad hash"))
        sendText("INSTALL\n", fd: fd); XCTAssertEqual(receive(fd), "DENY\n")
    }
}
