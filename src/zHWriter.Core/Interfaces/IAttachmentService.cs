using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Saves clipboard-derived attachments beside the current periodic note.</summary>
public interface IAttachmentService
{
    Task<IReadOnlyList<string>> CopyImageFilesAsync(IEnumerable<string> sourceFiles, PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default);
    string BuildMarkdownReference(string attachmentPath, PeriodicNoteType type, DateOnly date, AppSettings settings);
}
