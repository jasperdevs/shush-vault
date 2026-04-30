using System.Text;
using System.Text.Json;
using ShushVault.Core;

namespace ShushVault.Core.Tests;

[TestClass]
public sealed class VaultServiceTests
{
    [TestMethod]
    public async Task AddAsyncPersistsEncryptedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShushVault.Tests", Guid.NewGuid().ToString("N"));
        var service = new VaultService(root);
        service.Unlock("correct horse battery staple");

        await service.AddAsync("Default", "OPENAI_API_KEY", "sk-test-secret", "Dev", "OpenAI", "test secret");
        var records = await service.LoadAsync();

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("OPENAI_API_KEY", records[0].Name);
        Assert.AreEqual("sk-test-secret", records[0].Value);
        Assert.AreEqual("Default", records[0].Workspace);
        Assert.AreEqual("Dev", records[0].Environment);

        var vaultPath = Path.Combine(root, "vault.shush");
        var storedText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(vaultPath));
        Assert.IsTrue(storedText.Contains("pbkdf2-sha256", StringComparison.Ordinal));
        Assert.IsTrue(storedText.Contains("aes-256-gcm", StringComparison.Ordinal));
        Assert.IsFalse(storedText.Contains("sk-test-secret", StringComparison.Ordinal));
        Assert.IsFalse(storedText.Contains("OPENAI_API_KEY", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LoadAsyncRejectsWrongPassphrase()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShushVault.Tests", Guid.NewGuid().ToString("N"));
        var service = new VaultService(root);
        service.Unlock("right");
        await service.AddAsync("Default", "KEY", "value", "Dev", "", "");

        var wrong = new VaultService(root);
        wrong.Unlock("wrong");

        try
        {
            await wrong.LoadAsync();
            Assert.Fail("Wrong passphrase should not decrypt the vault.");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
        }
    }

    [TestMethod]
    public async Task SaveAsyncUsesRecoverableAtomicReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShushVault.Tests", Guid.NewGuid().ToString("N"));
        var service = new VaultService(root);
        service.Unlock("correct horse battery staple");

        await service.AddAsync("Default", "FIRST", "one", "Dev", "", "");
        await service.AddAsync("Default", "SECOND", "two", "Dev", "", "");
        await service.UpdateAsync((await service.LoadAsync())[0].Id, "Default", "SECOND", "three", "Dev", "", "");

        var backupPath = Path.Combine(root, "vault.shush.bak");
        Assert.IsTrue(File.Exists(Path.Combine(root, "vault.shush")));
        Assert.IsTrue(File.Exists(backupPath));

        var reloaded = new VaultService(root);
        reloaded.Unlock("correct horse battery staple");
        var records = await reloaded.LoadAsync();

        Assert.AreEqual(2, records.Count);
        Assert.IsTrue(records.Any(record => record.Name == "SECOND" && record.Value == "three"));
    }

    [TestMethod]
    public async Task LoadAsyncRejectsUnsupportedIterationCountBeforePbkdf()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShushVault.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var envelope = new VaultEnvelope(
            1,
            "pbkdf2-sha256",
            310_001,
            "aes-256-gcm",
            Convert.ToBase64String(new byte[16]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[32]));
        await File.WriteAllTextAsync(Path.Combine(root, "vault.shush"), JsonSerializer.Serialize(envelope));

        var service = new VaultService(root);
        service.Unlock("passphrase");

        await Assert.ThrowsExceptionAsync<System.Security.Cryptography.CryptographicException>(() => service.LoadAsync());
    }

    [TestMethod]
    public async Task LoadAsyncDecryptsCommittedV1Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShushVault.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(FindFixturePath(), Path.Combine(root, "vault.shush"));

        var service = new VaultService(root);
        service.Unlock("fixture-passphrase");

        var records = await service.LoadAsync();

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("Fixture", records[0].Workspace);
        Assert.AreEqual("FIXTURE_KEY", records[0].Name);
        Assert.AreEqual("fixture-secret", records[0].Value);
        Assert.AreEqual("Tests", records[0].Provider);
    }

    [TestMethod]
    public async Task LoadAsyncRejectsMalformedEnvelopes()
    {
        await AssertEnvelopeRejected(new VaultEnvelope(
            2,
            "pbkdf2-sha256",
            310_000,
            "aes-256-gcm",
            Convert.ToBase64String(new byte[16]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[32])));

        await AssertEnvelopeRejected(new VaultEnvelope(
            1,
            "argon2id",
            310_000,
            "aes-256-gcm",
            Convert.ToBase64String(new byte[16]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[32])));

        await AssertEnvelopeRejected(new VaultEnvelope(
            1,
            "pbkdf2-sha256",
            310_000,
            "aes-128-gcm",
            Convert.ToBase64String(new byte[16]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[32])));

        await AssertEnvelopeRejected(new VaultEnvelope(
            1,
            "pbkdf2-sha256",
            310_000,
            "aes-256-gcm",
            Convert.ToBase64String(new byte[15]),
            Convert.ToBase64String(new byte[12]),
            Convert.ToBase64String(new byte[32])));

        await AssertEnvelopeRejected(new VaultEnvelope(
            1,
            "pbkdf2-sha256",
            310_000,
            "aes-256-gcm",
            Convert.ToBase64String(new byte[16]),
            "***",
            Convert.ToBase64String(new byte[32])));
    }

    private static async Task AssertEnvelopeRejected(VaultEnvelope envelope)
    {
        var root = Path.Combine(Path.GetTempPath(), "ShushVault.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "vault.shush"), JsonSerializer.Serialize(envelope));

        var service = new VaultService(root);
        service.Unlock("passphrase");

        await Assert.ThrowsExceptionAsync<System.Security.Cryptography.CryptographicException>(() => service.LoadAsync());
    }

    private static string FindFixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var fixture = Path.Combine(directory.FullName, "tests", "fixtures", "vault-v1.fixture.json");
            if (File.Exists(fixture))
            {
                return fixture;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find vault fixture.");
    }
}
