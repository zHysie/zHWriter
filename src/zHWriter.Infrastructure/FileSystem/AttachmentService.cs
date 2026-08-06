using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Infrastructure.FileSystem;

/// <summary>Copies image files into the entry-local assets directory without overwriting attachments.</summary>
public sealed class AttachmentService : IAttachmentService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    private readonly IJournalPathService _paths;
    public AttachmentService(IJournalPathService paths) => _paths = paths;

    public async Task<IReadOnlyList<string>> CopyImageFilesAsync(IEnumerable<string> sourceFiles, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = _paths.GetAttachmentDirectory(date, settings);
        Directory.CreateDirectory(directory);
        var result = new List<string>();
        foreach (var source in sourceFiles.Where(File.Exists))
        {
            var extension = Path.GetExtension(source);
            if (!Extensions.Contains(extension)) continue;
            var destination = UniquePath(directory, Sanitize(Path.GetFileNameWithoutExtension(source)) + extension);
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            result.Add(destination);
        }
        return result;
    }

    public string BuildMarkdownReference(string attachmentPath, DateOnly date, AppSettings settings)
    {
        var journalDirectory = Path.GetDirectoryName(_paths.GetJournalPath(date, settings))!;
        var relative = Path.GetRelativePath(journalDirectory, attachmentPath).Replace('\\', '/');
        return $"![]({relative})";
    }

    public static string UniquePath(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        var index = 2;
        while (File.Exists(candidate)) candidate = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)}-{index++}{Path.GetExtension(name)}");
        return candidate;
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
}
