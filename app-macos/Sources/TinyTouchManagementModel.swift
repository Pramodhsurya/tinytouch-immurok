import Foundation
import Combine
import TinyTouchKit

@MainActor
final class TinyTouchManagementModel: ObservableObject {
    @Published private(set) var inventory: TinyTouchFingerprintInventory?
    @Published private(set) var hosts: TinyTouchHostInventory?
    @Published private(set) var settings: [String: String] = [:]
    @Published private(set) var busy = false
    @Published private(set) var progress = ""
    @Published private(set) var error: String?
    @Published private(set) var complete = false
    @Published private(set) var names: [Int: String] = [:]
    private var runner: TinyTouchCommandRunner?
    private var generation = UUID()
    private let connection: TinyTouchConnection
    init(connection: TinyTouchConnection) { self.connection = connection }
    deinit { runner?.cancel() }

    func cancel() { runner?.cancel() }
    func rename(_ slot: Int, to name: String) {
        guard !busy, inventory?.groups[slot] != nil, let serial = connection.usbDevice?.serial else { return }
        do {
            try TinyTouchFingerprintNames.save(name: name, slot: slot, serial: serial, defaults: .standard)
            names = TinyTouchFingerprintNames.load(serial: serial, defaults: .standard)
        } catch { self.error = error.localizedDescription }
    }
    func refresh() { perform(.inventory) }
    func perform(_ command: TinyTouchManagementCommand) {
        guard !busy else { return }
        guard let backend = Bundle.main.resourceURL?.appendingPathComponent("tinyTouchCLI/tinytouch"),
              let device = connection.beginManagement() else {
            error = "Connect tinyTouch by USB and wait for the status check to finish."
            return
        }
        busy = true; error = nil; complete = false; progress = "Connecting to the sensor…"
        let runner = TinyTouchCommandRunner()
        self.runner = runner
        let id = UUID(); generation = id
        let connection = self.connection
        Task { @MainActor [weak self] in
            let result = await Task.detached { () -> Result<(TinyTouchFingerprintInventory, [String: String], TinyTouchStatus, TinyTouchHostInventory), Error> in
                do {
                    let protectedSlot: Int?
                    switch command {
                    case .delete(let slot), .enroll(let slot, replace: true): protectedSlot = slot
                    default: protectedSlot = nil
                    }
                    if let slot = protectedSlot {
                        let current = try TinyTouchFingerprintInventory(output: runner.run(device: device, backend: backend, command: .inventory) { _ in })
                        guard current.canRemoveOrReplace(slot: slot) else {
                            throw TinyTouchError.lastFingerprint
                        }
                    }
                    if case .removeHost(let identifier) = command {
                        let current = try TinyTouchHostInventory(output: runner.run(device: device, backend: backend, command: .hosts) { _ in })
                        guard current.canRemove(identifier) else { throw TinyTouchError.lastHost }
                    }
                    let output = try runner.run(device: device, backend: backend, command: command) { text in
                        Task { @MainActor [weak self] in
                            guard self?.generation == id else { return }
                            self?.progress = text
                        }
                    }
                    let inventoryText: String
                    if case .inventory = command { inventoryText = output }
                    else { inventoryText = try runner.run(device: device, backend: backend, command: .inventory) { _ in } }
                    let inventory = try TinyTouchFingerprintInventory(output: inventoryText)
                    let settingsText = try runner.run(device: device, backend: backend, command: .settings) { _ in }
                    let settings = try TinyTouchDeviceSettings(data: Data(settingsText.utf8)).values
                    switch command {
                    case .enroll(let slot, _): guard inventory.groups[slot] == 4 else { throw TinyTouchError.invalidStatus }
                    case .delete(let slot): guard inventory.groups[slot] == nil else { throw TinyTouchError.invalidStatus }
                    case .set(let setting, let value): guard settings[setting.rawValue] == value else { throw TinyTouchError.invalidStatus }
                    default: break
                    }
                    let hostText: String
                    if case .hosts = command { hostText = output }
                    else { hostText = try runner.run(device: device, backend: backend, command: .hosts) { _ in } }
                    let hosts = try TinyTouchHostInventory(output: hostText)
                    if case .removeHost(let identifier) = command {
                        guard !hosts.identifiers.contains(identifier) else { throw TinyTouchError.invalidStatus }
                    }
                    let status = try TinyTouchUSB.status(device: device, backend: backend)
                    guard hosts.identifiers.count == status.hosts else { throw TinyTouchError.invalidStatus }
                    return .success((inventory, settings, status, hosts))
                } catch { return .failure(error) }
            }.value
            connection.endManagement(status: (try? result.get())?.2)
            guard let self else { return }
            self.generation = UUID() // Discard progress callbacks queued after completion.
            self.runner = nil; self.busy = false
            switch result {
            case .success(let (inventory, settings, _, hosts)):
                self.inventory = inventory; self.settings = settings; self.hosts = hosts; self.complete = true
                if case .delete(let slot) = command {
                    try? TinyTouchFingerprintNames.save(name: "", slot: slot, serial: device.serial, defaults: .standard)
                }
                self.names = TinyTouchFingerprintNames.load(serial: device.serial, defaults: .standard)
                self.progress = "Device state verified."
                NSLog("tinyTouch management: %ld occupied slots, %ld free slots, %ld settings, %ld hosts verified", inventory.groups.count, inventory.available, settings.count, hosts.identifiers.count)
            case .failure(let failure):
                self.inventory = nil; self.settings = [:]; self.hosts = nil
                self.error = failure is CancellationError
                    ? "Cancelled. Reconnect and refresh if enrollment cleanup is pending. Other fingerprint slots were preserved."
                    : failure.localizedDescription
            }
        }
    }
}
