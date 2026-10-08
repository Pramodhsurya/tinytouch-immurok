import XCTest
@testable import TinyTouchKit

final class TinyTouchTests: XCTestCase {
    private func status(_ changes: [String: String] = [:]) throws -> TinyTouchStatus {
        var fields = ["protocol": "6", "firmware": "0.1.35", "build": "esp32-baseline-1", "sensor": "ready", "fingerprints": "1", "hosts": "1", "mode": "hid"]
        fields.merge(changes) { _, new in new }
        return try TinyTouchStatus(data: JSONEncoder().encode(fields))
    }
    func testBaselineAndOfflineSensor() throws {
        XCTAssertEqual(try status().fingerprints, 1)
        XCTAssertFalse(try status(["sensor": "offline", "fingerprints": "-1"]).sensorReady)
    }
    func testRejectsOtherProtocolAndInvalidCounts() {
        XCTAssertThrowsError(try status(["protocol": "7"]))
        XCTAssertThrowsError(try status(["fingerprints": "41"]))
        XCTAssertThrowsError(try status(["fingerprints": "garbage"]))
        XCTAssertThrowsError(try status(["hosts": "-1"]))
        XCTAssertThrowsError(try status(["sensor": "unknown"]))
        XCTAssertThrowsError(try status(["build": ""]))
    }
    func testMalformedAndOversizedStatusFailClosed() {
        XCTAssertThrowsError(try TinyTouchStatus(data: Data("not JSON".utf8)))
        XCTAssertThrowsError(try TinyTouchStatus(data: Data(repeating: 32, count: 65537)))
        XCTAssertThrowsError(try TinyTouchStatus(data: Data("{\"protocol\":6}".utf8)))
    }
    func testUSBIdentityFilter() {
        XCTAssertTrue(TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: "TT-90706911C494"))
        XCTAssertFalse(TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: "tinyTouch"))
        XCTAssertFalse(TinyTouchUSBDevice.accepts(vendor: 1, product: 0x4001, serial: "TT-90706911C494"))
    }
    func testBluetoothIdentityRequiresExactConfiguredSerial() {
        let serial = "TT-90706911C494"
        XCTAssertTrue(TinyTouchIdentity.matches(Data(serial.utf8), expected: serial))
        XCTAssertFalse(TinyTouchIdentity.matches(Data("TT-000000000000".utf8), expected: serial))
        XCTAssertFalse(TinyTouchIdentity.matches(Data((serial + "\u{0}").utf8), expected: serial))
        XCTAssertFalse(TinyTouchIdentity.matches(Data([0xff]), expected: serial))
        XCTAssertFalse(TinyTouchIdentity.matches(Data(repeating: 65, count: 65), expected: serial))
        XCTAssertFalse(TinyTouchIdentity.matches(Data("tinyTouch".utf8), expected: "tinyTouch"))
    }
    func testNeverSelectsFirstOrWrongDevice() throws {
        let one = TinyTouchUSBDevice(path: "/dev/cu.one", serial: "TT-90706911C494")
        let two = TinyTouchUSBDevice(path: "/dev/cu.two", serial: "TT-000000000000")
        XCTAssertThrowsError(try TinyTouchUSBDevice.select([one, two], expected: nil))
        XCTAssertEqual(try TinyTouchUSBDevice.select([one, two], expected: one.serial), one)
        XCTAssertNil(try TinyTouchUSBDevice.select([two], expected: one.serial))
    }
}
