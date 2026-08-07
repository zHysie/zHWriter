using System.Globalization;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Core.Services;

/// <summary>
/// Calculates periodic-note paths under the note-library root using the fixed layout:
/// Daily/MM/yyyy-MM-dd.md, Weekly/ISOWeekYear-ISOWeekNumberW.md, Monthly/yyyy-MM.md.
/// </summary>
public sealed class JournalPathService : IJournalPathService
{
    public string GetNotePath(PeriodicNoteType type, DateOnly date, AppSettings settings)
    {
        Validate(settings);
        var relative = type switch
        {
            PeriodicNoteType.Daily => $"Daily/{date.Month:D2}/{date:yyyy-MM-dd}.md",
            PeriodicNoteType.Weekly => $"Weekly/{GetIsoWeekYear(date)}-{GetIsoWeekNumber(date):D2}W.md",
            PeriodicNoteType.Monthly => $"Monthly/{date:yyyy-MM}.md",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var path = ResolveRelative(settings.DiaryRoot, relative);
        EnsureInside(path, settings);
        return path;
    }

    public string GetTemplatePath(PeriodicNoteType type, AppSettings settings)
    {
        Validate(settings);
        return ResolveRelative(settings.DiaryRoot, $"{settings.TemplatesDirectory}/{type}.md");
    }

    public string GetAttachmentDirectory(PeriodicNoteType type, DateOnly date, AppSettings settings)
    {
        ValidateFolderName(settings.AttachmentFolderName, nameof(settings.AttachmentFolderName));
        var result = Path.Combine(Path.GetDirectoryName(GetNotePath(type, date, settings))!, settings.AttachmentFolderName);
        EnsureInside(result, settings);
        return result;
    }

    public bool IsInsideDiaryRoot(string path, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DiaryRoot)) return false;
        var root = Path.GetFullPath(settings.DiaryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public void Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DiaryRoot)) throw new ArgumentException("尚未选择笔记库根目录。", nameof(settings));
        _ = Path.GetFullPath(settings.DiaryRoot);
        ValidateRelativePath(settings.TemplatesDirectory, nameof(settings.TemplatesDirectory));
        ValidateFolderName(settings.AttachmentFolderName, nameof(settings.AttachmentFolderName));
    }

    /// <summary>ISO 8601 week number (1-53) of the given date.</summary>
    public static int GetIsoWeekNumber(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>ISO 8601 week-numbering year of the given date (differs from the calendar year around New Year).</summary>
    public static int GetIsoWeekYear(DateOnly date) => ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>The Monday of the ISO week containing the given date.</summary>
    public static DateOnly GetIsoWeekMonday(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        var monday = value.AddDays(1 - (int)value.DayOfWeek); // Sunday = 0 per DayOfWeek.
        return DateOnly.FromDateTime(monday);
    }

    /// <summary>The Monday of the given ISO week (year, week). Returns the date even for a non-existent 53rd week.</summary>
    public static DateOnly GetIsoWeekMonday(int year, int week)
    {
        var jan4 = new DateOnly(year, 1, 4).ToDateTime(TimeOnly.MinValue);
        var week1Monday = jan4.AddDays(1 - (int)jan4.DayOfWeek);
        return DateOnly.FromDateTime(week1Monday.AddDays(7 * (week - 1)));
    }

    private string ResolveRelative(string root, string relative)
    {
        ValidateRelativePath(relative, nameof(relative));
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideDiaryRoot(path, new AppSettings { DiaryRoot = root })) throw new ArgumentException("路径不能离开笔记库根目录。");
        return path;
    }

    private void EnsureInside(string path, AppSettings settings)
    {
        if (!IsInsideDiaryRoot(path, settings)) throw new ArgumentException("路径不能离开笔记库根目录。");
    }

    private static void ValidateRelativePath(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or ".."))
            throw new ArgumentException("必须是笔记库内的相对路径，且不能包含 . 或 ..。", argumentName);
    }

    private static void ValidateFolderName(string value, string argumentName)
    {
        ValidateFileName(value, argumentName);
        if (value.Contains('/') || value.Contains('\\')) throw new ArgumentException("附件目录只能是单个目录名。", argumentName);
    }

    private static void ValidateFileName(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("包含非法文件名字符。", argumentName);
    }
}
