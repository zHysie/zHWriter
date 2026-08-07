using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Indexes existing periodic notes without reading their Markdown bodies.</summary>
public interface ICalendarIndexService : IDisposable
{
    event EventHandler? IndexChanged;
    /// <summary>
    /// Returns existing entries for one period kind without reading note bodies.
    /// For Daily the set holds day dates of (year, month); for Weekly it holds the ISO-Monday
    /// dates of existing weeks in <paramref name="year"/> (month ignored); for Monthly it holds
    /// the first-of-month dates of existing months in <paramref name="year"/> (month ignored).
    /// </summary>
    Task<IReadOnlySet<DateOnly>> GetExistingDatesAsync(PeriodicNoteType type, int year, int month, AppSettings settings, CancellationToken cancellationToken = default);
    Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default);
    void StartWatching(AppSettings settings);
}
