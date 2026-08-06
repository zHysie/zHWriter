using System.Collections.Concurrent;
using System.Globalization;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;

namespace zHWriter.Infrastructure.FileSystem;

/// <summary>Month-scoped filename index with debounced file-system watching.</summary>
public sealed class CalendarIndexService : ICalendarIndexService
{
    private readonly IJournalPathService _paths;
    private readonly ConcurrentDictionary<(int Year, int Month), IReadOnlySet<DateOnly>> _months = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private AppSettings? _settings;
    public event EventHandler? IndexChanged;
    public CalendarIndexService(IJournalPathService paths) => _paths = paths;

    public async Task<IReadOnlySet<DateOnly>> GetExistingDatesAsync(int year, int month, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var key = (year, month);
        if (_months.TryGetValue(key, out var dates)) return dates;
        return await ScanMonthAsync(year, month, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        foreach (var key in _months.Keys) await ScanMonthAsync(key.Year, key.Month, settings, cancellationToken).ConfigureAwait(false);
        IndexChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartWatching(AppSettings settings)
    {
        _settings = settings;
        _watcher?.Dispose();
        var root = Path.Combine(settings.DiaryRoot, "Journal");
        var watchPath = FindExistingDirectory(root, settings.DiaryRoot);
        _watcher = new FileSystemWatcher(watchPath, "*.md") { IncludeSubdirectories = true, EnableRaisingEvents = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += QueueRefresh; _watcher.Deleted += QueueRefresh; _watcher.Renamed += QueueRefresh; _watcher.Changed += QueueRefresh;
    }

    public void Dispose() { _watcher?.Dispose(); _debounce?.Dispose(); }

    private async Task<IReadOnlySet<DateOnly>> ScanMonthAsync(int year, int month, AppSettings settings, CancellationToken cancellationToken)
    {
        var dates = await Task.Run(() =>
        {
            var start = new DateOnly(year, month, 1);
            var directory = Path.GetDirectoryName(_paths.GetJournalPath(start, settings))!;
            if (!Directory.Exists(directory)) return (IReadOnlySet<DateOnly>)new HashSet<DateOnly>();
            var expectedPrefix = start.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-", CultureInfo.InvariantCulture);
            return (IReadOnlySet<DateOnly>)Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not null && name.StartsWith(expectedPrefix, StringComparison.Ordinal))
                .Select(name => DateOnly.TryParseExact(name!, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateOnly?)null)
                .Where(date => date.HasValue).Select(date => date!.Value).ToHashSet();
        }, cancellationToken).ConfigureAwait(false);
        _months[(year, month)] = dates;
        return dates;
    }

    private void QueueRefresh(object? sender, FileSystemEventArgs args)
    {
        _debounce?.Dispose();
        _debounce = new Timer(async _ => { if (_settings is not null) await RefreshAsync(_settings).ConfigureAwait(false); }, null, 350, Timeout.Infinite);
    }

    private static string FindExistingDirectory(string requested, string fallback)
    {
        var current = requested;
        while (!Directory.Exists(current))
        {
            var parent = Directory.GetParent(current);
            if (parent is null) return fallback;
            current = parent.FullName;
        }
        return current;
    }
}
