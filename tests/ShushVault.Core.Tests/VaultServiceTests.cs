using System.Text;
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
}
