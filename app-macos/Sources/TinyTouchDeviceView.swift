import SwiftUI

struct TinyTouchDeviceView: View {
    @ObservedObject var connection: TinyTouchConnection
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
                Button(connection.refreshing ? "Checking…" : "Refresh USB status") { connection.refreshUSB() }
                    .disabled(connection.refreshing)
                Divider()
                Text("Fingerprint unlock uses the existing tinyTouch helper and the device keyboard. Connection identity does not authorize privileged operations.")
                Text("Feature port in progress: fingerprint management, pairing, keys, PAM/sudo, SSH, Quick Fill, automation and signed OTA require ESP32 protocol adapters. The other tabs describe the original app; their device commands are not enabled by this connection.")
                    .foregroundStyle(.secondary)
            }.frame(maxWidth: .infinity, alignment: .leading).padding(24)
        }
    }
}
