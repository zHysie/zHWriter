using System.Text;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Infrastructure.FileSystem;

/// <summary>Exclusive creation and same-directory atomic saves for daily Markdown files.</summary>
public sealed class JournalFileService : IJournalFileService
{
    private readonly IJournalPathService _paths;
    private readonly ITemplateService _templates;
    public JournalFileService(IJournalPathService paths, ITemplateService templates) => (_paths, _templates) = (paths, templates);

    public async Task<JournalDocument> OpenOrCreateAsync(DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var path = _paths.GetJournalPath(date, settings);
        if (File.Exists(path)) return await LoadAtPathAsync(date, path, false, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = await _templates.ReadExpandedTemplateAsync(date, settings, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path))
        {
            return await LoadAtPathAsync(date, path, false, cancellationToken).ConfigureAwait(false);
        }
        return await LoadAtPathAsync(date, path, true, cancellationToken).ConfigureAwait(false);
    }

    public Task<JournalDocument> LoadAsync(DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
        => LoadAtPathAsync(date, _paths.GetJournalPath(date, settings), false, cancellationToken);

    public async Task<SaveResult> SaveAsync(JournalDocument document, string content, CancellationToken cancellationToken = default)
    {
        var target = document.Path;
        var directory = Path.GetDirectoryName(target)!;
        var fileName = Path.GetFileName(target);
        var temporary = Path.Combine(directory, "." + fileName + ".zhw.tmp");
        var backup = Path.Combine(directory, "." + fileName + ".zhw.bak");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (File.Exists(target))
            {
                try { File.Replace(temporary, target, backup, true); }
                catch (PlatformNotSupportedException) { File.Copy(target, backup, true); File.Move(temporary, target, true); }
            }
            else File.Move(temporary, target);
            return SaveResult.Success(File.Exists(backup) ? backup : null);
        }
        catch (Exception exception)
        {
            return SaveResult.Failure($"无法保存日记：{target}。正文仍保留在编辑器中。原因：{exception.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> FindRecoverableTemporaryFilesAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.Validate(settings);
        var journalRoot = Path.Combine(settings.DiaryRoot, "Journal");
        if (!Directory.Exists(journalRoot)) return Array.Empty<string>();
        return await Task.Run(() => Directory.EnumerateFiles(journalRoot, ".*.zhw.tmp", SearchOption.AllDirectories)
            .Where(path => { var target = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path)[1..^8]); return !File.Exists(target) || File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(target); })
            .ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public Task RestoreTemporaryFileAsync(string temporaryPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(temporaryPath) || !temporaryPath.EndsWith(".zhw.tmp", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("未找到可恢复的临时保存文件。", temporaryPath);
        var name = Path.GetFileName(temporaryPath);
        if (name.Length <= 9 || name[0] != '.') throw new InvalidDataException("临时保存文件名无效。");
        var target = Path.Combine(Path.GetDirectoryName(temporaryPath)!, name[1..^8]);
        var backup = target + ".zhw.pre-recovery.bak";
        if (File.Exists(target)) File.Copy(target, backup, true);
        File.Copy(temporaryPath, target, true);
        File.Delete(temporaryPath);
        return Task.CompletedTask;
    }

    private static async Task<JournalDocument> LoadAtPathAsync(DateOnly date, string path, bool wasCreated, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"未找到日记文件：{path}", path);
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return new JournalDocument(date, path, content, File.GetLastWriteTimeUtc(path), wasCreated);
    }
}
