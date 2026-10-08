import SwiftUI
import TinyTouchKit

struct TinyTouchDeviceView: View {
    @ObservedObject var connection: TinyTouchConnection
    @StateObject private var management: TinyTouchManagementModel
    @State private var selectedFinger = 1
    @State private var draftName = ""
    @State private var pendingCommand: TinyTouchManagementCommand?
    @State private var confirmation = false
    @State private var confirmationText = ""
    init(connection: TinyTouchConnection) {
        self.connection = connection
        _management = StateObject(wrappedValue: TinyTouchManagementModel(connection: connection))
    }
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("tinyTouch · ESP32-S3 + ZW111").font(.title2)
                Label(connection.usbMessage, systemImage: "cable.connector")
                Label(connection.bluetoothMessage, systemImage: "antenna.radiowaves.left.and.right")
                if let device = connection.usbDevice, let status = connection.usbStatus {
                    Text("Device: \(device.serial)")
                    Text("Firmware: \(status.firmware) · \(status.build)")
                    Text("Sensor: \(status.sensorReady ? "ready" : "offline") · Templates: \(status.fingerprints < 0 ? "unknown" : String(status.fingerprints)) · Hosts: \(status.hosts)")
                    Text("Mode: \(status.mode.uppercased()) · Battery measurement unavailable")
                }
                Button("Refresh device and fingerprints") { management.refresh() }
                    .disabled(management.busy || connection.refreshing || connection.usbDevice == nil)
                if let inventory = management.inventory {
                    GroupBox("Fingerprints") {
                        VStack(alignment: .leading, spacing: 12) {
                            Picker("Finger slot", selection: $selectedFinger) {
                                ForEach(1...10, id: \.self) { slot in
                                    Text("\(management.names[slot] ?? "Finger \(slot)"): \(description(inventory.groups[slot]))").tag(slot)
                                }
                            }
                            Text("Space for \(inventory.available) additional fingers. Each complete enrollment captures four views.")
                                .font(.caption).foregroundStyle(.secondary)
                            HStack {
                                TextField("Finger \(selectedFinger)", text: $draftName)
                                Button("Rename on this Mac") { management.rename(selectedFinger, to: draftName) }
                                    .disabled(management.busy || inventory.groups[selectedFinger] == nil)
                            }
                            HStack {
                                Button(inventory.groups[selectedFinger] == nil ? "Enroll finger" : "Replace finger") {
                                    if inventory.groups[selectedFinger] == nil {
                                        management.perform(.enroll(selectedFinger, replace: false))
                                    } else {
                                        pendingCommand = .enroll(selectedFinger, replace: true)
                                        confirmationText = "Replace slot \(selectedFinger)? Its existing fingerprint views will be removed. Other slots will be preserved."
                                        confirmation = true
                                    }
                                }
                                .disabled(management.busy || connection.refreshing || connection.usbDevice == nil || connection.usbStatus?.sensorReady != true || (inventory.groups[selectedFinger] != nil && !inventory.canRemoveOrReplace(slot: selectedFinger)))
                                Button("Delete finger", role: .destructive) {
                                    pendingCommand = .delete(selectedFinger)
                                    confirmationText = "Delete every fingerprint view in slot \(selectedFinger)? Other slots will be preserved."
                                    confirmation = true
                                }
                                .disabled(management.busy || connection.refreshing || !inventory.canRemoveOrReplace(slot: selectedFinger) || connection.usbDevice == nil)
                            }
                            if inventory.groups.count <= 1 {
                                Text("Keep the last enrolled finger so you can continue authorizing device changes.").font(.caption)
                            }
                        }.padding(8)
                    }
                }
                if !management.settings.isEmpty {
                    DisclosureGroup("Device settings") {
                        VStack(alignment: .leading, spacing: 12) {
                            Text("Saving requires your enrolled finger. The app checks the saved value after the device acknowledges it.").font(.caption)
                            ForEach(TinyTouchSetting.allCases, id: \.rawValue) { setting in
                                if let value = management.settings[setting.rawValue] {
                                    TinyTouchSettingEditor(setting: setting, value: value, disabled: management.busy || connection.refreshing || connection.usbDevice == nil) { draft in
                                        management.perform(.set(setting, draft))
                                    }
                                }
                            }
                        }.padding(.top, 12)
                    }
                }
                if management.busy {
                    ProgressView()
                    Text("Follow the sensor prompts. Touch your enrolled finger first to authorize changes.").font(.caption)
                    Text(management.progress).font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                    Button("Cancel operation") { management.cancel() }
                }
                if let error = management.error { Text(error).foregroundStyle(.red) }
                if management.complete { Text("Device state verified.").foregroundStyle(.secondary) }
                Divider()
                Text("Fingerprint unlock continues through the tinyTouch helper and device keyboard. Management currently requires USB; Bluetooth management is being ported.")
                Text("Keys, PAM/sudo, SSH, Quick Fill, automation and signed OTA remain under compatibility development.")
                    .foregroundStyle(.secondary)
            }.frame(maxWidth: .infinity, alignment: .leading).padding(24)
        }
        .onAppear { if connection.usbDevice != nil { management.refresh() } }
        .onDisappear { management.cancel() }
        .onChange(of: connection.usbDevice?.serial) { serial in
            if serial != nil { management.refresh() }
            else { management.cancel() }
        }
        .onChange(of: selectedFinger) { draftName = management.names[$0] ?? "" }
        .onChange(of: management.names) { draftName = $0[selectedFinger] ?? "" }
        .alert("Confirm fingerprint change", isPresented: $confirmation) {
            Button("Continue", role: .destructive) {
                if let command = pendingCommand { management.perform(command) }
                pendingCommand = nil
            }
            Button("Cancel", role: .cancel) { pendingCommand = nil }
        } message: { Text(confirmationText) }
    }
    private func description(_ views: Int?) -> String {
        guard let views else { return "empty" }
        return views == -1 ? "cleanup pending" : "\(views) of 4 views"
    }
}

private struct TinyTouchSettingEditor: View {
    let setting: TinyTouchSetting
    let value: String
    let disabled: Bool
    let apply: (String) -> Void
    @State private var draft: String
    init(setting: TinyTouchSetting, value: String, disabled: Bool, apply: @escaping (String) -> Void) {
        self.setting = setting; self.value = value; self.disabled = disabled; self.apply = apply
        _draft = State(initialValue: value)
    }
    var body: some View {
        HStack {
            Text(setting.title).frame(maxWidth: .infinity, alignment: .leading)
            if setting.choices.isEmpty {
                TextField(setting.range.map { "\($0.lowerBound)…\($0.upperBound)" } ?? "", text: $draft).frame(width: 90)
            } else {
                Picker(setting.title, selection: $draft) {
                    ForEach(setting.choices, id: \.self) { Text($0).tag($0) }
                }.labelsHidden().frame(width: 130)
            }
            Button("Save") { apply(draft) }.disabled(disabled || draft == value || !setting.accepts(draft))
        }.onChange(of: value) { draft = $0 }
    }
}
