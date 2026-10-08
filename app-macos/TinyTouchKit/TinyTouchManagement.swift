import Foundation
import Darwin

public struct TinyTouchFingerprintInventory: Equatable {
    /// A value of -1 means cleanup pending; 1...3 means partial enrollment.
    public let groups: [Int: Int]
    public let available: Int
    public func canRemoveOrReplace(slot: Int) -> Bool {
        groups[slot] != nil && groups.contains { $0.key != slot && $0.value > 0 }
    }
    public init(output: String) throws {
        var groups: [Int: Int] = [:]
        var available: Int?
        var explicitlyEmpty = false
        for line in output.split(separator: "\n").map(String.init) {
            if line == "No fingerprints enrolled." { explicitlyEmpty = true; continue }
            if line.hasPrefix("Space for "), line.hasSuffix(" additional fingers.") {
                guard available == nil, let count = Int(line.dropFirst(10).dropLast(20)), (0...10).contains(count) else { throw TinyTouchError.invalidStatus }
                available = count; continue
            }
            guard line.hasPrefix("Finger ") else { continue }
            let parts = line.split(separator: ":", maxSplits: 1)
            guard parts.count == 2, let slot = Int(parts[0].dropFirst(7)), (1...10).contains(slot), groups[slot] == nil else { throw TinyTouchError.invalidStatus }
            let description = parts[1].trimmingCharacters(in: .whitespaces)
            let count: Int
            if description == "Enrolled: 4 fingerprint views." { count = 4 }
            else if description == "Cleanup pending. Reconnect the device." { count = -1 }
            else if description.hasPrefix("Partially enrolled: "), description.hasSuffix(" of 4 fingerprint views."),
                    let value = Int(description.dropFirst(20).dropLast(24)), (1...3).contains(value) { count = value }
            else { throw TinyTouchError.invalidStatus }
            groups[slot] = count
        }
        guard let available, !groups.isEmpty || explicitlyEmpty, !(explicitlyEmpty && !groups.isEmpty), groups.count + available <= 10 else { throw TinyTouchError.invalidStatus }
        self.groups = groups; self.available = available
    }
}

public enum TinyTouchSetting: String, CaseIterable {
    case typingDelay = "typing_delay_ms", submitEnter = "submit_enter", cooldown = "touch_cooldown_ms"
    case led, idleColor = "led_idle_color", successColor = "led_success_color", failureColor = "led_failure_color"
    case endColor = "led_idle_end_color", effect = "led_idle_effect", cycles = "led_idle_cycles", feedback = "led_feedback_ms"
    public var title: String {
        switch self {
        case .typingDelay: return "Typing delay (ms)"
        case .submitEnter: return "Submit Enter"
        case .cooldown: return "Touch cooldown (ms)"
        case .led: return "Sensor lighting"
        case .idleColor: return "Idle color"
        case .successColor: return "Success color"
        case .failureColor: return "Failure color"
        case .endColor: return "Animation end color"
        case .effect: return "Idle animation"
        case .cycles: return "Animation repeats (0 = continuous)"
        case .feedback: return "Result feedback (ms)"
        }
    }
    public var choices: [String] {
        switch self {
        case .submitEnter: return ["off", "on"]
        case .led: return ["off", "on", "only-auth"]
        case .idleColor, .successColor, .failureColor, .endColor: return ["off", "blue", "green", "red", "cyan", "purple", "yellow", "white"]
        case .effect: return ["steady", "breathe", "flash", "fade-in", "fade-out"]
        default: return []
        }
    }
    public var range: ClosedRange<Int>? {
        switch self {
        case .typingDelay: return 1...100
        case .cooldown: return 100...5000
        case .cycles: return 0...255
        case .feedback: return 50...2000
        default: return nil
        }
    }
    public func accepts(_ value: String) -> Bool {
        if let range { return Int(value).map { range.contains($0) && String($0) == value } ?? false }
        return choices.contains(value)
    }
}

public enum TinyTouchInventoryStage: String, CaseIterable {
    case profiles, parameters, capacity, index, indexBounds = "index_bounds", countConsistency = "count_consistency", busy
    var retryable: Bool { self == .parameters || self == .index || self == .countConsistency || self == .busy }
    // Accept only the device's finite diagnostic vocabulary, never arbitrary
    // backend stderr (which may contain sensitive or unrelated information).
    static func backendFailure(_ data: Data) -> TinyTouchError {
        guard data.count <= 65536, let text = String(data: data, encoding: .utf8) else { return .commandFailed }
        let prefix = "Error: tinyTouch rejected the request: FINGER inventory_unavailable reason="
        let line = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard line.hasPrefix(prefix), let stage = Self(rawValue: String(line.dropFirst(prefix.count))) else { return .commandFailed }
        return .inventoryUnavailable(stage: stage)
    }
}

public enum TinyTouchManagementCommand {
    case inventory, settings, hosts, removeHost(String), enroll(Int, replace: Bool), delete(Int), set(TinyTouchSetting, String)
    public var arguments: [String] {
        get throws {
            switch self {
            case .inventory: return ["fingers"]
            case .settings: return ["config", "--json"]
            case .hosts: return ["computers", "list"]
            case .removeHost(let identifier):
                guard TinyTouchHostInventory.validIdentifier(identifier) else { throw TinyTouchError.invalidStatus }
                return ["computers", "remove", identifier]
            case .enroll(let slot, let replace):
                guard (1...10).contains(slot) else { throw TinyTouchError.invalidStatus }
                return ["enroll", String(slot)] + (replace ? ["--replace"] : [])
            case .delete(let slot):
                guard (1...10).contains(slot) else { throw TinyTouchError.invalidStatus }
                return ["delete", String(slot)]
            case .set(let setting, let value):
                guard setting.accepts(value) else { throw TinyTouchError.invalidStatus }
                return ["config", setting.rawValue, value]
            }
        }
    }
    public var timeout: TimeInterval {
        switch self { case .enroll: return 330; case .inventory, .settings, .hosts: return 15; default: return 45 }
    }
}

/// A single cancellable command. SIGINT lets the CLI close CDC and release its
/// foreground lease; firmware cancels enrollment when CDC DTR is deasserted.
public final class TinyTouchCommandRunner: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false
    private var process: Process?
    public init() {}
    public func cancel() {
        lock.lock(); cancelled = true
        if let process, process.isRunning { kill(process.processIdentifier, SIGINT) }
        lock.unlock()
    }
    public func run(device: TinyTouchUSBDevice, backend: URL, command: TinyTouchManagementCommand,
                    progress: @escaping (String) -> Void) throws -> String {
        try readWithRetry(command: command, progress: progress) {
            guard TinyTouchUSBDevice.connected().contains(device) else { throw TinyTouchError.wrongDevice }
            let output = try execute(backend: backend, arguments: ["--port", device.path] + command.arguments,
                                     timeout: command.timeout, progress: progress)
            guard TinyTouchUSBDevice.connected().contains(device) else { throw TinyTouchError.wrongDevice }
            return output
        }
    }
    private func checkCancellation() throws {
        lock.lock(); let value = cancelled; lock.unlock()
        if value { throw CancellationError() }
    }
    // Only a failed read-only inventory command may retry. A protected command
    // might have succeeded before its response was lost, so it must never repeat.
    func readWithRetry(command: TinyTouchManagementCommand, progress: (String) -> Void,
                       operation: () throws -> String) throws -> String {
        for attempt in 0..<3 {
            try checkCancellation()
            do { return try operation() }
            catch TinyTouchError.inventoryUnavailable(let stage) {
                guard case .inventory = command, stage.retryable, attempt < 2 else {
                    throw TinyTouchError.inventoryUnavailable(stage: stage)
                }
                progress("Sensor inventory unavailable (\(stage.rawValue)); retrying read \(attempt + 2) of 3…")
                for _ in 0..<10 {
                    try checkCancellation()
                    Thread.sleep(forTimeInterval: 0.02)
                }
            }
        }
        throw TinyTouchError.commandFailed
    }
    // Kept internal so tests exercise cancellation/output bounds without opening a device.
    func execute(backend: URL, arguments: [String], timeout: TimeInterval, progress: @escaping (String) -> Void) throws -> String {
        guard FileManager.default.isExecutableFile(atPath: backend.path) else { throw TinyTouchError.backendMissing }
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        defer { try? FileManager.default.removeItem(at: directory) }
        let stdout = directory.appendingPathComponent("stdout")
        let stderr = directory.appendingPathComponent("stderr")
        for path in [stdout, stderr] { FileManager.default.createFile(atPath: path.path, contents: nil, attributes: [.posixPermissions: 0o600]) }
        let out = try FileHandle(forWritingTo: stdout), err = try FileHandle(forWritingTo: stderr)
        defer { try? out.close(); try? err.close() }
        let child = Process()
        child.executableURL = backend; child.arguments = arguments
        child.standardInput = FileHandle.nullDevice; child.standardOutput = out; child.standardError = err
        lock.lock()
        if cancelled { lock.unlock(); throw CancellationError() }
        do { try child.run(); process = child; lock.unlock() }
        catch { lock.unlock(); throw error }
        defer { lock.lock(); process = nil; lock.unlock() }
        let deadline = Date().addingTimeInterval(timeout)
        var previous = Data()
        while true {
            lock.lock(); let wasCancelled = cancelled; lock.unlock()
            let tooLarge = [stdout, stderr].contains { url in
                ((try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.intValue ?? 0) > 65536
            }
            if wasCancelled || Date() >= deadline || tooLarge {
                if child.isRunning {
                    kill(child.processIdentifier, SIGINT)
                    let grace = Date().addingTimeInterval(1)
                    while child.isRunning && Date() < grace { Thread.sleep(forTimeInterval: 0.02) }
                    if child.isRunning { kill(child.processIdentifier, SIGKILL) }
                }
                child.waitUntilExit()
                if wasCancelled { throw CancellationError() }
                throw TinyTouchError.timedOut
            }
            let bytes = try Data(contentsOf: stdout)
            if bytes != previous, let text = String(data: bytes, encoding: .utf8) {
                previous = bytes; progress(text)
            }
            if !child.isRunning { break }
            Thread.sleep(forTimeInterval: 0.1)
        }
        guard child.terminationStatus == 0 else {
            throw TinyTouchInventoryStage.backendFailure(try Data(contentsOf: stderr))
        }
        guard let output = String(data: try Data(contentsOf: stdout), encoding: .utf8) else { throw TinyTouchError.invalidStatus }
        return output
    }
}

public struct TinyTouchDeviceSettings {
    public let values: [String: String]
    public init(data: Data) throws {
        guard data.count <= 65536,
              let fields = try? JSONDecoder().decode([String: String].self, from: data),
              let mode = fields["mode"], ["hid", "piv"].contains(mode) else { throw TinyTouchError.invalidStatus }
        for setting in TinyTouchSetting.allCases {
            if let value = fields[setting.rawValue], !setting.accepts(value) { throw TinyTouchError.invalidStatus }
        }
        values = fields
    }
}

/// Names are Mac preferences, as in upstream; they are scoped to the device serial.
public enum TinyTouchFingerprintNames {
    public static func load(serial: String, defaults: UserDefaults) -> [Int: String] {
        guard TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: serial),
              let raw = defaults.dictionary(forKey: "tinyTouch.fingerNames." + serial) as? [String: String] else { return [:] }
        var result: [Int: String] = [:]
        for (key, value) in raw {
            if let slot = Int(key), (1...10).contains(slot), valid(value) { result[slot] = value }
        }
        return result
    }
    public static func save(name: String, slot: Int, serial: String, defaults: UserDefaults) throws {
        let normalized = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard (1...10).contains(slot), TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: serial),
              normalized.isEmpty || valid(normalized) else { throw TinyTouchError.invalidStatus }
        var names = load(serial: serial, defaults: defaults)
        names[slot] = normalized.isEmpty ? nil : normalized
        defaults.set(Dictionary(uniqueKeysWithValues: names.map { (String($0.key), $0.value) }), forKey: "tinyTouch.fingerNames." + serial)
    }
    private static func valid(_ name: String) -> Bool {
        !name.isEmpty && name.utf8.count <= 64 && name.rangeOfCharacter(from: .controlCharacters) == nil
    }
}
