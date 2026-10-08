import Foundation

/// Host IDs are public identifiers, not pairing keys. The CLI retains all
/// credential access under its existing signature and Keychain identity.
public struct TinyTouchHostInventory: Equatable {
    public let identifiers: [String]
    public let capacity: Int
    public static func validIdentifier(_ value: String) -> Bool {
        value.range(of: "^[0-9a-f]{16}$", options: .regularExpression) != nil
    }
    public func canRemove(_ identifier: String) -> Bool {
        identifiers.count > 1 && identifiers.contains(identifier)
    }
    public init(output: String) throws {
        guard output.utf8.count <= 65536 else { throw TinyTouchError.invalidStatus }
        var declared: Int?
        var capacity: Int?
        var identifiers: [String] = []
        for line in output.split(separator: "\n").map(String.init) {
            if line.hasPrefix("HID computers (") {
                guard declared == nil, line.hasSuffix("):") else { throw TinyTouchError.invalidStatus }
                let parts = line.dropFirst(15).dropLast(2).components(separatedBy: " of ")
                guard parts.count == 2, let count = Int(parts[0]), let maximum = Int(parts[1]),
                      (1...8).contains(maximum), (0...maximum).contains(count) else { throw TinyTouchError.invalidStatus }
                declared = count; capacity = maximum
            } else if declared != nil {
                // CLI emits only indented identifiers after the header.
                guard line.hasPrefix("  "), Self.validIdentifier(String(line.dropFirst(2))) else { throw TinyTouchError.invalidStatus }
                let identifier = String(line.dropFirst(2))
                guard !identifiers.contains(identifier) else { throw TinyTouchError.invalidStatus }
                identifiers.append(identifier)
            }
        }
        guard let declared, let capacity, declared == identifiers.count else { throw TinyTouchError.invalidStatus }
        self.identifiers = identifiers.sorted(); self.capacity = capacity
    }
}
