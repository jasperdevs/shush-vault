import CryptoKit
import Foundation
import Security

private let formatVersion = 1
private let saltLength = 16
private let nonceLength = 12
private let keyLength = 32
private let tagLength = 16
private let kdfName = "pbkdf2-sha256"
private let kdfIterations = 310_000
private let cipherName = "aes-256-gcm"

struct SecretRow: Identifiable, Codable, Equatable {
    var id: String
    var workspace: String
    var name: String
    var value: String
    var environment: String
    var provider: String
    var notes: String
    var createdAt: String
    var updatedAt: String
    var deletedAt: String?

    var maskedValue: String {
        if value.count <= 4 {
            return String(repeating: "*", count: max(value.count, 1))
        }

        return String(repeating: "*", count: 8) + value.suffix(4)
    }
}

private struct VaultDocument: Codable {
    var records: [SecretRow]
}

private struct EncryptedVault: Codable {
    var version: Int
    var kdf: String
    var iterations: Int
    var cipher: String
    var salt: String
    var nonce: String
    var ciphertext: String
}

@MainActor
final class VaultStore: ObservableObject {
    @Published private(set) var rows: [SecretRow] = []
    @Published private(set) var isUnlocked = false
    @Published var status = "Locked"

    private var passphrase = ""
    private let fileURL: URL

    init(fileURL: URL = VaultStore.defaultVaultURL()) {
        self.fileURL = fileURL
    }

    func unlock(passphrase: String) {
        guard !passphrase.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            status = "Enter a passphrase."
            return
        }

        do {
            self.passphrase = passphrase
            rows = try Self.loadVault(fileURL: fileURL, passphrase: passphrase)
            isUnlocked = true
            status = "Unlocked encrypted vault."
        } catch {
            self.passphrase = ""
            isUnlocked = false
            status = "Could not unlock vault."
        }
    }

    func add(workspace: String, name: String, value: String, environment: String, provider: String, notes: String) {
        guard isUnlocked else {
            status = "Unlock the vault first."
            return
        }

        let cleanName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !cleanName.isEmpty, !value.isEmpty else {
            status = "Key and value are required."
            return
        }

        let now = Self.isoNow()
        rows.insert(SecretRow(
            id: UUID().uuidString.lowercased(),
            workspace: Self.clean(workspace, fallback: "Default"),
            name: cleanName,
            value: value,
            environment: Self.clean(environment, fallback: "Dev"),
            provider: provider.trimmingCharacters(in: .whitespacesAndNewlines),
            notes: notes.trimmingCharacters(in: .whitespacesAndNewlines),
            createdAt: now,
            updatedAt: now,
            deletedAt: nil
        ), at: 0)

        save()
    }

    func delete(_ row: SecretRow) {
        rows.removeAll { $0.id == row.id }
        save()
    }

    private func save() {
        do {
            try Self.saveVault(rows: rows, fileURL: fileURL, passphrase: passphrase)
            status = "Saved to encrypted vault."
        } catch {
            status = "Could not save encrypted vault."
        }
    }

    nonisolated private static func defaultVaultURL() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        return base.appendingPathComponent("ShushVault", isDirectory: true).appendingPathComponent("vault.shush")
    }

    private static func loadVault(fileURL: URL, passphrase: String) throws -> [SecretRow] {
        guard FileManager.default.fileExists(atPath: fileURL.path) else {
            return []
        }

        let encrypted = try JSONDecoder().decode(EncryptedVault.self, from: Data(contentsOf: fileURL))
        guard encrypted.version == formatVersion,
              encrypted.kdf == kdfName,
              encrypted.cipher == cipherName,
              encrypted.iterations == kdfIterations,
              let salt = Data(base64Encoded: encrypted.salt),
              let nonce = Data(base64Encoded: encrypted.nonce),
              let payload = Data(base64Encoded: encrypted.ciphertext),
              salt.count == saltLength,
              nonce.count == nonceLength,
              payload.count >= tagLength else {
            throw VaultError.invalidEnvelope
        }

        let key = pbkdf2SHA256(passphrase: passphrase, salt: salt, iterations: encrypted.iterations)
        let ciphertext = Data(payload.prefix(payload.count - tagLength))
        let tag = Data(payload.suffix(tagLength))
        let box = try AES.GCM.SealedBox(nonce: AES.GCM.Nonce(data: nonce), ciphertext: ciphertext, tag: tag)
        let plaintext = try AES.GCM.open(box, using: SymmetricKey(data: key))
        return try JSONDecoder().decode(VaultDocument.self, from: plaintext).records
    }

    private static func saveVault(rows: [SecretRow], fileURL: URL, passphrase: String) throws {
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)

        let salt = randomData(count: saltLength)
        let nonce = randomData(count: nonceLength)
        let key = pbkdf2SHA256(passphrase: passphrase, salt: salt, iterations: kdfIterations)
        let plaintext = try JSONEncoder().encode(VaultDocument(records: rows))
        let box = try AES.GCM.seal(plaintext, using: SymmetricKey(data: key), nonce: AES.GCM.Nonce(data: nonce))
        let payload = box.ciphertext + box.tag
        let encrypted = EncryptedVault(
            version: formatVersion,
            kdf: kdfName,
            iterations: kdfIterations,
            cipher: cipherName,
            salt: salt.base64EncodedString(),
            nonce: nonce.base64EncodedString(),
            ciphertext: payload.base64EncodedString()
        )
        try JSONEncoder().encode(encrypted).write(to: fileURL, options: .atomic)
    }

    private static func clean(_ value: String, fallback: String) -> String {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? fallback : trimmed
    }

    private static func isoNow() -> String {
        ISO8601DateFormatter().string(from: Date())
    }
}

private enum VaultError: Error {
    case invalidEnvelope
}

private func randomData(count: Int) -> Data {
    var data = Data(count: count)
    _ = data.withUnsafeMutableBytes { SecRandomCopyBytes(kSecRandomDefault, count, $0.baseAddress!) }
    return data
}

private func pbkdf2SHA256(passphrase: String, salt: Data, iterations: Int) -> Data {
    let password = Data(passphrase.utf8)
    var derived = Data()
    var blockIndex: UInt32 = 1

    while derived.count < keyLength {
        var saltAndIndex = Data(salt)
        saltAndIndex.append(contentsOf: withUnsafeBytes(of: blockIndex.bigEndian, Array.init))
        var u = Data(HMAC<SHA256>.authenticationCode(for: saltAndIndex, using: SymmetricKey(data: password)))
        var block = u

        if iterations > 1 {
            for _ in 2...iterations {
                u = Data(HMAC<SHA256>.authenticationCode(for: u, using: SymmetricKey(data: password)))
                for index in block.indices {
                    block[index] ^= u[index]
                }
            }
        }

        derived.append(block)
        blockIndex += 1
    }

    return Data(derived.prefix(keyLength))
}
