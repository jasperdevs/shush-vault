namespace ShushVault.Core;

public enum ImportConflictMode
{
    Skip,
    Overwrite,
    Rename
}

public enum ImportPreviewStatus
{
    Ready,
    Conflict,
    Ignored,
    Invalid
}

public sealed record ImportPreviewItem(
    int Line,
    string Key,
    string Value,
    ImportPreviewStatus Status,
    string Message);
