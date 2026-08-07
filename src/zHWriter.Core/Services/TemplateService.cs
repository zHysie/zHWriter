using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Core.Services;

/// <summary>UTF-8 template creation (never overwriting existing files) and periodic-variable expansion.</summary>
public sealed class TemplateService : ITemplateService
{
    private readonly IJournalPathService _paths;
    public TemplateService(IJournalPathService paths) => _paths = paths;

    public async Task EnsureDefaultTemplatesAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        foreach (var type in Enum.GetValues<PeriodicNoteType>()) await EnsureTemplateAsync(type, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadExpandedTemplateAsync(PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var template = _paths.GetTemplatePath(type, settings);
        if (!File.Exists(template)) await EnsureTemplateAsync(type, settings, cancellationToken).ConfigureAwait(false);
        string content;
        try { content = await File.ReadAllTextAsync(template, Encoding.UTF8, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { throw new IOException($"无法读取{Describe(type)}模板：{template}。请修复模板或在设置中重新选择路径。", exception); }

        var fileName = Path.GetFileNameWithoutExtension(_paths.GetNotePath(type, date, settings));
        var dt = date.ToDateTime(TimeOnly.MinValue);
        content = Regex.Replace(content, @"\{\{date(?::([^}]+))?\}\}", match => dt.ToString(match.Groups[1].Success ? match.Groups[1].Value : "yyyy-MM-dd", CultureInfo.InvariantCulture));
        content = content.Replace("{{year}}", dt.ToString("yyyy", CultureInfo.InvariantCulture))
            .Replace("{{month}}", dt.ToString("MM", CultureInfo.InvariantCulture))
            .Replace("{{day}}", dt.ToString("dd", CultureInfo.InvariantCulture))
            .Replace("{{weekday}}", "星期" + "日一二三四五六"[(int)dt.DayOfWeek])
            .Replace("{{fileName}}", fileName);

        if (type == PeriodicNoteType.Weekly)
        {
            var monday = JournalPathService.GetIsoWeekMonday(date);
            content = content.Replace("{{week}}", ISOWeek.GetWeekOfYear(dt).ToString(CultureInfo.InvariantCulture))
                .Replace("{{weekYear}}", ISOWeek.GetYear(dt).ToString(CultureInfo.InvariantCulture))
                .Replace("{{weekStart}}", monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Replace("{{weekEnd}}", monday.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        content = content.Replace("<% tp.file.title %>", fileName);
        return content;
    }

    private async Task EnsureTemplateAsync(PeriodicNoteType type, AppSettings settings, CancellationToken cancellationToken)
    {
        var template = _paths.GetTemplatePath(type, settings);
        if (File.Exists(template)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(template)!);
        await File.WriteAllTextAsync(template, DefaultTemplate(type), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static string DefaultTemplate(PeriodicNoteType type) => type switch
    {
        PeriodicNoteType.Daily => "---\n日期: {{date:yyyy-MM-dd}}\ntags:\n  - 日记\n---\n## 习惯打卡\n- [ ] 健身\n- [ ] 背单词\n\n\n\n# 今日日记\n",
        PeriodicNoteType.Weekly => "---\n日期: {{date:yyyy-MM-dd}}\n周: {{weekYear}} 第 {{week}} 周\ntags:\n  - 周记\n---\n\n# 周记 {{weekYear}} 第 {{week}} 周\n\n（{{weekStart}} ~ {{weekEnd}}）\n",
        PeriodicNoteType.Monthly => "---\n日期: {{date:yyyy-MM-dd}}\n月份: {{year}} 年 {{month}} 月\ntags:\n  - 月记\n---\n\n# 月记 {{year}} 年 {{month}} 月\n",
        _ => string.Empty
    };

    private static string Describe(PeriodicNoteType type) => type switch
    {
        PeriodicNoteType.Daily => "日记",
        PeriodicNoteType.Weekly => "周记",
        PeriodicNoteType.Monthly => "月记",
        _ => "周期"
    };
}
