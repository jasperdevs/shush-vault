namespace ShushVault.Core;

public sealed record SecretRecord(
    string Id,
    string Workspace,
    string Name,
    string Value,
    string Environment,
    string Provider,
    string Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static SecretRecord Create(string workspace, string name, string value, string environment, string provider, string notes)
    {
        var now = DateTimeOffset.UtcNow;
        return new SecretRecord(
            Guid.NewGuid().ToString("N"),
            Clean(workspace, "Default"),
            name.Trim(),
            value,
            Clean(environment, "Dev"),
            provider.Trim(),
            notes.Trim(),
            now,
            now);
    }

    public SecretRecord Update(string workspace, string name, string value, string environment, string provider, string notes)
        => this with
        {
            Workspace = Clean(workspace, "Default"),
            Name = name.Trim(),
            Value = value,
            Environment = Clean(environment, "Dev"),
            Provider = provider.Trim(),
            Notes = notes.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static string Clean(string value, string fallback)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
