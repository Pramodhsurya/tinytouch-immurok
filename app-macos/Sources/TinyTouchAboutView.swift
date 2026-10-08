import SwiftUI
import AppKit

struct TinyTouchAboutView: View {
    @ObservedObject var connection: TinyTouchConnection
    @State private var copied = false
    private var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "development"
    }
    private var diagnosticText: String {
        var fields = ["App: tinyTouch Native Preview", "App version: \(version)",
                      "USB: \(connection.usbMessage)", "Bluetooth: \(connection.bluetoothMessage)"]
        if let device = connection.usbDevice, let status = connection.usbStatus {
            fields += ["Device: \(device.serial)", "Firmware: \(status.firmware)", "Build: \(status.build)",
                       "Sensor: \(status.sensorReady ? "ready" : "offline")", "Templates: \(status.fingerprints)", "Hosts: \(status.hosts)"]
        }
        return fields.joined(separator: "\n")
    }
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("tinyTouch").font(.largeTitle)
                Text("Native Mac preview · Version \(version)")
                Text("ESP32-S3 + ZW111")
                GroupBox("Diagnostics") {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(diagnosticText).font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                        Button(copied ? "Copied" : "Copy connection diagnostics") {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString(diagnosticText, forType: .string)
                            copied = true
                        }
                        Text("Includes connection and firmware metadata. Passwords, pairing keys and fingerprint images are excluded.").font(.caption).foregroundStyle(.secondary)
                    }.padding(8).frame(maxWidth: .infinity, alignment: .leading)
                }
                Link("Project and source", destination: URL(string: "https://github.com/Pramodhsurya/tinytouch-immurok")!)
                Link("Mac feature checklist", destination: URL(string: "https://github.com/Pramodhsurya/tinytouch-immurok/blob/main/docs/FEATURE_CHECKLIST.md")!)
                Link("Credits and licenses", destination: URL(string: "https://github.com/Pramodhsurya/tinytouch-immurok/blob/main/CREDITS.md")!)
                Text("App updates and signed ESP32 OTA will be enabled after their compatibility and packaging tests pass.").foregroundStyle(.secondary)
            }.padding(24).frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}
