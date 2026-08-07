using zHWriter.Core.Models;
using zHWriter.Core.Services;
using zHWriter.Infrastructure.FileSystem;

namespace zHWriter.IntegrationTests;

public sealed class JournalFileServiceTests
{
    [Fact]
    public async Task Concurrent_open_or_create_keeps_a_single_template_file()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var templates = new TemplateService(paths); var files = new JournalFileService(paths, templates); var settings = new AppSettings { DiaryRoot = root };
            var date = new DateOnly(2026, 8, 6);
            var documents = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => files.OpenOrCreateAsync(PeriodicNoteType.Daily, date, settings)));
            Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(documents[0].Path)!, "*.md"));
            Assert.All(documents, document => Assert.Contains("# 今日日记", document.Content));
            Assert.Single(documents.Where(document => document.WasCreated));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Atomic_save_keeps_content_and_backup()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var templates = new TemplateService(paths); var files = new JournalFileService(paths, templates); var settings = new AppSettings { DiaryRoot = root };
            var document = await files.OpenOrCreateAsync(PeriodicNoteType.Daily, new DateOnly(2026, 8, 6), settings);
            var result = await files.SaveAsync(document, "可靠保存的内容");
            Assert.True(result.Succeeded); Assert.Equal("可靠保存的内容", await File.ReadAllTextAsync(document.Path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Newer_temporary_save_is_discovered_and_restored_with_backup()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var templates = new TemplateService(paths); var files = new JournalFileService(paths, templates); var settings = new AppSettings { DiaryRoot = root };
            var document = await files.OpenOrCreateAsync(PeriodicNoteType.Daily, new DateOnly(2026, 8, 6), settings);
            await File.WriteAllTextAsync(document.Path, "正式内容");
            var temporary = Path.Combine(Path.GetDirectoryName(document.Path)!, "." + Path.GetFileName(document.Path) + ".zhw.tmp");
            await File.WriteAllTextAsync(temporary, "恢复内容"); File.SetLastWriteTimeUtc(temporary, DateTime.UtcNow.AddMinutes(1));
            Assert.Contains(temporary, await files.FindRecoverableTemporaryFilesAsync(settings));
            await files.RestoreTemporaryFileAsync(temporary);
            Assert.Equal("恢复内容", await File.ReadAllTextAsync(document.Path));
            Assert.True(File.Exists(document.Path + ".zhw.pre-recovery.bak"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Open_or_create_uses_correct_template_and_path_per_period()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var templates = new TemplateService(paths); var files = new JournalFileService(paths, templates); var settings = new AppSettings { DiaryRoot = root };
            await templates.EnsureDefaultTemplatesAsync(settings);
            var date = new DateOnly(2026, 8, 7);

            var daily = await files.OpenOrCreateAsync(PeriodicNoteType.Daily, date, settings);
            var weekly = await files.OpenOrCreateAsync(PeriodicNoteType.Weekly, date, settings);
            var monthly = await files.OpenOrCreateAsync(PeriodicNoteType.Monthly, date, settings);

            Assert.EndsWith(Path.Combine("Daily", "08", "2026-08-07.md"), daily.Path);
            Assert.EndsWith(Path.Combine("Weekly", "2026-32W.md"), weekly.Path);
            Assert.EndsWith(Path.Combine("Monthly", "2026-08.md"), monthly.Path);

            Assert.Contains("# 今日日记", daily.Content);
            Assert.Contains("# 周记", weekly.Content);
            Assert.Contains("第 32 周", weekly.Content);
            Assert.Contains("# 月记", monthly.Content);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Open_existing_file_does_not_overwrite_content()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var templates = new TemplateService(paths); var files = new JournalFileService(paths, templates); var settings = new AppSettings { DiaryRoot = root };
            var date = new DateOnly(2026, 8, 7);
            var created = await files.OpenOrCreateAsync(PeriodicNoteType.Daily, date, settings);
            Assert.True(created.WasCreated);
            await File.WriteAllTextAsync(created.Path, "用户已在外部修改的内容");

            var reopened = await files.OpenOrCreateAsync(PeriodicNoteType.Daily, date, settings);
            Assert.False(reopened.WasCreated);
            Assert.Equal("用户已在外部修改的内容", reopened.Content);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Creates_missing_directories_for_weekly_and_monthly()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var templates = new TemplateService(paths); var files = new JournalFileService(paths, templates); var settings = new AppSettings { DiaryRoot = root };
            await templates.EnsureDefaultTemplatesAsync(settings);

            var weekly = await files.OpenOrCreateAsync(PeriodicNoteType.Weekly, new DateOnly(2026, 8, 7), settings);
            Assert.True(Directory.Exists(Path.Combine(root, "Weekly")));
            Assert.True(File.Exists(weekly.Path));

            var monthly = await files.OpenOrCreateAsync(PeriodicNoteType.Monthly, new DateOnly(2026, 8, 7), settings);
            Assert.True(Directory.Exists(Path.Combine(root, "Monthly")));
            Assert.True(File.Exists(monthly.Path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateRoot() { var root = Path.Combine(Path.GetTempPath(), "zHWriter-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
}
