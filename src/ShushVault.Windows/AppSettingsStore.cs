using System.Text.Json;

namespace ShushVault.Windows;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string filePath;

    public AppSettingsStore(string? rootOverride = null)
    {
        var root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShushVault");
        Directory.CreateDirectory(root);
        filePath = Path.Combine(root, "settings.json");

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShushVault",
            "settings.json");
        if (!File.Exists(filePath) && File.Exists(legacyPath))
        {
            try
            {
                File.Copy(legacyPath, filePath, overwrite: false);
            }
            catch
            {
            }
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<AppSettings>(stream) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            using var stream = File.Create(filePath);
            JsonSerializer.Serialize(stream, settings, Options);
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }
}

internal sealed class AppSettings
{
    public int ClipboardClearSeconds { get; set; } = 30;
    public string? DefaultEnvironment { get; set; } = "Dev";
    public string? DefaultWorkspace { get; set; } = "Default";
}
