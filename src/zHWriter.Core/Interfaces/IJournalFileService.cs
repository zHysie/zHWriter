using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Creates, loads and atomically persists periodic note files (daily / weekly / monthly).</summary>
public interface IJournalFileService
{
    Task<JournalDocument> OpenOrCreateAsync(PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default);
    Task<JournalDocument> LoadAsync(PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default);
    Task<SaveResult> SaveAsync(JournalDocument document, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> FindRecoverableTemporaryFilesAsync(AppSettings settings, CancellationToken cancellationToken = default);
    /// <summary>Restores a previously detected same-directory temporary save file.</summary>
    Task RestoreTemporaryFileAsync(string temporaryPath, CancellationToken cancellationToken = default);
}
