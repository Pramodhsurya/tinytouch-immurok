import Foundation
import TinyTouchKit

do {
    guard [2, 3].contains(CommandLine.arguments.count) else { throw TinyTouchError.backendMissing }
    guard let device = try TinyTouchUSBDevice.select(TinyTouchUSBDevice.connected(), expected: nil) else {
        throw TinyTouchError.wrongDevice
    }
    let status = try TinyTouchUSB.status(device: device, backend: URL(fileURLWithPath: CommandLine.arguments[1]))
    print("\(device.serial): USB protocol 6, firmware \(status.firmware), build \(status.build), sensor \(status.sensorReady ? "ready" : "offline"), templates \(status.fingerprints), hosts \(status.hosts)")
    if CommandLine.arguments.count == 3 {
        guard CommandLine.arguments[2] == "--management" else { throw TinyTouchError.invalidStatus }
        let backend = URL(fileURLWithPath: CommandLine.arguments[1])
        let runner = TinyTouchCommandRunner()
        let inventory = try TinyTouchFingerprintInventory(output: runner.run(device: device, backend: backend, command: .inventory) { _ in })
        let settings = try TinyTouchDeviceSettings(data: Data(runner.run(device: device, backend: backend, command: .settings) { _ in }.utf8))
        let hosts = try TinyTouchHostInventory(output: runner.run(device: device, backend: backend, command: .hosts) { _ in })
        guard hosts.identifiers.count == status.hosts else { throw TinyTouchError.invalidStatus }
        print("Host inventory verified: \(hosts.identifiers.count) registered, capacity \(hosts.capacity)")
        print("Management read verified: slots \(inventory.groups.sorted { $0.key < $1.key }), free blocks \(inventory.available), settings \(settings.values.count)")
    }
} catch {
    fputs("\(error.localizedDescription)\n", stderr)
    exit(1)
}
