import Foundation
import IOKit
import Darwin

public enum TinyTouchError: Error, LocalizedError {
    case invalidStatus, unsupportedProtocol, wrongDevice, ambiguousDevices, backendMissing, commandFailed, timedOut, lastFingerprint, lastHost
    public var errorDescription: String? {
        switch self {
        case .invalidStatus: return "The device returned an invalid status."
        case .unsupportedProtocol: return "This app requires tinyTouch protocol 6."
        case .wrongDevice: return "The device identity does not match the configured tinyTouch."
        case .ambiguousDevices: return "Multiple tinyTouch devices are connected. Select a device before continuing."
        case .backendMissing: return "The bundled tinyTouch USB backend is missing."
        case .commandFailed: return "The USB status request failed. Reconnect the device and retry."
        case .timedOut: return "The USB command timed out."
        case .lastHost: return "Keep the last registered computer. Add another computer before removing this one."
        case .lastFingerprint: return "Enroll another finger before deleting or replacing this slot."
        }
    }
}

/// Counts are physical sensor templates, not immurok's six-slot bitmap.
public struct TinyTouchStatus: Equatable {
    public let firmware: String
    public let build: String
    public let sensorReady: Bool
    public let fingerprints: Int
    public let hosts: Int
    public let mode: String
    public let authProofSupported: Bool
    public let authFreshSupported: Bool
    public var authenticationSupported: Bool { authProofSupported && authFreshSupported }
    public init(data: Data) throws {
        guard data.count <= 65536,
              let fields = try? JSONDecoder().decode([String: String].self, from: data),
              let firmware = fields["firmware"], !firmware.isEmpty,
              let build = fields["build"], !build.isEmpty,
              let sensor = fields["sensor"], ["ready", "offline"].contains(sensor),
              let fingerprints = Int(fields["fingerprints"] ?? ""), (-1...40).contains(fingerprints),
              let hosts = Int(fields["hosts"] ?? ""), hosts >= 0, hosts <= 255,
              let mode = fields["mode"], ["hid", "piv"].contains(mode)
        else { throw TinyTouchError.invalidStatus }
        guard fields["protocol"] == "6" else { throw TinyTouchError.unsupportedProtocol }
        if let value = fields["auth_proof"], !["0", "1"].contains(value) { throw TinyTouchError.invalidStatus }
        if let value = fields["auth_fresh"], !["0", "1"].contains(value) { throw TinyTouchError.invalidStatus }
        authProofSupported = fields["auth_proof"] == "1"
        authFreshSupported = fields["auth_fresh"] == "1"
        self.firmware = firmware; self.build = build; self.sensorReady = sensor == "ready"
        self.fingerprints = fingerprints; self.hosts = hosts; self.mode = mode
    }
}

public struct TinyTouchUSBDevice: Equatable {
    public let path: String
    public let serial: String
    public static func accepts(vendor: Int, product: Int, serial: String) -> Bool {
        vendor == 0x303a && product == 0x4001 && serial.range(of: "^TT-[0-9A-F]{12}$", options: .regularExpression) != nil
    }
    public static func connected() -> [Self] {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOSerialBSDClient"), &iterator) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(iterator) }
        var result: [Self] = []
        while case let entry = IOIteratorNext(iterator), entry != 0 {
            defer { IOObjectRelease(entry) }
            func property(_ key: String) -> Any? {
                IORegistryEntrySearchCFProperty(entry, kIOServicePlane, key as CFString, kCFAllocatorDefault,
                                               IOOptionBits(kIORegistryIterateRecursively | kIORegistryIterateParents))
            }
            guard let path = property("IOCalloutDevice") as? String,
                  let serial = property("USB Serial Number") as? String,
                  let vendor = property("idVendor") as? NSNumber,
                  let product = property("idProduct") as? NSNumber,
                  accepts(vendor: vendor.intValue, product: product.intValue, serial: serial) else { continue }
            result.append(Self(path: path, serial: serial))
        }
        return result.sorted { $0.path < $1.path }
    }
    public static func select(_ devices: [Self], expected: String?) throws -> Self? {
        let candidates = devices.filter { expected == nil || $0.serial == expected }
        guard candidates.count <= 1 else { throw TinyTouchError.ambiguousDevices }
        return candidates.first
    }
}

public enum TinyTouchIdentity {
    public static func matches(_ data: Data, expected: String) -> Bool {
        guard data.count <= 64,
              TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: expected),
              let serial = String(data: data, encoding: .utf8) else { return false }
        return serial == expected
    }
}

public enum TinyTouchUSB {
    /// Uses the signed CLI's foreground lease, which pauses/resumes the existing USB helper.
    /// No password or pairing key enters this process. Output is bounded and never logged.
    public static func status(device: TinyTouchUSBDevice, backend: URL, timeout: TimeInterval = 12) throws -> TinyTouchStatus {
        guard FileManager.default.isExecutableFile(atPath: backend.path) else { throw TinyTouchError.backendMissing }
        guard TinyTouchUSBDevice.connected().contains(device) else { throw TinyTouchError.wrongDevice }
        let output = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        FileManager.default.createFile(atPath: output.path, contents: nil, attributes: [.posixPermissions: 0o600])
        defer { try? FileManager.default.removeItem(at: output) }
        let handle = try FileHandle(forWritingTo: output)
        defer { try? handle.close() }
        let process = Process()
        process.executableURL = backend
        process.arguments = ["--port", device.path, "status"]
        process.standardOutput = handle
        process.standardError = FileHandle.nullDevice
        process.standardInput = FileHandle.nullDevice
        try process.run()
        let deadline = Date().addingTimeInterval(timeout)
        while process.isRunning {
            let size = (try? FileManager.default.attributesOfItem(atPath: output.path)[.size] as? NSNumber)?.intValue ?? 0
            if Date() >= deadline || size > 65536 {
                process.terminate()
                let grace = Date().addingTimeInterval(1)
                while process.isRunning && Date() < grace { Thread.sleep(forTimeInterval: 0.02) }
                if process.isRunning { kill(process.processIdentifier, SIGKILL) }
                process.waitUntilExit()
                throw TinyTouchError.timedOut
            }
            Thread.sleep(forTimeInterval: 0.05)
        }
        guard process.terminationStatus == 0 else { throw TinyTouchError.commandFailed }
        guard TinyTouchUSBDevice.connected().contains(device) else { throw TinyTouchError.wrongDevice }
        return try TinyTouchStatus(data: Data(contentsOf: output))
    }
}
