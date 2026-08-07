using zHWriter.Core.Models;
using zHWriter.Core.Services;

namespace zHWriter.Core.Tests;

public sealed class JournalPathAndTemplateTests
{
    [Theory]
    [InlineData(2024, 2, 29, PeriodicNoteType.Daily, "Daily/02/2024-02-29.md")]
    [InlineData(2026, 8, 7, PeriodicNoteType.Daily, "Daily/08/2026-08-07.md")]
    [InlineData(2026, 8, 7, PeriodicNoteType.Weekly, "Weekly/2026-32W.md")]
    [InlineData(2026, 8, 7, PeriodicNoteType.Monthly, "Monthly/2026-08.md")]
    [InlineData(2026, 1, 5, PeriodicNoteType.Monthly, "Monthly/2026-01.md")]
    public void Calculates_periodic_note_paths(int year, int month, int day, PeriodicNoteType type, string expectedRelative)
    {
        var service = new JournalPathService();
        var settings = new AppSettings { DiaryRoot = Path.Combine(Path.GetTempPath(), "zHWriter-tests", "中文 空格") };
        var path = service.GetNotePath(type, new DateOnly(year, month, day), settings);
        Assert.EndsWith(expectedRelative.Replace('/', Path.DirectorySeparatorChar), path);
        Assert.True(service.IsInsideDiaryRoot(path, settings));
    }

    [Theory]
    [InlineData(2026, 8, 7, "2026-32W")]   // 需求示例
    [InlineData(2025, 12, 31, "2026-01W")] // 12 月底属于下一年的第 1 周
    [InlineData(2026, 1, 1, "2026-01W")]   // 1 月初的第 1 周
    [InlineData(2026, 1, 4, "2026-01W")]
    [InlineData(2026, 1, 5, "2026-02W")]   // 周一开始的第 2 周
    [InlineData(2018, 12, 31, "2019-01W")] // 12 月底属于下一年的第 1 周
    [InlineData(2016, 1, 1, "2015-53W")]   // 1 月初属于上一年的第 53 周
    [InlineData(2026, 12, 31, "2026-53W")] // 2026 有 53 周
    [InlineData(2020, 12, 31, "2020-53W")] // 2020 有 53 周
    public void Calculates_iso_week_paths_across_year_boundaries(int year, int month, int day, string expectedFile)
    {
        var service = new JournalPathService();
        var settings = new AppSettings { DiaryRoot = Path.GetTempPath() };
        var path = service.GetNotePath(PeriodicNoteType.Weekly, new DateOnly(year, month, day), settings);
        Assert.EndsWith(Path.Combine("Weekly", expectedFile + ".md"), path);
    }

    [Fact]
    public void Rejects_path_escape()
    {
        var service = new JournalPathService();
        var settings = new AppSettings { DiaryRoot = Path.GetTempPath(), TemplatesDirectory = "../outside" };
        Assert.Throws<ArgumentException>(() => service.Validate(settings));
    }

    [Fact]
    public async Task Expands_known_daily_variables_and_keeps_unknown_ones()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var template = new TemplateService(paths); var settings = new AppSettings { DiaryRoot = root };
            Directory.CreateDirectory(Path.Combine(root, "Templates"));
            await File.WriteAllTextAsync(Path.Combine(root, "Templates", "Daily.md"), "{{date}} {{year}} {{weekday}} {{fileName}} {{unknown}} <% tp.file.title %>");
            var expanded = await template.ReadExpandedTemplateAsync(PeriodicNoteType.Daily, new DateOnly(2026, 8, 6), settings);
            Assert.Equal("2026-08-06 2026 星期四 2026-08-06 {{unknown}} 2026-08-06", expanded);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Expands_weekly_variables()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var template = new TemplateService(paths); var settings = new AppSettings { DiaryRoot = root };
            Directory.CreateDirectory(Path.Combine(root, "Templates"));
            await File.WriteAllTextAsync(Path.Combine(root, "Templates", "Weekly.md"), "{{weekYear}} {{week}} {{weekStart}} {{weekEnd}} {{date:yyyy年MM月dd日}} {{fileName}}");
            var expanded = await template.ReadExpandedTemplateAsync(PeriodicNoteType.Weekly, new DateOnly(2026, 8, 7), settings);
            Assert.Equal("2026 32 2026-08-03 2026-08-09 2026年08月07日 2026-32W", expanded);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Expands_monthly_variables()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var template = new TemplateService(paths); var settings = new AppSettings { DiaryRoot = root };
            Directory.CreateDirectory(Path.Combine(root, "Templates"));
            await File.WriteAllTextAsync(Path.Combine(root, "Templates", "Monthly.md"), "{{year}} {{month}} {{fileName}} {{date}}");
            var expanded = await template.ReadExpandedTemplateAsync(PeriodicNoteType.Monthly, new DateOnly(2026, 8, 7), settings);
            Assert.Equal("2026 08 2026-08 2026-08-07", expanded);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Ensure_default_templates_creates_all_and_never_overwrites_existing()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var template = new TemplateService(paths); var settings = new AppSettings { DiaryRoot = root };
            Directory.CreateDirectory(Path.Combine(root, "Templates"));
            await File.WriteAllTextAsync(Path.Combine(root, "Templates", "Daily.md"), "自定义内容");
            await template.EnsureDefaultTemplatesAsync(settings);

            Assert.Equal("自定义内容", await File.ReadAllTextAsync(Path.Combine(root, "Templates", "Daily.md")));
            Assert.True(File.Exists(Path.Combine(root, "Templates", "Weekly.md")));
            Assert.True(File.Exists(Path.Combine(root, "Templates", "Monthly.md")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateRoot() { var root = Path.Combine(Path.GetTempPath(), "zHWriter-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
}
