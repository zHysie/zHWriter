using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using System.Windows.Threading;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;
using zHWriter.Core.Services;
using MediaBrushes = System.Windows.Media.Brushes;

namespace zHWriter.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IJournalPathService _paths;
    private readonly IJournalFileService _files;
    private readonly ICalendarIndexService _calendarIndex;
    private readonly IAttachmentService _attachments;
    private readonly DispatcherTimer _autosaveTimer;
    private AppSettings _settings = new();
    private JournalDocument? _document;
    private string _editorText = string.Empty;
    private string _status = "准备就绪";
    private bool _isExpanded;
    private bool _isCalendarOpen;
    private bool _isDirty;
    private bool _conflictPending;
    private bool _isAltInteractionMode;
    private IReadOnlyList<string> _recoverableTemporaryFiles = Array.Empty<string>();
    private WpfBrush _editorForeground = MediaBrushes.White;
    private WpfBrush _secondaryForeground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(159, 179, 200));
    private PeriodicNoteType _activeType = PeriodicNoteType.Daily;
    private PeriodicNoteType _calendarPeriod = PeriodicNoteType.Daily;
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private int _displayedYear = DateTime.Today.Year;

    public MainViewModel(ISettingsService settingsService, IJournalPathService paths, IJournalFileService files, ICalendarIndexService calendarIndex, IAttachmentService attachments)
    {
        (_settingsService, _paths, _files, _calendarIndex, _attachments) = (settingsService, paths, files, calendarIndex, attachments);
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _autosaveTimer.Tick += async (_, _) => { _autosaveTimer.Stop(); await SaveAsync(); };
        _calendarIndex.IndexChanged += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () => await RebuildCalendarAsync()));
        ToggleExpandedCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        ToggleCalendarCommand = new RelayCommand(_ => { IsCalendarOpen = !IsCalendarOpen; if (IsCalendarOpen) _ = RebuildCalendarAsync(); });
        OpenCalendarCellCommand = new RelayCommand(async item => { if (item is CalendarCellViewModel cell) await OpenPeriodAsync(CalendarPeriod, cell.Date); });
        PreviousPeriodCommand = new RelayCommand(_ => { if (CalendarPeriod == PeriodicNoteType.Daily) DisplayedMonth = DisplayedMonth.AddMonths(-1); else DisplayedYear--; _ = RebuildCalendarAsync(); });
        NextPeriodCommand = new RelayCommand(_ => { if (CalendarPeriod == PeriodicNoteType.Daily) DisplayedMonth = DisplayedMonth.AddMonths(1); else DisplayedYear++; _ = RebuildCalendarAsync(); });
        TodayPeriodCommand = new RelayCommand(async _ => await OpenPeriodAsync(CalendarPeriod, PeriodDateForToday(CalendarPeriod)));
        SaveCommand = new RelayCommand(async _ => await SaveAsync());
    }

    public ObservableCollection<CalendarCellViewModel> CalendarDays { get; } = new();
    public RelayCommand ToggleExpandedCommand { get; }
    public RelayCommand ToggleCalendarCommand { get; }
    public RelayCommand OpenCalendarCellCommand { get; }
    public RelayCommand PreviousPeriodCommand { get; }
    public RelayCommand NextPeriodCommand { get; }
    public RelayCommand TodayPeriodCommand { get; }
    public RelayCommand SaveCommand { get; }
    public event EventHandler? ExternalConflictDetected;
    public event EventHandler<string>? ErrorOccurred;

    public AppSettings Settings => _settings;
    public bool HasDiaryRoot => !string.IsNullOrWhiteSpace(_settings.DiaryRoot);
    public IReadOnlyList<string> RecoverableTemporaryFiles { get => _recoverableTemporaryFiles; private set => SetProperty(ref _recoverableTemporaryFiles, value); }
    public string CurrentFileName => _document is null ? "未打开笔记" : Path.GetFileName(_document.Path);
    public string CurrentJournalDirectory => _document is null ? _settings.DiaryRoot : Path.GetDirectoryName(_document.Path)!;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsCalendarOpen { get => _isCalendarOpen; set => SetProperty(ref _isCalendarOpen, value); }
    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }
    public bool IsAltInteractionMode { get => _isAltInteractionMode; set => SetProperty(ref _isAltInteractionMode, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public WpfBrush EditorForeground { get => _editorForeground; private set => SetProperty(ref _editorForeground, value); }
    public WpfBrush SecondaryForeground { get => _secondaryForeground; private set => SetProperty(ref _secondaryForeground, value); }

    public PeriodicNoteType CalendarPeriod
    {
        get => _calendarPeriod;
        set
        {
            if (!SetProperty(ref _calendarPeriod, value)) return;
            OnPropertyChanged(nameof(CalendarTitle));
            OnPropertyChanged(nameof(DailyBodyVisibility));
            OnPropertyChanged(nameof(WeeklyBodyVisibility));
            OnPropertyChanged(nameof(MonthlyBodyVisibility));
            OnPropertyChanged(nameof(DayHeaderVisibility));
        }
    }

    public DateOnly DisplayedMonth { get => _displayedMonth; set { if (SetProperty(ref _displayedMonth, value)) OnPropertyChanged(nameof(CalendarTitle)); } }
    public int DisplayedYear { get => _displayedYear; set { if (SetProperty(ref _displayedYear, value)) OnPropertyChanged(nameof(CalendarTitle)); } }
    public string CalendarTitle => CalendarPeriod switch
    {
        PeriodicNoteType.Daily => $"{DisplayedMonth.Year} 年 {DisplayedMonth.Month} 月",
        _ => $"{DisplayedYear} 年"
    };
    public Visibility DailyBodyVisibility => CalendarPeriod == PeriodicNoteType.Daily ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WeeklyBodyVisibility => CalendarPeriod == PeriodicNoteType.Weekly ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MonthlyBodyVisibility => CalendarPeriod == PeriodicNoteType.Monthly ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DayHeaderVisibility => CalendarPeriod == PeriodicNoteType.Daily ? Visibility.Visible : Visibility.Collapsed;

    public string EditorText
    {
        get => _editorText;
        set { if (SetProperty(ref _editorText, value)) { IsDirty = true; _autosaveTimer.Stop(); _autosaveTimer.Start(); } }
    }

    public async Task<bool> InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(HasDiaryRoot));
        if (!HasDiaryRoot) return false;
        try
        {
            _paths.Validate(_settings); await EnsureReadyAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            await OpenPeriodAsync(PeriodicNoteType.Daily, _settings.LastOpenedDate is { } date ? DateOnly.FromDateTime(date) : today);
            return true;
        }
        catch (Exception exception) { ErrorOccurred?.Invoke(this, exception.Message); return false; }
    }

    public async Task ConfigureDiaryRootAsync(string root)
    {
        _settings.DiaryRoot = root;
        _paths.Validate(_settings);

        Directory.CreateDirectory(root);
        await EnsureReadyAsync();

        _settings.WindowLeft = double.IsFinite(_settings.WindowLeft) ? _settings.WindowLeft : 100;
        _settings.WindowTop = double.IsFinite(_settings.WindowTop) ? _settings.WindowTop : 100;
        _settings.ExpandedWidth = double.IsFinite(_settings.ExpandedWidth) ? _settings.ExpandedWidth : 520;
        _settings.ExpandedHeight = double.IsFinite(_settings.ExpandedHeight) ? _settings.ExpandedHeight : 300;
        _settings.TextOpacity = double.IsFinite(_settings.TextOpacity) ? _settings.TextOpacity : 1;

        await _settingsService.SaveAsync(_settings);

        OnPropertyChanged(nameof(HasDiaryRoot));
        await OpenPeriodAsync(PeriodicNoteType.Daily, DateOnly.FromDateTime(DateTime.Today));
    }

    public async Task ApplySettingsAsync(AppSettings updated)
    {
        _paths.Validate(updated); Directory.CreateDirectory(updated.DiaryRoot);
        _settings = updated; await _settingsService.SaveAsync(_settings); await EnsureReadyAsync(); await OpenPeriodAsync(_activeType, _selectedDate);
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(HasDiaryRoot));
    }

    public Task OpenTodayAsync() => OpenPeriodAsync(PeriodicNoteType.Daily, DateOnly.FromDateTime(DateTime.Today));

    public Task OpenThisWeekAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return OpenPeriodAsync(PeriodicNoteType.Weekly, JournalPathService.GetIsoWeekMonday(today));
    }

    public Task OpenThisMonthAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return OpenPeriodAsync(PeriodicNoteType.Monthly, new DateOnly(today.Year, today.Month, 1));
    }

    public async Task OpenPeriodAsync(PeriodicNoteType type, DateOnly date)
    {
        if (!HasDiaryRoot) { ErrorOccurred?.Invoke(this, "请先选择笔记库根目录。"); return; }
        if (!await SaveAsync()) return;
        try
        {
            _document = await _files.OpenOrCreateAsync(type, date, _settings);
            _activeType = type; _selectedDate = date;
            switch (type)
            {
                case PeriodicNoteType.Daily: DisplayedMonth = new DateOnly(date.Year, date.Month, 1); break;
                case PeriodicNoteType.Weekly: DisplayedYear = JournalPathService.GetIsoWeekYear(date); break;
                case PeriodicNoteType.Monthly: DisplayedYear = date.Year; break;
            }
            CalendarPeriod = type;
            _editorText = _document.Content;
            OnPropertyChanged(nameof(EditorText)); OnPropertyChanged(nameof(CurrentFileName)); OnPropertyChanged(nameof(CurrentJournalDirectory));
            IsDirty = false;
            _settings.LastOpenedDate = date.ToDateTime(TimeOnly.MinValue);
            await _settingsService.SaveAsync(_settings);
            Status = _document.WasCreated ? "已创建新笔记。" : "已打开笔记。";
            IsExpanded = true; IsCalendarOpen = false;
            await RebuildCalendarAsync();
        }
        catch (Exception exception) { ErrorOccurred?.Invoke(this, exception.Message); }
    }

    public async Task<bool> SaveAsync()
    {
        if (_document is null || !IsDirty) return true;
        if (!_conflictPending && File.Exists(_document.Path) && File.GetLastWriteTimeUtc(_document.Path) > _document.LastWriteTimeUtc)
        {
            _conflictPending = true;
            ExternalConflictDetected?.Invoke(this, EventArgs.Empty);
            return false;
        }
        var result = await _files.SaveAsync(_document, EditorText);
        if (!result.Succeeded) { ErrorOccurred?.Invoke(this, result.ErrorMessage ?? "保存失败。"); return false; }
        _document = _document with { Content = EditorText, LastWriteTimeUtc = File.GetLastWriteTimeUtc(_document.Path) };
        IsDirty = false; return true;
    }

    public async Task KeepMyContentAfterConflictAsync()
    {
        if (_document is null) return;
        var disk = await _files.LoadAsync(_activeType, _selectedDate, _settings);
        _document = disk with { Content = EditorText };
        _conflictPending = false;
        await SaveAsync();
    }

    public async Task ReloadDiskContentAfterConflictAsync()
    {
        if (_document is null) return;
        var disk = await _files.LoadAsync(_activeType, _selectedDate, _settings);
        _document = disk; _editorText = disk.Content; OnPropertyChanged(nameof(EditorText)); IsDirty = false; _conflictPending = false;
    }

    public async Task SaveCopyAfterConflictAsync(string destination)
    {
        try
        {
            await File.WriteAllTextAsync(destination, EditorText); _conflictPending = false; Status = $"已另存为副本：{destination}";
        }
        catch (Exception exception) { ErrorOccurred?.Invoke(this, $"无法另存副本：{destination}。正文仍安全：{exception.Message}"); }
    }

    public async Task RestoreRecoverableFilesAsync()
    {
        foreach (var path in RecoverableTemporaryFiles) await _files.RestoreTemporaryFileAsync(path);
        RecoverableTemporaryFiles = Array.Empty<string>();
        Status = "已恢复崩溃前未完成的保存。";
        if (_document is not null) await OpenPeriodAsync(_activeType, _selectedDate);
    }

    public async Task<bool> CollapseAsync()
    {
        if (!await SaveAsync()) return false;
        IsExpanded = false; return true;
    }

    public void InsertText(int caretIndex, string text)
    {
        var index = Math.Clamp(caretIndex, 0, EditorText.Length);
        EditorText = EditorText.Insert(index, text);
    }

    public async Task InsertImageFilesAsync(IEnumerable<string> files, int caretIndex)
    {
        if (_document is null) { ErrorOccurred?.Invoke(this, "请先打开一篇笔记后再粘贴图片。"); return; }
        try
        {
            var attachments = await _attachments.CopyImageFilesAsync(files, _activeType, _selectedDate, _settings);
            var markdown = string.Join(Environment.NewLine, attachments.Select(path => _attachments.BuildMarkdownReference(path, _activeType, _selectedDate, _settings)));
            if (markdown.Length > 0) InsertText(caretIndex, markdown + Environment.NewLine);
            else Status = "剪贴板中没有受支持的本地图片文件。";
        }
        catch (Exception exception) { ErrorOccurred?.Invoke(this, $"无法保存图片附件。正文仍安全：{exception.Message}"); }
    }

    public async Task InsertClipboardPngAsync(byte[] png, int caretIndex)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"zHWriter-{Guid.NewGuid():N}.png");
        try { await File.WriteAllBytesAsync(temporary, png); await InsertImageFilesAsync(new[] { temporary }, caretIndex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void UpdateWindowMetrics(double left, double top, double width, double height, double opacity)
    {
        if (double.IsFinite(left)) _settings.WindowLeft = left;
        if (double.IsFinite(top)) _settings.WindowTop = top;
        if (double.IsFinite(width)) _settings.ExpandedWidth = Math.Max(300, width);
        if (double.IsFinite(height)) _settings.ExpandedHeight = Math.Max(96, height);
        if (double.IsFinite(opacity)) _settings.TextOpacity = Math.Clamp(opacity, 0.1, 1);

        _ = _settingsService.SaveAsync(_settings);
        OnPropertyChanged(nameof(Settings));
    }

    public void SetBackgroundBrightness(bool isLight)
    {
        EditorForeground = isLight ? MediaBrushes.Black : MediaBrushes.White;
        SecondaryForeground = isLight ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 65, 80)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(159, 179, 200));
    }

    public async Task RebuildCalendarAsync()
    {
        if (!HasDiaryRoot) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        CalendarDays.Clear();
        switch (CalendarPeriod)
        {
            case PeriodicNoteType.Daily:
            {
                var entries = await _calendarIndex.GetExistingDatesAsync(PeriodicNoteType.Daily, DisplayedMonth.Year, DisplayedMonth.Month, _settings);
                var first = DisplayedMonth;
                var offset = ((int)first.DayOfWeek - (_settings.WeekStartsOnMonday ? 1 : 0) + 7) % 7;
                var start = first.AddDays(-offset);
                for (var index = 0; index < 42; index++)
                {
                    var day = start.AddDays(index);
                    var inMonth = day.Month == DisplayedMonth.Month;
                    CalendarDays.Add(new CalendarCellViewModel(
                        day.Day.ToString(),
                        null,
                        inMonth ? 1 : 0.35,
                        BackgroundFor(day == today, IsSelectedFor(day)),
                        entries.Contains(day),
                        day));
                }
                break;
            }
            case PeriodicNoteType.Weekly:
            {
                var entries = await _calendarIndex.GetExistingDatesAsync(PeriodicNoteType.Weekly, DisplayedYear, 0, _settings);
                var mondayOfWeek1 = JournalPathService.GetIsoWeekMonday(new DateOnly(DisplayedYear, 1, 4));
                for (var index = 0; index < 53; index++)
                {
                    var monday = mondayOfWeek1.AddDays(7 * index);
                    var weekYear = JournalPathService.GetIsoWeekYear(monday);
                    if (weekYear < DisplayedYear) continue;
                    if (weekYear > DisplayedYear) break;
                    var weekNumber = JournalPathService.GetIsoWeekNumber(monday);
                    var isThisWeek = today >= monday && today <= monday.AddDays(6);
                    CalendarDays.Add(new CalendarCellViewModel(
                        $"第 {weekNumber} 周",
                        $"{monday:MM-dd} ~ {monday.AddDays(6):MM-dd}",
                        1,
                        BackgroundFor(isThisWeek, IsSelectedFor(monday)),
                        entries.Contains(monday),
                        monday));
                }
                break;
            }
            case PeriodicNoteType.Monthly:
            {
                var entries = await _calendarIndex.GetExistingDatesAsync(PeriodicNoteType.Monthly, DisplayedYear, 0, _settings);
                for (var month = 1; month <= 12; month++)
                {
                    var first = new DateOnly(DisplayedYear, month, 1);
                    var isThisMonth = first.Year == today.Year && first.Month == today.Month;
                    CalendarDays.Add(new CalendarCellViewModel(
                        $"{month} 月",
                        null,
                        1,
                        BackgroundFor(isThisMonth, IsSelectedFor(first)),
                        entries.Contains(first),
                        first));
                }
                break;
            }
        }
    }

    private bool IsSelectedFor(DateOnly date) => _activeType == _calendarPeriod && date == _selectedDate;

    private static string BackgroundFor(bool isToday, bool isSelected)
        => isSelected ? "#486581" : isToday ? "#2b4c5f" : "Transparent";

    private static DateOnly PeriodDateForToday(PeriodicNoteType type)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return type switch
        {
            PeriodicNoteType.Weekly => JournalPathService.GetIsoWeekMonday(today),
            PeriodicNoteType.Monthly => new DateOnly(today.Year, today.Month, 1),
            _ => today
        };
    }

    private async Task EnsureReadyAsync()
    {
        var template = new TemplateService(_paths); await template.EnsureDefaultTemplatesAsync(_settings);
        _calendarIndex.StartWatching(_settings); await _calendarIndex.RefreshAsync(_settings);
        RecoverableTemporaryFiles = await _files.FindRecoverableTemporaryFilesAsync(_settings);
        if (RecoverableTemporaryFiles.Count > 0) Status = $"发现 {RecoverableTemporaryFiles.Count} 个可恢复的未完成保存文件。";
    }

    public void Dispose() { _autosaveTimer.Stop(); _calendarIndex.Dispose(); }
}
