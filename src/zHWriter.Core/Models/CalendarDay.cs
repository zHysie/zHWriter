namespace zHWriter.Core.Models;

/// <summary>A date cell exposed by the calendar view.</summary>
public sealed record CalendarDay(DateOnly Date, bool IsInDisplayedMonth, bool IsToday, bool IsSelected, bool HasEntry);
