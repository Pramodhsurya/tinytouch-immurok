import SwiftUI
import TinyTouchKit

enum TinyTouchSetupCompletion {
    static let key = "tinyTouch.esp32.setup.v1.serial"
    static func isComplete() -> Bool {
        guard let serial = UserDefaults.standard.string(forKey: key) else { return false }
        let devices = TinyTouchUSBDevice.connected()
        return devices.isEmpty || (devices.count == 1 && devices[0].serial == serial)
    }
}

struct TinyTouchSetupWizard: View {
    @ObservedObject var connection: TinyTouchConnection
    @StateObject private var management: TinyTouchManagementModel
    @Environment(\.dismiss) private var dismiss
    @State private var step = 0
    @State private var testedOutput = false
    @State private var enrollmentSlot = 1
    @State private var checkedSerial: String?
    private let titles = ["Connect your device", "Set up fingerprints", "Test fingerprint output", "Ready for this Mac"]
    init(connection: TinyTouchConnection) {
        self.connection = connection
        _management = StateObject(wrappedValue: TinyTouchManagementModel(connection: connection))
    }
    private var ready: Bool {
        TinyTouchSetupReadiness.canFinish(status: connection.usbStatus, inventory: management.inventory,
                                         currentSerial: connection.usbDevice?.serial, checkedSerial: checkedSerial,
                                         outputConfirmed: true)
    }
    private var canContinue: Bool {
        guard !management.busy && !connection.refreshing else { return false }
        switch step {
        case 0: return connection.usbStatus?.sensorReady == true
        case 1: return ready
        default: return ready && testedOutput
        }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack {
                Image(systemName: "touchid").font(.largeTitle).foregroundStyle(Color.accentColor)
                VStack(alignment: .leading) {
                    Text("Welcome to tinyTouch").font(.title2)
                    Text("Step \(step + 1) of 4 · ESP32-S3 + ZW111").foregroundStyle(.secondary)
                }
            }
            ProgressView(value: Double(step + 1), total: 4)
            Text(titles[step]).font(.headline)
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    stepContent
                    if management.busy {
                        ProgressView()
                        Text(management.progress).font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                        Button("Cancel operation") { management.cancel() }
                    }
                    if let error = management.error { Text(error).foregroundStyle(.red) }
                }.frame(maxWidth: .infinity, alignment: .leading)
            }.frame(height: 265)
            Divider()
            HStack {
                Button("Later") { dismiss() }.disabled(management.busy)
                Spacer()
                if step > 0 { Button("Back") { step -= 1 }.disabled(management.busy) }
                Button(step == 3 ? "Finish setup" : "Continue") {
                    if step == 3, let serial = connection.usbDevice?.serial, ready && testedOutput {
                        UserDefaults.standard.set(serial, forKey: TinyTouchSetupCompletion.key)
                        NotificationCenter.default.post(name: .openSettingsWindow, object: SettingsTab.device)
                        dismiss()
                    } else if step < 3 { step += 1 }
                }.keyboardShortcut(.defaultAction).disabled(!canContinue)
            }
        }.padding(24).frame(width: 530)
        .onAppear { refreshInventoryIfReady() }
        .onChange(of: connection.refreshing) { refreshing in
            if !refreshing { refreshInventoryIfReady() }
        }
        .onChange(of: connection.usbDevice?.serial) { serial in
            if serial != checkedSerial { testedOutput = false; checkedSerial = nil }
            if serial == nil { management.cancel() }
            else { refreshInventoryIfReady() }
        }
        .onReceive(management.$inventory) { inventory in
            if let inventory {
                checkedSerial = connection.usbDevice?.serial
                enrollmentSlot = (1...10).first { inventory.groups[$0] == nil } ?? 1
            }
        }
        .onDisappear { management.cancel() }
    }
    @ViewBuilder private var stepContent: some View {
        switch step {
        case 0:
            Text("Connect by USB for initial setup and sensor management. Your existing fingerprints and pairing are preserved.")
            Label(connection.usbMessage, systemImage: "cable.connector")
            Label(connection.bluetoothMessage, systemImage: "antenna.radiowaves.left.and.right")
            if let device = connection.usbDevice { Text("Device: \(device.serial)").textSelection(.enabled) }
            Text("If Bluetooth access is denied, enable tinyTouch in System Settings → Privacy & Security → Bluetooth. USB setup can continue without Bluetooth.").font(.caption)
            Button("Check connection again") { connection.refreshUSB() }.disabled(connection.refreshing || management.busy)
        case 1:
            Text("Existing enrolled fingers can authorize changes. A complete new enrollment captures four views.")
            if connection.usbStatus?.hosts == 0 {
                Text("No host is paired. Complete tinyTouch helper pairing before proceeding; native host pairing is still being ported.").foregroundStyle(.orange)
            }
            if connection.usbStatus?.mode != "hid" {
                Text("Keyboard output requires HID mode. Mode changes are not yet available in this wizard.").foregroundStyle(.orange)
            }
            if let inventory = management.inventory {
                ForEach(inventory.groups.keys.sorted(), id: \.self) { slot in
                    Text("Finger \(slot): \(inventory.groups[slot] == -1 ? "cleanup pending" : "\(inventory.groups[slot] ?? 0) of 4 views")")
                }
                let empty = (1...10).filter { inventory.groups[$0] == nil }
                if !empty.isEmpty {
                    Picker("New finger slot", selection: $enrollmentSlot) {
                        ForEach(empty, id: \.self) { Text("Finger \($0)").tag($0) }
                    }
                    Button("Enroll in empty slot") {
                        guard inventory.groups[enrollmentSlot] == nil else { return }
                        testedOutput = false
                        management.perform(.enroll(enrollmentSlot, replace: false))
                    }.disabled(management.busy || connection.refreshing)
                }
                Text("You can continue with an existing finger. Replacement and deletion are available on the Device page after setup.").font(.caption)
            }
            Button("Refresh fingerprints") { management.refresh() }.disabled(management.busy || connection.refreshing || connection.usbDevice == nil)
        case 2:
            Text("Open a separate, empty text field on this Mac, focus it, then touch your enrolled finger. Confirm that the configured output arrives once. Clear that field afterward.")
            Text("Keep the configured tinyTouch helper running. Password delivery remains with the existing helper/device keyboard; this app does not read your saved password.").font(.caption)
            Toggle("I tested fingerprint output successfully on this Mac", isOn: $testedOutput)
            if !ready { Text("Verify the USB sensor, host configuration and fingerprint inventory before finishing.").foregroundStyle(.orange) }
        default:
            Label(ready ? "USB sensor and fingerprint inventory verified" : "Reconnect USB and refresh to verify readiness", systemImage: ready ? "checkmark.circle" : "exclamationmark.circle")
            Label("Fingerprint output confirmed by you", systemImage: "person.crop.circle.badge.checkmark")
            Label(connection.bluetoothMessage, systemImage: "antenna.radiowaves.left.and.right")
            Text("USB supports fingerprint management and device settings. Bluetooth supports the existing unlock path. Bluetooth management, Keys, PAM/sudo, SSH and automation execution are still being ported.").font(.caption)
        }
    }
    private func refreshInventoryIfReady() {
        guard connection.usbDevice != nil, !connection.refreshing, !connection.managing else { return }
        management.refresh()
    }
}
