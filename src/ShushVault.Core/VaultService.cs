using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShushVault.Core;

public sealed class VaultService
{
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int KeyLength = 32;
    private const int TagLength = 16;
    private const int KdfIterations = 310_000;
    private const string KdfName = "pbkdf2-sha256";
    private const string CipherName = "aes-256-gcm";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string filePath;
    private string? passphrase;

    public VaultService(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShushVault");

        Directory.CreateDirectory(root);
        filePath = Path.Combine(root, "vault.shush");
    }

    public bool IsUnlocked => !string.IsNullOrWhiteSpace(passphrase);

    public string FilePath => filePath;

    public void Unlock(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Vault passphrase is required.", nameof(value));
        }

        passphrase = value;
    }

    public void Lock()
        => passphrase = null;

    public async Task<IReadOnlyList<SecretRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        EnsureUnlocked();

        var encryptedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (encryptedBytes.Length == 0)
        {
            return [];
        }

        var envelope = JsonSerializer.Deserialize<VaultEnvelope>(encryptedBytes, JsonOptions)
            ?? throw new CryptographicException("Invalid vault file.");

        var jsonBytes = Decrypt(envelope, passphrase!);
        var records = DeserializeRecords(jsonBytes);
        CryptographicOperations.ZeroMemory(jsonBytes);
        return records;
    }

    public async Task<IReadOnlyList<string>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken))
            .Select(record => record.Workspace)
            .Append("Default")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<SecretRecord>> AddAsync(
        string workspace,
        string name,
        string value,
        string environment,
        string provider,
        string notes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Secret value is required.", nameof(value));
        }

        var records = (await LoadAsync(cancellationToken)).ToList();
        records.Insert(0, SecretRecord.Create(workspace, name, value, environment, provider, notes));
        await SaveAsync(records, cancellationToken);
        return records;
    }

    public async Task<IReadOnlyList<SecretRecord>> UpdateAsync(
        string id,
        string workspace,
        string name,
        string value,
        string environment,
        string provider,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var records = (await LoadAsync(cancellationToken)).ToList();
        var index = records.FindIndex(record => record.Id == id);
        if (index < 0)
        {
            return records;
        }

        records[index] = records[index].Update(workspace, name, value, environment, provider, notes);
        await SaveAsync(records, cancellationToken);
        return records;
    }

    public async Task<IReadOnlyList<SecretRecord>> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var records = (await LoadAsync(cancellationToken)).Where(record => record.Id != id).ToList();
        await SaveAsync(records, cancellationToken);
        return records;
    }

    public IReadOnlyList<ImportPreviewItem> PreviewEnv(string content, string workspace, string environment)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var preview = new List<ImportPreviewItem>();

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                preview.Add(new ImportPreviewItem(lineNumber, string.Empty, string.Empty, ImportPreviewStatus.Ignored, "Ignored"));
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                preview.Add(new ImportPreviewItem(lineNumber, string.Empty, string.Empty, ImportPreviewStatus.Invalid, "Missing key/value separator"));
                continue;
            }

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            preview.Add(new ImportPreviewItem(lineNumber, key, value, ImportPreviewStatus.Ready, "Ready"));
        }

        return preview;
    }

    public async Task<IReadOnlyList<SecretRecord>> ImportEnvAsync(
        string content,
        string workspace,
        string environment,
        string provider,
        ImportConflictMode conflictMode,
        CancellationToken cancellationToken = default)
    {
        var records = (await LoadAsync(cancellationToken)).ToList();
        var items = PreviewEnv(content, workspace, environment)
            .Where(item => item.Status == ImportPreviewStatus.Ready)
            .ToList();

        foreach (var item in items)
        {
            var existingIndex = records.FindIndex(record =>
                record.Workspace.Equals(workspace, StringComparison.OrdinalIgnoreCase) &&
                record.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase) &&
                record.Name.Equals(item.Key, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0 && conflictMode == ImportConflictMode.Skip)
            {
                continue;
            }

            if (existingIndex >= 0 && conflictMode == ImportConflictMode.Overwrite)
            {
                records[existingIndex] = records[existingIndex].Update(workspace, item.Key, item.Value, environment, provider, records[existingIndex].Notes);
                continue;
            }

            var key = existingIndex >= 0 ? $"{item.Key}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}" : item.Key;
            records.Insert(0, SecretRecord.Create(workspace, key, item.Value, environment, provider, ".env import"));
        }

        await SaveAsync(records, cancellationToken);
        return records;
    }

    public string ExportEnv(IEnumerable<SecretRecord> records)
        => string.Join(Environment.NewLine, records.Select(record => $"{record.Name}={QuoteIfNeeded(record.Value)}"));

    private async Task SaveAsync(IReadOnlyList<SecretRecord> records, CancellationToken cancellationToken)
    {
        EnsureUnlocked();

        var json = JsonSerializer.SerializeToUtf8Bytes(new VaultDocument(records.ToList()), JsonOptions);
        var envelope = Encrypt(json, passphrase!);
        await File.WriteAllBytesAsync(filePath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions), cancellationToken);
        CryptographicOperations.ZeroMemory(json);
    }

    private void EnsureUnlocked()
    {
        if (!IsUnlocked)
        {
            throw new InvalidOperationException("Enter the vault passphrase first.");
        }
    }

    private static VaultEnvelope Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = DeriveKey(passphrase, salt, KdfIterations);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new VaultEnvelope(
            1,
            KdfName,
            KdfIterations,
            CipherName,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String([.. ciphertext, .. tag]));
    }

    private static byte[] Decrypt(VaultEnvelope envelope, string passphrase)
    {
        if (envelope is not { Version: 1, Kdf: KdfName, Iterations: KdfIterations, Cipher: CipherName })
        {
            throw new CryptographicException("Unsupported vault format.");
        }

        var salt = Convert.FromBase64String(envelope.Salt);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var payload = Convert.FromBase64String(envelope.Ciphertext);
        if (salt.Length != SaltLength || nonce.Length != NonceLength || payload.Length < TagLength)
        {
            throw new CryptographicException("Invalid vault file.");
        }

        var ciphertext = payload[..^TagLength];
        var tag = payload[^TagLength..];
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(passphrase, salt, envelope.Iterations);

        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeyLength);

    private static List<SecretRecord> DeserializeRecords(byte[] jsonBytes)
    {
        try
        {
            var document = JsonSerializer.Deserialize<VaultDocument>(jsonBytes, JsonOptions);
            if (document is not null)
            {
                return document.Records;
            }
        }
        catch (JsonException)
        {
        }

        return JsonSerializer.Deserialize<List<SecretRecord>>(jsonBytes, JsonOptions) ?? [];
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Any(char.IsWhiteSpace) || value.Contains('#') || value.Contains('"'))
        {
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}

public sealed record VaultEnvelope(
    int Version,
    string Kdf,
    int Iterations,
    string Cipher,
    string Salt,
    string Nonce,
    string Ciphertext);

internal sealed record VaultDocument(List<SecretRecord> Records);
