using System.Text;
using System.Collections.Concurrent;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Infrastructure.FileSystem;

/// <summary>Exclusive creation (serialized per note path) and same-directory atomic saves for periodic Markdown files.</summary>
public sealed class JournalFileService : IJournalFileService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CreationGates = new();

    private readonly IJournalPathService _paths;
    private readonly ITemplateService _templates;
    public JournalFileService(IJournalPathService paths, ITemplateService templates) => (_paths, _templates) = (paths, templates);

    public async Task<JournalDocument> OpenOrCreateAsync(PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var path = _paths.GetNotePath(type, date, settings);
        var gate = CreationGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return await LoadAtPathAsync(type, date, path, false, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var content = await _templates.ReadExpandedTemplateAsync(type, date, settings, cancellationToken).ConfigureAwait(false);
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
                var bytes = new UTF8Encoding(false).GetBytes(content);
                await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another process created the file concurrently; load its content instead.
                return await LoadAtPathAsync(type, date, path, false, cancellationToken).ConfigureAwait(false);
            }
            return await LoadAtPathAsync(type, date, path, true, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public Task<JournalDocument> LoadAsync(PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
        => LoadAtPathAsync(type, date, _paths.GetNotePath(type, date, settings), false, cancellationToken);

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
            return SaveResult.Failure($"无法保存笔记：{target}。正文仍保留在编辑器中。原因：{exception.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> FindRecoverableTemporaryFilesAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.Validate(settings);
        if (!Directory.Exists(settings.DiaryRoot)) return Array.Empty<string>();
        return await Task.Run(() => Directory.EnumerateFiles(settings.DiaryRoot, ".*.zhw.tmp", SearchOption.AllDirectories)
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

    private static async Task<JournalDocument> LoadAtPathAsync(PeriodicNoteType type, DateOnly date, string path, bool wasCreated, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"未找到笔记文件：{path}", path);
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return new JournalDocument(type, date, path, content, File.GetLastWriteTimeUtc(path), wasCreated);
    }
}
