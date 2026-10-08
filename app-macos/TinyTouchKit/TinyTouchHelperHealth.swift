import Foundation

public enum TinyTouchHelperHealth: String, Equatable {
    case running = "Running"
    case loaded = "Loaded; not running"
    case unavailable = "Not loaded or unavailable"

    public init(data: Data) throws {
        guard data.count <= 65536 else { throw TinyTouchError.invalidStatus }
        let fields: [String: Any]
        if let plist = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any] {
            fields = plist
        } else {
            // launchctl list uses semicolons inside arrays, which is not a
            // valid OpenStep plist. Read only exact top-level metadata lines.
            guard let text = String(data: data, encoding: .utf8),
                  text.trimmingCharacters(in: .whitespacesAndNewlines).hasPrefix("{"),
                  (text.trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("}") || text.trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("};")) else { throw TinyTouchError.invalidStatus }
            var metadata: [String: Any] = [:]
            let pattern = #"^[\t ]"(Label|PID)" = (?:"([^"]*)"|([0-9]+));$"#
            let regex = try NSRegularExpression(pattern: pattern, options: .anchorsMatchLines)
            for match in regex.matches(in: text, range: NSRange(text.startIndex..., in: text)) {
                guard let keyRange = Range(match.range(at: 1), in: text) else { throw TinyTouchError.invalidStatus }
                let key = String(text[keyRange])
                guard metadata[key] == nil else { throw TinyTouchError.invalidStatus }
                let valueRange = Range(match.range(at: 2), in: text) ?? Range(match.range(at: 3), in: text)
                guard let valueRange else { throw TinyTouchError.invalidStatus }
                metadata[key] = String(text[valueRange])
            }
            fields = metadata
        }
        guard let label = fields["Label"] as? String,
              ["com.tinytouch.helper", "com.tinytouch.bluetooth"].contains(label) else { throw TinyTouchError.invalidStatus }
        if let raw = fields["PID"] {
            let pid = (raw as? NSNumber)?.intValue ?? (raw as? String).flatMap(Int.init)
            guard let pid, (0...Int(Int32.max)).contains(pid) else { throw TinyTouchError.invalidStatus }
            self = pid > 0 ? .running : .loaded
        } else { self = .loaded }
    }
    public static func read(label: String) -> Self {
        guard ["com.tinytouch.helper", "com.tinytouch.bluetooth"].contains(label) else { return .unavailable }
        do {
            let output = try TinyTouchCommandRunner().execute(backend: URL(fileURLWithPath: "/bin/launchctl"),
                arguments: ["list", label], timeout: 3, progress: { _ in })
            return try Self(data: Data(output.utf8))
        } catch { return .unavailable }
    }
}
