import Foundation
import Combine
import TinyTouchKit

@MainActor
final class TinyTouchAuthTestModel: ObservableObject {
    @Published private(set) var busy = false
    @Published private(set) var message = "Test a fresh fingerprint request without performing a privileged action."
    @Published private(set) var passed = false
    private var cancellation: TinyTouchAuthCancellation?
    private var activeRequest: UUID?
    private let connection: TinyTouchConnection
    init(connection: TinyTouchConnection) { self.connection = connection }
    deinit { cancellation?.cancel() }
    func cancel() { cancellation?.cancel() }
    func test() {
        guard !busy else { return }
        // Every click clears the old green result, including a rejected start.
        passed = false
        guard connection.usbStatus?.authenticationSupported == true else {
            message = "Fresh fingerprint authentication is unavailable. Update the firmware and refresh USB."
            return
        }
        guard let device = connection.beginManagement() else {
            message = "A new test could not start. Refresh USB and wait for other operations to finish."
            return
        }
        busy = true
        message = "Reserving USB for this authentication request…"
        let cancellation = TinyTouchAuthCancellation(); self.cancellation = cancellation
        let requestID = UUID(); activeRequest = requestID
        let connection = self.connection
        Task { @MainActor [weak self] in
            let result = await Task.detached { () -> Result<Void, Error> in
                do {
                    try TinyTouchAuthUSB.test(device: device, cancellation: cancellation) { text in
                        Task { @MainActor [weak self] in
                            guard let self, self.activeRequest == requestID else { return }
                            self.message = text
                        }
                    }
                    return .success(())
                } catch { return .failure(error) }
            }.value
            connection.endManagement(status: nil)
            guard let self else { return }
            self.activeRequest = nil
            self.busy = false; self.cancellation = nil
            switch result {
            case .success:
                self.passed = true
                self.message = "Fresh fingerprint proof verified for this test. This result cannot be reused for another request."
                NSLog("tinyTouch authentication proof test passed")
            case .failure(let error):
                self.message = error.localizedDescription
                NSLog("tinyTouch authentication proof test failed")
            }
        }
    }
}
