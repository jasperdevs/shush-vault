import XCTest
@testable import ShushVaultMac

@MainActor
final class VaultStoreTests: XCTestCase {
    func testUnlockDecryptsCommittedV1Fixture() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("ShushVaultMacTests")
            .appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)

        let vaultURL = root.appendingPathComponent("vault.shush")
        try FileManager.default.copyItem(at: fixtureURL(), to: vaultURL)

        let store = VaultStore(fileURL: vaultURL)
        store.unlock(passphrase: "fixture-passphrase")

        XCTAssertTrue(store.isUnlocked)
        XCTAssertEqual(store.rows.count, 1)
        XCTAssertEqual(store.rows[0].workspace, "Fixture")
        XCTAssertEqual(store.rows[0].name, "FIXTURE_KEY")
        XCTAssertEqual(store.rows[0].value, "fixture-secret")
        XCTAssertEqual(store.rows[0].provider, "Tests")
    }

    func testImportUpdateDeleteAndExportOperations() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("ShushVaultMacTests")
            .appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)

        let store = VaultStore(fileURL: root.appendingPathComponent("vault.shush"))
        store.unlock(passphrase: "test-passphrase")
        store.importEnv(content: "API_KEY=one\nQUOTED=\"two words\"", workspace: "Fixture", environment: "Dev", provider: "Tests", conflict: "Skip")

        XCTAssertEqual(store.rows.count, 2)
        XCTAssertTrue(store.rows.contains { $0.name == "API_KEY" && $0.value == "one" })
        XCTAssertTrue(store.rows.contains { $0.name == "QUOTED" && $0.value == "two words" })

        let row = try XCTUnwrap(store.rows.first { $0.name == "API_KEY" })
        store.update(id: row.id, workspace: "Fixture", name: "API_KEY", value: "changed", environment: "Prod", provider: "Tests", notes: "updated")

        let updated = try XCTUnwrap(store.rows.first { $0.id == row.id })
        XCTAssertEqual(updated.value, "changed")
        XCTAssertEqual(updated.environment, "Prod")
        XCTAssertEqual(updated.notes, "updated")

        store.delete(updated)
        XCTAssertFalse(store.rows.contains { $0.id == row.id })
    }

    private func fixtureURL() throws -> URL {
        var url = URL(fileURLWithPath: #filePath)
        for _ in 0..<4 {
            url.deleteLastPathComponent()
        }

        let fixture = url.appendingPathComponent("tests/fixtures/vault-v1.fixture.json")
        guard FileManager.default.fileExists(atPath: fixture.path) else {
            throw NSError(domain: "ShushVaultMacTests", code: 1)
        }

        return fixture
    }
}
