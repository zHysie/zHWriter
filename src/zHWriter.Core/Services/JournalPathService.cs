using System.Globalization;
using System.Text.RegularExpressions;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Core.Services;

/// <summary>Safe journal path calculations using the configured .NET date format patterns.</summary>
public sealed class JournalPathService : IJournalPathService
{
    public string GetJournalPath(DateOnly date, AppSettings settings)
    {
        Validate(settings);
        var directory = ResolveRelative(settings.DiaryRoot, FormatPattern(date, settings.JournalDirectoryPattern));
        var name = FormatPattern(date, settings.JournalFileNamePattern);
        ValidateFileName(name, nameof(settings.JournalFileNamePattern));
        var path = Path.Combine(directory, name + ".md");
        EnsureInside(path, settings);
        return path;
    }

    public string GetTemplatePath(AppSettings settings) => ResolveRelative(settings.DiaryRoot, settings.DailyTemplateRelativePath);

    public string GetAttachmentDirectory(DateOnly date, AppSettings settings)
    {
        ValidateFolderName(settings.AttachmentFolderName, nameof(settings.AttachmentFolderName));
        var result = Path.Combine(Path.GetDirectoryName(GetJournalPath(date, settings))!, settings.AttachmentFolderName);
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
        if (string.IsNullOrWhiteSpace(settings.DiaryRoot)) throw new ArgumentException("尚未选择日记库根目录。", nameof(settings));
        _ = Path.GetFullPath(settings.DiaryRoot);
        ValidateRelativePath(settings.JournalDirectoryPattern, nameof(settings.JournalDirectoryPattern));
        ValidateRelativePath(settings.DailyTemplateRelativePath, nameof(settings.DailyTemplateRelativePath));
        ValidateFolderName(settings.AttachmentFolderName, nameof(settings.AttachmentFolderName));
        _ = FormatPattern(DateOnly.FromDateTime(DateTime.Today), settings.JournalDirectoryPattern);
        _ = FormatPattern(DateOnly.FromDateTime(DateTime.Today), settings.JournalFileNamePattern);
    }

    private string ResolveRelative(string root, string relative)
    {
        ValidateRelativePath(relative, nameof(relative));
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideDiaryRoot(path, new AppSettings { DiaryRoot = root })) throw new ArgumentException("路径不能离开日记库根目录。");
        return path;
    }

    private void EnsureInside(string path, AppSettings settings)
    {
        if (!IsInsideDiaryRoot(path, settings)) throw new ArgumentException("路径不能离开日记库根目录。");
    }

    private static void ValidateRelativePath(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or ".."))
            throw new ArgumentException("必须是日记库内的相对路径，且不能包含 . 或 ..。", argumentName);
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

    /// <summary>Expands only standalone date tokens, keeping literal directory names such as Daily intact.</summary>
    public static string FormatPattern(DateOnly date, string pattern)
    {
        var values = new Dictionary<string, string> { ["yyyy"] = date.Year.ToString("D4", CultureInfo.InvariantCulture), ["yy"] = (date.Year % 100).ToString("D2", CultureInfo.InvariantCulture), ["MM"] = date.Month.ToString("D2", CultureInfo.InvariantCulture), ["dd"] = date.Day.ToString("D2", CultureInfo.InvariantCulture) };
        return Regex.Replace(pattern, @"(?<![A-Za-z])(yyyy|yy|MM|dd)(?![A-Za-z])", match => values[match.Value]);
    }
}
