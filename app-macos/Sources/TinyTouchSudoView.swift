import SwiftUI

struct TinyTouchSudoView: View {
    @ObservedObject var model: TinyTouchSudoModel
    var body: some View {
        GroupBox("Fingerprint approval for sudo") {
            VStack(alignment: .leading, spacing: 10) {
                Text(model.message).textSelection(.enabled)
                if model.busy {
                    ProgressView()
                    Button("Cancel sudo approval") { model.cancel() }
                } else {
                    Toggle("Enable fingerprint approval for sudo", isOn: Binding(get: { model.enabled }, set: { model.setEnabled($0) }))
                        .disabled(!model.installed)
                    HStack {
                        Button("Prepare sudo setup") { model.prepare() }
                        Button("Refresh setup status") { model.refresh() }
                    }
                }
                Text("Connect USB and keep tinyTouch Native open. Each sudo authentication prompt needs a fresh fingerprint. Cancelled or unavailable approvals continue to the Mac password prompt. Administrator installation is required once for this app build.")
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(8).frame(maxWidth: .infinity, alignment: .leading)
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in model.refresh() }
    }
}
