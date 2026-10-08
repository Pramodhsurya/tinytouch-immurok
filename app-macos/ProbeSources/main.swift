import Foundation
import TinyTouchKit

do {
    guard CommandLine.arguments.count == 2 else { throw TinyTouchError.backendMissing }
    guard let device = try TinyTouchUSBDevice.select(TinyTouchUSBDevice.connected(), expected: nil) else {
        throw TinyTouchError.wrongDevice
    }
    let status = try TinyTouchUSB.status(device: device, backend: URL(fileURLWithPath: CommandLine.arguments[1]))
    print("\(device.serial): USB protocol 6, firmware \(status.firmware), build \(status.build), sensor \(status.sensorReady ? "ready" : "offline"), templates \(status.fingerprints), hosts \(status.hosts)")
} catch {
    fputs("\(error.localizedDescription)\n", stderr)
    exit(1)
}
