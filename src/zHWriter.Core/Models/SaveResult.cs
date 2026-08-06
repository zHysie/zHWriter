namespace zHWriter.Core.Models;

/// <summary>Outcome of a journal save operation.</summary>
public sealed record SaveResult(bool Succeeded, string? ErrorMessage = null, string? BackupPath = null)
{
    public static SaveResult Success(string? backupPath) => new(true, null, backupPath);
    public static SaveResult Failure(string message) => new(false, message);
}
