import XCTest
@testable import TinyTouchKit

final class TinyTouchHostTests: XCTestCase {
    func testReadsEightSlotESP32Inventory() throws {
        let hosts = try TinyTouchHostInventory(output: "tinyTouch\nHID computers (2 of 8):\n  1111111111111111\n  0000000000000000\n")
        XCTAssertEqual(hosts.identifiers, ["0000000000000000", "1111111111111111"])
        XCTAssertEqual(hosts.capacity, 8)
        XCTAssertTrue(hosts.canRemove("1111111111111111"))
        XCTAssertFalse(hosts.canRemove("2222222222222222"))
    }
    func testLastHostIsProtected() throws {
        let hosts = try TinyTouchHostInventory(output: "HID computers (1 of 8):\n  1111111111111111\n")
        XCTAssertFalse(hosts.canRemove("1111111111111111"))
        let empty = try TinyTouchHostInventory(output: "HID computers (0 of 8):\n")
        XCTAssertEqual(empty.identifiers, [])
    }
    func testRejectsInvalidCountsDuplicatesAndIdentifiers() {
        for output in ["HID computers (1 of 8):\n", "HID computers (2 of 8):\n  1111111111111111\n  1111111111111111\n", "HID computers (1 of 9):\n  1111111111111111\n", "HID computers (1 of 8):\n  secret\n", "HID computers (1 of 8):\n  1111111111111111\nHID computers (1 of 8):\n", "HID computers (1 of 8):\n  AAAAAAAAAAAAAAAA\n", "HID computers (-1 of 8):\n"] {
            XCTAssertThrowsError(try TinyTouchHostInventory(output: output), output)
        }
    }
    func testTypedRemovalRejectsCommandInjection() throws {
        XCTAssertEqual(try TinyTouchManagementCommand.hosts.arguments, ["computers", "list"])
        XCTAssertEqual(try TinyTouchManagementCommand.removeHost("1111111111111111").arguments, ["computers", "remove", "1111111111111111"])
        for identifier in ["1111111111111111\nRESET FACTORY", "111111111111111", "FFFFFFFFFFFFFFFF", "--all"] {
            XCTAssertThrowsError(try TinyTouchManagementCommand.removeHost(identifier).arguments)
        }
    }
    func testRejectsUnexpectedTailAndOversize() {
        XCTAssertThrowsError(try TinyTouchHostInventory(output: "HID computers (0 of 8):\nOK AUTH\n"))
        XCTAssertThrowsError(try TinyTouchHostInventory(output: String(repeating: "x", count: 65537)))
    }
}
