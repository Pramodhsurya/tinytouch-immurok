import XCTest
@testable import TinyTouchKit

final class ManagementTests: XCTestCase {
    func testPreservesPartialExistingFingerprintInventory() throws {
        let inventory = try TinyTouchFingerprintInventory(output: "Finger 10: Partially enrolled: 1 of 4 fingerprint views.\nSpace for 9 additional fingers.\n")
        XCTAssertEqual(inventory.groups, [10: 1]); XCTAssertEqual(inventory.available, 9)
    }
    func testEmptyFullAndPendingInventory() throws {
        XCTAssertEqual(try TinyTouchFingerprintInventory(output: "No fingerprints enrolled.\nSpace for 10 additional fingers.\n").groups, [:])
        let inventory = try TinyTouchFingerprintInventory(output: "Finger 1: Enrolled: 4 fingerprint views.\nFinger 2: Cleanup pending. Reconnect the device.\nSpace for 8 additional fingers.\n")
        XCTAssertEqual(inventory.groups, [1: 4, 2: -1])
    }
    func testCleanupPendingSlotCannotServeAsBackupAuthenticationFinger() throws {
        let inventory = try TinyTouchFingerprintInventory(output: "Finger 10: Partially enrolled: 1 of 4 fingerprint views.\nFinger 1: Cleanup pending. Reconnect the device.\nSpace for 8 additional fingers.\n")
        XCTAssertFalse(inventory.canRemoveOrReplace(slot: 10))
        XCTAssertTrue(inventory.canRemoveOrReplace(slot: 1))
        XCTAssertFalse(inventory.canRemoveOrReplace(slot: 2))
        let two = try TinyTouchFingerprintInventory(output: "Finger 1: Enrolled: 4 fingerprint views.\nFinger 10: Partially enrolled: 1 of 4 fingerprint views.\nSpace for 8 additional fingers.\n")
        XCTAssertTrue(two.canRemoveOrReplace(slot: 10))
    }
    func testRejectsInconsistentAndMalformedInventory() {
        for output in ["", "Finger 11: Enrolled: 4 fingerprint views.\nSpace for 9 additional fingers.", "Finger 1: Partially enrolled: 5 of 4 fingerprint views.\nSpace for 9 additional fingers.", "No fingerprints enrolled.\nFinger 1: Enrolled: 4 fingerprint views.\nSpace for 9 additional fingers.", "Finger 1: Enrolled: 4 fingerprint views.\nFinger 1: Enrolled: 4 fingerprint views.\nSpace for 8 additional fingers.", "No fingerprints enrolled.\nSpace for 11 additional fingers."] {
            XCTAssertThrowsError(try TinyTouchFingerprintInventory(output: output))
        }
    }
    func testTypedActionsValidateSlotsAndSettingsWithoutShellInterpolation() throws {
        XCTAssertEqual(try TinyTouchManagementCommand.enroll(1, replace: false).arguments, ["enroll", "1"])
        XCTAssertEqual(try TinyTouchManagementCommand.enroll(10, replace: true).arguments, ["enroll", "10", "--replace"])
        XCTAssertEqual(try TinyTouchManagementCommand.set(.typingDelay, "7").arguments, ["config", "typing_delay_ms", "7"])
        XCTAssertThrowsError(try TinyTouchManagementCommand.delete(0).arguments)
        XCTAssertThrowsError(try TinyTouchManagementCommand.enroll(11, replace: false).arguments)
        XCTAssertThrowsError(try TinyTouchManagementCommand.set(.typingDelay, "101").arguments)
        XCTAssertThrowsError(try TinyTouchManagementCommand.set(.typingDelay, "7;echo x").arguments)
        XCTAssertThrowsError(try TinyTouchManagementCommand.set(.idleColor, "#ffffff").arguments)
    }
    func testSettingsRequireKnownModeAndValidReportedValues() throws {
        let data = Data("{\"mode\":\"hid\",\"led\":\"on\",\"typing_delay_ms\":\"1\"}".utf8)
        XCTAssertEqual(try TinyTouchDeviceSettings(data: data).values["typing_delay_ms"], "1")
        XCTAssertThrowsError(try TinyTouchDeviceSettings(data: Data("{\"mode\":\"hid\",\"typing_delay_ms\":\"0\"}".utf8)))
        XCTAssertThrowsError(try TinyTouchDeviceSettings(data: Data("{}".utf8)))
    }
    func testRunnerStreamsOutputAndRejectsFailure() throws {
        let runner = TinyTouchCommandRunner()
        var progress = ""
        let output = try runner.execute(backend: URL(fileURLWithPath: "/bin/sh"), arguments: ["-c", "printf 'Touch the sensor\\n'"], timeout: 2) { progress = $0 }
        XCTAssertEqual(output, "Touch the sensor\n"); XCTAssertEqual(progress, output)
        XCTAssertThrowsError(try TinyTouchCommandRunner().execute(backend: URL(fileURLWithPath: "/bin/sh"), arguments: ["-c", "exit 1"], timeout: 2) { _ in })
    }
    func testRunnerTimeoutAndOutputBoundReapChild() {
        XCTAssertThrowsError(try TinyTouchCommandRunner().execute(backend: URL(fileURLWithPath: "/bin/sh"), arguments: ["-c", "exec sleep 20"], timeout: 0.1) { _ in })
        XCTAssertThrowsError(try TinyTouchCommandRunner().execute(backend: URL(fileURLWithPath: "/usr/bin/head"), arguments: ["-c", "65537", "/dev/zero"], timeout: 2) { _ in })
    }
    func testCancelBeforeStartCannotLaunchCommand() {
        let runner = TinyTouchCommandRunner(); runner.cancel()
        XCTAssertThrowsError(try runner.execute(backend: URL(fileURLWithPath: "/bin/sh"), arguments: ["-c", "exit 0"], timeout: 2) { _ in }) {
            XCTAssertTrue($0 is CancellationError)
        }
    }
    func testInventoryReadRecoversWithoutReturningTheFailedAttempt() throws {
        var attempts = 0
        let valid = "Finger 1: Enrolled: 4 fingerprint views.\nSpace for 9 additional fingers.\n"
        let output = try TinyTouchCommandRunner().readWithRetry(command: .inventory, progress: { _ in }) {
            attempts += 1
            if attempts == 1 { throw TinyTouchError.inventoryUnavailable(stage: .parameters) }
            return valid
        }
        XCTAssertEqual(attempts, 2)
        XCTAssertEqual(try TinyTouchFingerprintInventory(output: output).groups, [1: 4])
    }
    func testPersistentInventoryFailureRemainsAnError() {
        var attempts = 0
        XCTAssertThrowsError(try TinyTouchCommandRunner().readWithRetry(command: .inventory, progress: { _ in }) {
            attempts += 1
            throw TinyTouchError.inventoryUnavailable(stage: .index)
        }) {
            guard case TinyTouchError.inventoryUnavailable(stage: .index) = $0 else { return XCTFail("Lost inventory diagnostic") }
        }
        XCTAssertEqual(attempts, 3)
    }
    func testWritesAndInvalidInventoryNeverRetry() {
        for command in [TinyTouchManagementCommand.delete(2), .enroll(2, replace: false), .set(.typingDelay, "2"), .removeHost("0011223344556677")] {
            var attempts = 0
            XCTAssertThrowsError(try TinyTouchCommandRunner().readWithRetry(command: command, progress: { _ in }) {
                attempts += 1
                throw TinyTouchError.inventoryUnavailable(stage: .parameters)
            })
            XCTAssertEqual(attempts, 1)
        }
        for failure in [TinyTouchError.invalidStatus, .wrongDevice, .timedOut, .commandFailed, .inventoryUnavailable(stage: .capacity), .inventoryUnavailable(stage: .profiles), .inventoryUnavailable(stage: .indexBounds)] {
            var attempts = 0
            XCTAssertThrowsError(try TinyTouchCommandRunner().readWithRetry(command: .inventory, progress: { _ in }) {
                attempts += 1; throw failure
            })
            XCTAssertEqual(attempts, 1)
        }
    }
    func testCancellationDuringInventoryBackoffPreventsAnotherCommand() {
        let runner = TinyTouchCommandRunner()
        var attempts = 0
        XCTAssertThrowsError(try runner.readWithRetry(command: .inventory, progress: { _ in }) {
            attempts += 1; runner.cancel()
            throw TinyTouchError.inventoryUnavailable(stage: .busy)
        }) { XCTAssertTrue($0 is CancellationError) }
        XCTAssertEqual(attempts, 1)
    }
    func testBackendDiagnosticsUseOnlyKnownStagesWithoutEchoingStderr() {
        let prefix = "Error: tinyTouch rejected the request: FINGER inventory_unavailable reason="
        for stage in TinyTouchInventoryStage.allCases {
            guard case .inventoryUnavailable(let actual) = TinyTouchInventoryStage.backendFailure(Data((prefix + stage.rawValue + "\n").utf8)) else { return XCTFail("Missing known diagnostic") }
            XCTAssertEqual(actual, stage)
        }
        for text in [prefix + "private-value", prefix + "parameters\nprivate-value", "private-value", String(repeating: "x", count: 65537)] {
            guard case .commandFailed = TinyTouchInventoryStage.backendFailure(Data(text.utf8)) else { return XCTFail("Unsafe stderr accepted") }
        }
    }
    func testRunnerPreservesSafeInventoryFailureStage() {
        XCTAssertThrowsError(try TinyTouchCommandRunner().execute(backend: URL(fileURLWithPath: "/bin/sh"),
            arguments: ["-c", "printf 'Error: tinyTouch rejected the request: FINGER inventory_unavailable reason=parameters\\n' >&2; exit 1"], timeout: 2) { _ in }) {
            guard case TinyTouchError.inventoryUnavailable(stage: .parameters) = $0 else { return XCTFail("Missing inventory failure stage") }
        }
    }
    func testCancelInFlightTerminatesCommand() {
        let runner = TinyTouchCommandRunner()
        let complete = expectation(description: "cancelled command returned")
        DispatchQueue.global().async {
            defer { complete.fulfill() }
            do {
                _ = try runner.execute(backend: URL(fileURLWithPath: "/bin/sh"), arguments: ["-c", "exec sleep 20"], timeout: 10) { _ in }
                XCTFail("Cancelled command succeeded")
            } catch { XCTAssertTrue(error is CancellationError) }
        }
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.2) { runner.cancel() }
        wait(for: [complete], timeout: 3)
    }
}

final class FingerprintNameTests: XCTestCase {
    func testNamesPersistPerDeviceAndReset() throws {
        let suite = "tinyTouch.names.tests." + UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let serial = "TT-90706911C494"
        try TinyTouchFingerprintNames.save(name: "  Right thumb  ", slot: 10, serial: serial, defaults: defaults)
        XCTAssertEqual(TinyTouchFingerprintNames.load(serial: serial, defaults: defaults), [10: "Right thumb"])
        XCTAssertEqual(TinyTouchFingerprintNames.load(serial: "TT-000000000000", defaults: defaults), [:])
        try TinyTouchFingerprintNames.save(name: "", slot: 10, serial: serial, defaults: defaults)
        XCTAssertEqual(TinyTouchFingerprintNames.load(serial: serial, defaults: defaults), [:])
    }
    func testInvalidNamesAndSlotsDoNotOverwrite() {
        let suite = "tinyTouch.names.tests." + UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        for name in [String(repeating: "A", count: 65), "Thumb\u{0}other", "Thumb\nother"] {
            XCTAssertThrowsError(try TinyTouchFingerprintNames.save(name: name, slot: 1, serial: "TT-90706911C494", defaults: defaults))
        }
        XCTAssertThrowsError(try TinyTouchFingerprintNames.save(name: "Thumb", slot: 11, serial: "TT-90706911C494", defaults: defaults))
        XCTAssertThrowsError(try TinyTouchFingerprintNames.save(name: "Thumb", slot: 1, serial: "tinyTouch", defaults: defaults))
    }
}
