using System.Collections.Concurrent;
using System.Globalization;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;
using zHWriter.Core.Services;

namespace zHWriter.Infrastructure.FileSystem;

/// <summary>Period-scoped filename index with debounced file-system watching.</summary>
public sealed class CalendarIndexService : ICalendarIndexService
{
    private readonly IJournalPathService _paths;
    private readonly ConcurrentDictionary<(PeriodicNoteType Type, int Year, int Month), IReadOnlySet<DateOnly>> _cache = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private AppSettings? _settings;
    public event EventHandler? IndexChanged;
    public CalendarIndexService(IJournalPathService paths) => _paths = paths;

    public async Task<IReadOnlySet<DateOnly>> GetExistingDatesAsync(PeriodicNoteType type, int year, int month, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var key = (type, year, month);
        if (_cache.TryGetValue(key, out var dates)) return dates;
        return await ScanAsync(type, year, month, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        foreach (var key in _cache.Keys) await ScanAsync(key.Type, key.Year, key.Month, settings, cancellationToken).ConfigureAwait(false);
        IndexChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartWatching(AppSettings settings)
    {
        _settings = settings;
        _watcher?.Dispose();
        var watchPath = FindExistingDirectory(settings.DiaryRoot, settings.DiaryRoot);
        _watcher = new FileSystemWatcher(watchPath, "*.md") { IncludeSubdirectories = true, EnableRaisingEvents = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += QueueRefresh; _watcher.Deleted += QueueRefresh; _watcher.Renamed += QueueRefresh; _watcher.Changed += QueueRefresh;
    }

    public void Dispose() { _watcher?.Dispose(); _debounce?.Dispose(); }

    private async Task<IReadOnlySet<DateOnly>> ScanAsync(PeriodicNoteType type, int year, int month, AppSettings settings, CancellationToken cancellationToken)
    {
        var dates = await Task.Run(() => type switch
        {
            PeriodicNoteType.Daily => ScanDaily(year, month, settings),
            PeriodicNoteType.Weekly => ScanWeekly(year, settings),
            PeriodicNoteType.Monthly => ScanMonthly(year, settings),
            _ => (IReadOnlySet<DateOnly>)new HashSet<DateOnly>()
        }, cancellationToken).ConfigureAwait(false);
        _cache[(type, year, month)] = dates;
        return dates;
    }

    private IReadOnlySet<DateOnly> ScanDaily(int year, int month, AppSettings settings)
    {
        var directory = Path.Combine(settings.DiaryRoot, "Daily", month.ToString("D2", CultureInfo.InvariantCulture));
        if (!Directory.Exists(directory)) return new HashSet<DateOnly>();
        var prefix = $"{year:D4}-{month:D2}-";
        var result = new HashSet<DateOnly>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) result.Add(date);
        }
        return result;
    }

    private IReadOnlySet<DateOnly> ScanWeekly(int year, AppSettings settings)
    {
        var directory = Path.Combine(settings.DiaryRoot, "Weekly");
        if (!Directory.Exists(directory)) return new HashSet<DateOnly>();
        var prefix = $"{year:D4}-";
        var result = new HashSet<DateOnly>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith("W", StringComparison.Ordinal)) continue;
            if (int.TryParse(name.AsSpan(5, name.Length - 6), out var week) && week is >= 1 and <= 53)
                result.Add(JournalPathService.GetIsoWeekMonday(year, week));
        }
        return result;
    }

    private IReadOnlySet<DateOnly> ScanMonthly(int year, AppSettings settings)
    {
        var directory = Path.Combine(settings.DiaryRoot, "Monthly");
        if (!Directory.Exists(directory)) return new HashSet<DateOnly>();
        var prefix = $"{year:D4}-";
        var result = new HashSet<DateOnly>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(name.AsSpan(5), out var month) && month is >= 1 and <= 12) result.Add(new DateOnly(year, month, 1));
        }
        return result;
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
