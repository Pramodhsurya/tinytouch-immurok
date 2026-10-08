import XCTest
@testable import TinyTouchKit

final class TinyTouchSetupReadinessTests: XCTestCase {
    private func status(sensor: String = "ready", hosts: String = "1", mode: String = "hid") throws -> TinyTouchStatus {
        let data = try JSONSerialization.data(withJSONObject: ["protocol": "6", "firmware": "0.1.35", "build": "esp32-baseline-1", "sensor": sensor, "fingerprints": "1", "hosts": hosts, "mode": mode])
        return try TinyTouchStatus(data: data)
    }
    func testExistingPartialFingerCanCommissionWithoutReplacement() throws {
        let inventory = try TinyTouchFingerprintInventory(output: "Finger 10: Partially enrolled: 1 of 4 fingerprint views.\nSpace for 9 additional fingers.\n")
        XCTAssertTrue(TinyTouchSetupReadiness.canFinish(status: try status(), inventory: inventory, currentSerial: "TT-90706911C494", checkedSerial: "TT-90706911C494", outputConfirmed: true))
    }
    func testIdentityAndHumanTestAreBothRequired() throws {
        let inventory = try TinyTouchFingerprintInventory(output: "Finger 10: Partially enrolled: 1 of 4 fingerprint views.\nSpace for 9 additional fingers.\n")
        for (current, checked, confirmed) in [(nil, "TT-A", true), ("TT-A", "TT-B", true), ("TT-A", "TT-A", false)] as [(String?, String?, Bool)] {
            XCTAssertFalse(TinyTouchSetupReadiness.canFinish(status: try status(), inventory: inventory, currentSerial: current, checkedSerial: checked, outputConfirmed: confirmed))
        }
    }
    func testSensorHostModeAndUsableFingerRequired() throws {
        let inventory = try TinyTouchFingerprintInventory(output: "Finger 10: Partially enrolled: 1 of 4 fingerprint views.\nSpace for 9 additional fingers.\n")
        for status in [try status(sensor: "offline"), try status(hosts: "0"), try status(mode: "piv")] {
            XCTAssertFalse(TinyTouchSetupReadiness.canFinish(status: status, inventory: inventory, currentSerial: "TT-A", checkedSerial: "TT-A", outputConfirmed: true))
        }
        let pending = try TinyTouchFingerprintInventory(output: "Finger 10: Cleanup pending. Reconnect the device.\nSpace for 9 additional fingers.\n")
        for inventory in [pending, nil] {
            XCTAssertFalse(TinyTouchSetupReadiness.canFinish(status: try status(), inventory: inventory, currentSerial: "TT-A", checkedSerial: "TT-A", outputConfirmed: true))
        }
    }
}
