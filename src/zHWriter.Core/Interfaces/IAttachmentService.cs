using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Saves clipboard-derived attachments beside the current journal entry.</summary>
public interface IAttachmentService
{
    Task<IReadOnlyList<string>> CopyImageFilesAsync(IEnumerable<string> sourceFiles, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default);
    string BuildMarkdownReference(string attachmentPath, DateOnly date, AppSettings settings);
}
