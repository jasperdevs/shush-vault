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
