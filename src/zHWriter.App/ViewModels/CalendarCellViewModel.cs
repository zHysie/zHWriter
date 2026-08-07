using zHWriter.Core.Models;

namespace zHWriter.App.ViewModels;

/// <summary>A single selectable calendar cell for the daily / weekly / monthly picker.</summary>
public sealed class CalendarCellViewModel
{
    public CalendarCellViewModel(string mainLabel, string? subLabel, double opacity, string background, bool hasEntry, DateOnly date)
    {
        MainLabel = mainLabel;
        SubLabel = subLabel ?? string.Empty;
        Opacity = opacity;
        Background = background;
        HasEntry = hasEntry;
        Date = date;
    }

    public string MainLabel { get; }
    public string SubLabel { get; }
    public double Opacity { get; }
    public string Background { get; }
    public bool HasEntry { get; }
    public string Marker => HasEntry ? "•" : string.Empty;
    public DateOnly Date { get; }
}
