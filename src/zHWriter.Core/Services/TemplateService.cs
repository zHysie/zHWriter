using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Core.Services;

/// <summary>UTF-8 template creation and explicit daily-variable expansion.</summary>
public sealed class TemplateService : ITemplateService
{
    private readonly IJournalPathService _paths;
    public TemplateService(IJournalPathService paths) => _paths = paths;

    public async Task EnsureDefaultTemplateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var template = _paths.GetTemplatePath(settings);
        if (File.Exists(template)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(template)!);
        const string content = "---\n日期: {{date:yyyy-MM-dd}}\ntags:\n  - 日记\n---\n## 习惯打卡\n- [ ] 健身\n- [ ] 背单词\n\n\n\n# 今日日记\n";
        await File.WriteAllTextAsync(template, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadExpandedTemplateAsync(DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var template = _paths.GetTemplatePath(settings);
        if (!File.Exists(template)) await EnsureDefaultTemplateAsync(settings, cancellationToken).ConfigureAwait(false);
        string content;
        try { content = await File.ReadAllTextAsync(template, Encoding.UTF8, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { throw new IOException($"无法读取日记模板：{template}。请修复模板或在设置中重新选择路径。", exception); }
        var dt = date.ToDateTime(TimeOnly.MinValue);
        content = Regex.Replace(content, @"\{\{date(?::([^}]+))?\}\}", match => dt.ToString(match.Groups[1].Success ? match.Groups[1].Value : "yyyy-MM-dd", CultureInfo.InvariantCulture));
        content = content.Replace("{{year}}", dt.ToString("yyyy", CultureInfo.InvariantCulture))
            .Replace("{{month}}", dt.ToString("MM", CultureInfo.InvariantCulture))
            .Replace("{{day}}", dt.ToString("dd", CultureInfo.InvariantCulture))
            .Replace("{{weekday}}", "星期" + "日一二三四五六"[(int)dt.DayOfWeek])
            .Replace("{{fileName}}", JournalPathService.FormatPattern(date, settings.JournalFileNamePattern))
            .Replace("<% tp.file.title %>", JournalPathService.FormatPattern(date, settings.JournalFileNamePattern));
        return content;
    }
}
