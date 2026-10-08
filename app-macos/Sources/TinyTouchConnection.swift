import Foundation
import CoreBluetooth
import Combine
import TinyTouchKit

/// Connection identity is deliberately separate from authority to unlock/PAM/sign.
/// The established helper remains the sole consumer of password delivery events.
final class TinyTouchConnection: NSObject, ObservableObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    @Published private(set) var usbStatus: TinyTouchStatus?
    @Published private(set) var usbDevice: TinyTouchUSBDevice?
    @Published private(set) var usbMessage = "Checking USB…"
    @Published private(set) var bluetoothMessage = "Checking Bluetooth…"
    @Published private(set) var bluetoothConnected = false
    @Published private(set) var refreshing = false
    private struct Remembered: Decodable { let schema: Int; let device_id: String; let peripheral: UUID }
    private let nus = CBUUID(string: "6e400001-b5a3-f393-e0a9-e50e24dcca9e")
    private let identity = CBUUID(string: "6e400004-b5a3-f393-e0a9-e50e24dcca9e")
    private var central: CBCentralManager!
    private var peer: CBPeripheral?
    private var remembered: Remembered?
    private var timer: Timer?
    private var connectionDeadline: Date?
    private var retryAfter = Date.distantPast
    private var lastUSBInventory: [TinyTouchUSBDevice] = []

    override init() {
        super.init()
        let config = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/tinyTouch/bluetooth.json")
        if let data = try? Data(contentsOf: config), data.count < 4096,
           let value = try? JSONDecoder().decode(Remembered.self, from: data), value.schema == 1,
           TinyTouchUSBDevice.accepts(vendor: 0x303a, product: 0x4001, serial: value.device_id) {
            remembered = value
        }
        central = CBCentralManager(delegate: self, queue: .main)
        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            self?.pollBluetooth()
        }
        refreshUSB()
    }
    deinit { timer?.invalidate() }

    func refreshUSB() {
        guard !refreshing else { return }
        refreshing = true
        usbStatus = nil; usbDevice = nil
        let expected = remembered?.device_id
        let backend = Bundle.main.resourceURL?.appendingPathComponent("tinyTouchCLI/tinytouch")
        Task { @MainActor [weak self] in
            let result = await Task.detached { () -> Result<(TinyTouchUSBDevice, TinyTouchStatus)?, Error> in
                do {
                    guard let device = try TinyTouchUSBDevice.select(TinyTouchUSBDevice.connected(), expected: expected) else { return .success(nil) }
                    guard let backend else { throw TinyTouchError.backendMissing }
                    return .success((device, try TinyTouchUSB.status(device: device, backend: backend)))
                } catch { return .failure(error) }
            }.value
            guard let self else { return }
            self.refreshing = false
            switch result {
            case .success(let value):
                self.usbDevice = value?.0; self.usbStatus = value?.1
                self.usbMessage = value == nil ? "USB disconnected" : "USB connected — protocol 6"
                NSLog("tinyTouch adapter: %@", self.usbMessage)
            case .failure(let error): self.usbMessage = error.localizedDescription
            }
        }
    }
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        NSLog("tinyTouch adapter: Bluetooth state %ld, authorization %ld", central.state.rawValue, CBCentralManager.authorization.rawValue)
        bluetoothConnected = false
        if central.state != .poweredOn {
            peer = nil; connectionDeadline = nil
            bluetoothMessage = central.state == .poweredOff ? "Bluetooth is off" : "Bluetooth unavailable or permission required"
        } else { pollBluetooth() }
    }
    private func pollBluetooth() {
        // Clear USB metadata immediately on unplug; active reads are manual to avoid
        // repeatedly pausing the password helper while the user authenticates.
        let inventory = TinyTouchUSBDevice.connected()
        if let device = usbDevice, !inventory.contains(device) {
            usbDevice = nil; usbStatus = nil; usbMessage = "USB disconnected"
        }
        if inventory != lastUSBInventory {
            lastUSBInventory = inventory
            refreshUSB()
        }
        guard central.state == .poweredOn else { return }
        guard let remembered else { bluetoothMessage = "Use the tinyTouch helper to pair Bluetooth first"; return }
        if let deadline = connectionDeadline, Date() >= deadline {
            reject("Bluetooth connection or identity read timed out")
            return
        }
        guard peer == nil, Date() >= retryAfter else { return }
        // Retrieve the bonded UUID; never select a peer by its advertised name.
        guard let candidate = central.retrievePeripherals(withIdentifiers: [remembered.peripheral]).first else {
            bluetoothMessage = "Paired tinyTouch unavailable"; return
        }
        peer = candidate; candidate.delegate = self
        connectionDeadline = Date().addingTimeInterval(12)
        bluetoothMessage = "Connecting to paired tinyTouch…"
        central.connect(candidate, options: nil)
    }
    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        guard peripheral === peer else { central.cancelPeripheralConnection(peripheral); return }
        peripheral.discoverServices([nus])
    }
    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        guard peripheral === peer else { return }
        reject("Bluetooth connection failed")
    }
    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        guard peripheral === peer else { return }
        peer = nil; connectionDeadline = nil; bluetoothConnected = false
        bluetoothMessage = "Bluetooth disconnected — retrying"
        retryAfter = Date().addingTimeInterval(5)
    }
    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard peripheral === peer else { return }
        guard error == nil, let service = peripheral.services?.first(where: { $0.uuid == nus }) else {
            reject("tinyTouch Bluetooth service unavailable"); return
        }
        peripheral.discoverCharacteristics([identity], for: service)
    }
    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        guard peripheral === peer else { return }
        guard error == nil, let characteristic = service.characteristics?.first(where: { $0.uuid == identity }),
              characteristic.properties.contains(.read) else { reject("Bluetooth identity unavailable"); return }
        peripheral.readValue(for: characteristic)
    }
    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard peripheral === peer, characteristic.uuid == identity else { return }
        guard error == nil, let data = characteristic.value, let expected = remembered?.device_id,
              TinyTouchIdentity.matches(data, expected: expected) else {
            reject("Bluetooth device identity rejected"); return
        }
        connectionDeadline = nil; bluetoothConnected = true
        bluetoothMessage = "Bluetooth connected — bonded identity confirmed"
        NSLog("tinyTouch adapter: bonded Bluetooth identity confirmed")
    }
    private func reject(_ message: String) {
        let rejected = peer; peer = nil; connectionDeadline = nil
        bluetoothConnected = false; bluetoothMessage = message
        NSLog("tinyTouch adapter: %@", message)
        retryAfter = Date().addingTimeInterval(15)
        if let rejected { central.cancelPeripheralConnection(rejected) }
    }
}
