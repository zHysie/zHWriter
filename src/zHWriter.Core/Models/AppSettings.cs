namespace zHWriter.Core.Models;

/// <summary>Persisted application preferences. Diary-relative paths are never stored as absolute paths.</summary>
public sealed class AppSettings
{
    public string DiaryRoot { get; set; } = string.Empty;
    public string JournalDirectoryPattern { get; set; } = "Journal/yyyy/Daily/MM";
    public string JournalFileNamePattern { get; set; } = "yyyy-MM-dd";
    public string DailyTemplateRelativePath { get; set; } = "Templates/Daily.md";
    public string AttachmentFolderName { get; set; } = "assets";
    public DateTime? LastOpenedDate { get; set; }
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;
    public double ExpandedWidth { get; set; } = 520;
    public double ExpandedHeight { get; set; } = 160;
    public double TextOpacity { get; set; } = 1;
    public bool AlwaysOnTop { get; set; } = true;
    public int CollapseDelayMs { get; set; } = 450;
    public int ExpandDelayMs { get; set; } = 100;
    public bool ShowExistingEntryMarks { get; set; } = true;
    public bool WeekStartsOnMonday { get; set; } = true;
    public bool LaunchAtStartup { get; set; }
}
