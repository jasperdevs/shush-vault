import Foundation
import LocalAuthentication
import Security

enum PlatformUnlockStore {
    private static let service = "dev.jasper.shushvault"
    private static let account = "vault-passphrase"

    static func state() -> (available: Bool, saved: Bool, label: String, message: String) {
        let context = LAContext()
        var error: NSError?
        let available = context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error)
        let label = label(for: context)
        let saved = readPassphrase() != nil
        let message = available
            ? "\(label) is available."
            : "Device authentication is not available on this Mac."
        return (available, saved, label, message)
    }

    static func savePassphrase(_ passphrase: String, reason: String) async -> Bool {
        guard await verify(reason: reason) else {
            return false
        }

        deletePassphrase()
        let data = Data(passphrase.utf8)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        ]
        return SecItemAdd(query as CFDictionary, nil) == errSecSuccess
    }

    static func readPassphraseWithDeviceAuth(reason: String) async -> String? {
        guard await verify(reason: reason) else {
            return nil
        }

        return readPassphrase()
    }

    static func deletePassphrase() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        SecItemDelete(query as CFDictionary)
    }

    private static func verify(reason: String) async -> Bool {
        let context = LAContext()
        do {
            return try await context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason)
        } catch {
            return false
        }
    }

    private static func readPassphrase() -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]

        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data else {
            return nil
        }

        return String(data: data, encoding: .utf8)
    }

    private static func label(for context: LAContext) -> String {
        _ = context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: nil)
        switch context.biometryType {
        case .faceID:
            return "Face ID"
        case .touchID:
            return "Touch ID"
        default:
            return "Device authentication"
        }
    }
}
