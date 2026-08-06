using zHWriter.Core.Models;
using zHWriter.Core.Services;

namespace zHWriter.Core.Tests;

public sealed class JournalPathAndTemplateTests
{
    [Fact]
    public void Calculates_default_path_for_leap_day()
    {
        var service = new JournalPathService();
        var settings = new AppSettings { DiaryRoot = Path.Combine(Path.GetTempPath(), "zHWriter-tests", "中文 空格") };
        var path = service.GetJournalPath(new DateOnly(2024, 2, 29), settings);
        Assert.EndsWith(Path.Combine("Journal", "2024", "Daily", "02", "2024-02-29.md"), path);
        Assert.True(service.IsInsideDiaryRoot(path, settings));
    }

    [Fact]
    public void Rejects_path_escape()
    {
        var service = new JournalPathService();
        var settings = new AppSettings { DiaryRoot = Path.GetTempPath(), DailyTemplateRelativePath = "../outside.md" };
        Assert.Throws<ArgumentException>(() => service.Validate(settings));
    }

    [Fact]
    public async Task Expands_known_template_variables_and_keeps_unknown_ones()
    {
        var root = CreateRoot();
        try
        {
            var paths = new JournalPathService(); var template = new TemplateService(paths); var settings = new AppSettings { DiaryRoot = root };
            Directory.CreateDirectory(Path.Combine(root, "Templates"));
            await File.WriteAllTextAsync(Path.Combine(root, "Templates", "Daily.md"), "{{date}} {{year}} {{weekday}} {{fileName}} {{unknown}} <% tp.file.title %>");
            var expanded = await template.ReadExpandedTemplateAsync(new DateOnly(2026, 8, 6), settings);
            Assert.Equal("2026-08-06 2026 星期四 2026-08-06 {{unknown}} 2026-08-06", expanded);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateRoot() { var root = Path.Combine(Path.GetTempPath(), "zHWriter-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
}
