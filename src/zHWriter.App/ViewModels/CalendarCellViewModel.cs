using zHWriter.Core.Models;

namespace zHWriter.App.ViewModels;

public sealed class CalendarCellViewModel
{
    public CalendarCellViewModel(CalendarDay day) => Day = day;
    public CalendarDay Day { get; }
    public string DayNumber => Day.Date.Day.ToString();
    public string Marker => Day.HasEntry ? "•" : string.Empty;
    public double Opacity => Day.IsInDisplayedMonth ? 1 : .35;
    public string Background => Day.IsSelected ? "#486581" : Day.IsToday ? "#2b4c5f" : "Transparent";
}
