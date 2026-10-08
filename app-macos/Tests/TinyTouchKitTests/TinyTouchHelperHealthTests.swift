import XCTest
@testable import TinyTouchKit

final class TinyTouchHelperHealthTests: XCTestCase {
    func testRunningHelperMetadata() throws {
        let data = try PropertyListSerialization.data(fromPropertyList: ["Label": "com.tinytouch.helper", "PID": 123], format: .xml, options: 0)
        XCTAssertEqual(try TinyTouchHelperHealth(data: data), .running)
    }
    func testLoadedIsNotRunning() throws {
        let data = try PropertyListSerialization.data(fromPropertyList: ["Label": "com.tinytouch.bluetooth", "LastExitStatus": 0], format: .xml, options: 0)
        XCTAssertEqual(try TinyTouchHelperHealth(data: data), .loaded)
    }
    func testOpenStepLaunchctlOutput() throws {
        XCTAssertEqual(try TinyTouchHelperHealth(data: Data("{\n \"Label\" = \"com.tinytouch.helper\";\n \"PID\" = 123;\n}".utf8)), .running)
    }
    func testLaunchctlArraySyntaxAndNestedMetadata() throws {
        let output = "{\n\t\"Label\" = \"com.tinytouch.helper\";\n\t\"PID\" = 123;\n\t\"ProgramArguments\" = (\n\t\t\"/helper\";\n\t\t\"--helper\";\n\t);\n}"
        XCTAssertEqual(try TinyTouchHelperHealth(data: Data((output + ";\n").utf8)), .running)
        let noPid = output.replacingOccurrences(of: "\t\"PID\" = 123;", with: "\t\t\"PID\" = 123;")
        XCTAssertEqual(try TinyTouchHelperHealth(data: Data(noPid.utf8)), .loaded)
    }
    func testRejectsUnrelatedMalformedAndOversizedData() throws {
        let unrelated = try PropertyListSerialization.data(fromPropertyList: ["Label": "other", "PID": 123], format: .xml, options: 0)
        for data in [unrelated, Data("broken".utf8), Data(repeating: 32, count: 65537)] {
            XCTAssertThrowsError(try TinyTouchHelperHealth(data: data))
        }
        XCTAssertEqual(TinyTouchHelperHealth.read(label: "other"), .unavailable)
    }
}
