import XCTest
@testable import TinyTouchKit

final class TinyTouchAuthLeaseTests: XCTestCase {
    private func fixture() throws -> URL {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false)
        addTeardownBlock { try? FileManager.default.removeItem(at: directory) }
        return directory
    }
    func testExclusiveLeaseAndMatchingCleanup() throws {
        let directory = try fixture(), one = TinyTouchAuthLease(directory: directory), two = TinyTouchAuthLease(directory: directory)
        try one.acquire(waitForAck: false, cancellation: TinyTouchAuthCancellation())
        let path = directory.appendingPathComponent("helper-suspend")
        XCTAssertEqual(try FileManager.default.attributesOfItem(atPath: path.path)[.posixPermissions] as? NSNumber, 0o600)
        XCTAssertThrowsError(try two.acquire(waitForAck: false, cancellation: TinyTouchAuthCancellation()))
        one.release()
        XCTAssertFalse(FileManager.default.fileExists(atPath: path.path))
        try two.acquire(waitForAck: false, cancellation: TinyTouchAuthCancellation()); two.release()
    }
    func testTimeoutAndCancellationReleaseLockAndRecord() throws {
        let directory = try fixture(), lease = TinyTouchAuthLease(directory: directory)
        XCTAssertThrowsError(try lease.acquire(waitForAck: true, cancellation: TinyTouchAuthCancellation(), timeout: 0.05))
        XCTAssertFalse(FileManager.default.fileExists(atPath: directory.appendingPathComponent("helper-suspend").path))
        let cancelled = TinyTouchAuthCancellation(); cancelled.cancel()
        XCTAssertThrowsError(try lease.acquire(waitForAck: false, cancellation: cancelled))
        try lease.acquire(waitForAck: false, cancellation: TinyTouchAuthCancellation()); lease.release()
    }
    func testDoesNotDeleteAnotherOwnersRecord() throws {
        let directory = try fixture(), lease = TinyTouchAuthLease(directory: directory)
        try lease.acquire(waitForAck: false, cancellation: TinyTouchAuthCancellation())
        let path = directory.appendingPathComponent("helper-suspend")
        try Data("{\"nonce\":\"other-owner\"}".utf8).write(to: path)
        lease.release()
        XCTAssertTrue(FileManager.default.fileExists(atPath: path.path))
    }
    func testSymlinkLockCannotBeUsed() throws {
        let directory = try fixture(), target = directory.appendingPathComponent("target")
        try Data().write(to: target)
        try FileManager.default.createSymbolicLink(at: directory.appendingPathComponent("helper-suspend.lock"), withDestinationURL: target)
        XCTAssertThrowsError(try TinyTouchAuthLease(directory: directory).acquire(waitForAck: false, cancellation: TinyTouchAuthCancellation()))
    }
    func testMatchingAcknowledgementAndStaleAcknowledgementRemoval() throws {
        let directory = try fixture(), lease = TinyTouchAuthLease(directory: directory)
        let ack = directory.appendingPathComponent("helper-suspend-ack")
        try Data("{\"nonce\":\"stale\"}".utf8).write(to: ack)
        DispatchQueue.global().async {
            for _ in 0..<100 {
                if let data = try? Data(contentsOf: directory.appendingPathComponent("helper-suspend")),
                   let record = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let nonce = record["nonce"] as? String,
                   let reply = try? JSONSerialization.data(withJSONObject: ["schema": 1, "pid": 123, "nonce": nonce]) {
                    try? reply.write(to: ack, options: .atomic); return
                }
                Thread.sleep(forTimeInterval: 0.01)
            }
        }
        try lease.acquire(waitForAck: true, cancellation: TinyTouchAuthCancellation(), timeout: 2)
        lease.release()
        XCTAssertFalse(FileManager.default.fileExists(atPath: ack.path))
    }
}
