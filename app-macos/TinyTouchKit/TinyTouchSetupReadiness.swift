import Foundation

/// Transport identity is not proof of authentication. Output confirmation is
/// a separate, explicit human check after read-only USB commissioning checks.
public enum TinyTouchSetupReadiness {
    public static func canFinish(status: TinyTouchStatus?, inventory: TinyTouchFingerprintInventory?,
                                 currentSerial: String?, checkedSerial: String?, outputConfirmed: Bool) -> Bool {
        guard outputConfirmed, let currentSerial, currentSerial == checkedSerial,
              let status, status.sensorReady, status.mode == "hid", status.hosts > 0,
              let inventory, inventory.groups.values.contains(where: { $0 > 0 }) else { return false }
        return true
    }
}
