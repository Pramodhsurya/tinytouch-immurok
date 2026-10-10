import SwiftUI
import ServiceManagement
import ApplicationServices
import CoreBluetooth
import TinyTouchKit

@MainActor
final class TinyTouchMacSettingsModel: ObservableObject {
    @Published private(set) var usbHelper: TinyTouchHelperHealth = .unavailable
    @Published private(set) var bluetoothHelper: TinyTouchHelperHealth = .unavailable
    @Published private(set) var refreshing = false
    @Published private(set) var loginStatus = SMAppService.mainApp.status
    @Published private(set) var accessibility = AXIsProcessTrusted()
    @Published private(set) var bluetoothAuthorization = CBCentralManager.authorization
    @Published private(set) var error: String?

    var installed: Bool {
        let path = Bundle.main.bundleURL.standardizedFileURL.path
        let personal = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications").path + "/"
        return path.hasPrefix(personal) || path.hasPrefix("/Applications/")
    }
    var loginDescription: String {
        switch loginStatus {
        case .enabled: return "Enabled"
        case .notRegistered: return "Off"
        case .requiresApproval: return "Approval required in System Settings"
        case .notFound: return "App registration unavailable"
        @unknown default: return "Unknown"
        }
    }
    var bluetoothDescription: String {
        switch bluetoothAuthorization {
        case .allowedAlways: return "Allowed"
        case .denied: return "Denied"
        case .restricted: return "Restricted by this Mac"
        case .notDetermined: return "Not requested yet"
        @unknown default: return "Unknown"
        }
    }
    func setLogin(_ enabled: Bool) {
        guard installed, !refreshing else { return }
        error = nil
        do {
            if enabled { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
        } catch { self.error = "Could not change launch at login: \(error.localizedDescription)" }
        loginStatus = SMAppService.mainApp.status
    }
    func refresh() {
        guard !refreshing else { return }
        refreshing = true
        loginStatus = SMAppService.mainApp.status
        accessibility = AXIsProcessTrusted()
        bluetoothAuthorization = CBCentralManager.authorization
        Task { @MainActor [weak self] in
            let health = await Task.detached {
                (TinyTouchHelperHealth.read(label: "com.tinytouch.helper"),
                 TinyTouchHelperHealth.read(label: "com.tinytouch.bluetooth"))
            }.value
            guard let self else { return }
            self.usbHelper = health.0; self.bluetoothHelper = health.1
            self.refreshing = false
            NSLog("tinyTouch Mac settings: USB helper %@, Bluetooth helper %@, login %@, installed %d",
                  self.usbHelper.rawValue, self.bluetoothHelper.rawValue, self.loginDescription, self.installed ? 1 : 0)
        }
    }
}

struct TinyTouchMacSettingsView: View {
    @StateObject private var settings = TinyTouchMacSettingsModel()
    @ObservedObject var connection: TinyTouchConnection
    @StateObject private var authentication: TinyTouchAuthTestModel
    init(connection: TinyTouchConnection) {
        self.connection = connection
        _authentication = StateObject(wrappedValue: TinyTouchAuthTestModel(connection: connection))
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            GroupBox("Fingerprint authentication test") {
                VStack(alignment: .leading, spacing: 10) {
                    Text(authentication.message).foregroundStyle(authentication.passed ? Color.green : Color.primary)
                    if connection.usbStatus?.authenticationSupported != true {
                        Text("Connect by USB with firmware supporting fresh finger presence (auth_fresh=1) to enable this test.").font(.caption)
                    }
                    Text("Mac Keychain may ask you to allow this app to read the existing pairing key. Passwords are not read. Sudo uses a separate approval request; system authorization, SSH and automation are still being ported.").font(.caption).foregroundStyle(.secondary)
                    if authentication.busy {
                        ProgressView()
                        Button("Cancel test") { authentication.cancel() }
                    } else {
                        Button("Test enrolled fingerprint") { authentication.test() }
                            .disabled(connection.usbStatus?.authenticationSupported != true || connection.refreshing || connection.managing)
                    }
                }.padding(8).frame(maxWidth: .infinity, alignment: .leading)
            }
            GroupBox("Mac startup") {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle("Launch tinyTouch Native at login", isOn: Binding(
                        get: { settings.loginStatus == .enabled || settings.loginStatus == .requiresApproval },
                        set: { settings.setLogin($0) }))
                        .disabled(!settings.installed || settings.refreshing)
                    Text("Status: \(settings.loginDescription)").font(.caption)
                    if !settings.installed { Text("Open the app from Applications before enabling launch at login.").font(.caption) }
                    if settings.loginStatus == .requiresApproval {
                        Button("Open Login Items settings") { SMAppService.openSystemSettingsLoginItems() }
                    }
                    Button("Run startup wizard") {
                        NotificationCenter.default.post(name: .openSetupWizard, object: nil)
                    }
                }.padding(8).frame(maxWidth: .infinity, alignment: .leading)
            }
            GroupBox("Permissions and helper health") {
                VStack(alignment: .leading, spacing: 10) {
                    Text("Native Bluetooth permission: \(settings.bluetoothDescription)")
                    Text("Native Accessibility permission: \(settings.accessibility ? "Allowed" : "Not granted; not required for current management")")
                    Text("USB password helper: \(settings.usbHelper.rawValue)")
                    Text("Bluetooth password helper: \(settings.bluetoothHelper.rawValue)")
                    Text(connection.bluetoothMessage).font(.caption)
                    Text("Helper health checks whether the service is running; it does not test saved credentials or fingerprint output. This app keeps the existing helpers as the password-output owners.").font(.caption).foregroundStyle(.secondary)
                    HStack {
                        Button("Refresh status") { settings.refresh() }.disabled(settings.refreshing)
                        Button("Bluetooth permissions") {
                            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Bluetooth") { NSWorkspace.shared.open(url) }
                        }
                    }
                    if let error = settings.error { Text(error).foregroundStyle(.red) }
                }.padding(8).frame(maxWidth: .infinity, alignment: .leading)
            }
        }.onAppear { settings.refresh() }
        .onDisappear { authentication.cancel() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in settings.refresh() }
    }
}
