using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Indexes journal dates without reading their Markdown bodies.</summary>
public interface ICalendarIndexService : IDisposable
{
    event EventHandler? IndexChanged;
    Task<IReadOnlySet<DateOnly>> GetExistingDatesAsync(int year, int month, AppSettings settings, CancellationToken cancellationToken = default);
    Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default);
    void StartWatching(AppSettings settings);
}
