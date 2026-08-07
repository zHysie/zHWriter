using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using System.Windows.Threading;
using zHWriter.Core.Interfaces;
using zHWriter.Core.Models;
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
    private IReadOnlyList<string> _recoverableTemporaryFiles = Array.Empty<string>();
    private WpfBrush _editorForeground = MediaBrushes.White;
    private WpfBrush _secondaryForeground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(159, 179, 200));
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public MainViewModel(ISettingsService settingsService, IJournalPathService paths, IJournalFileService files, ICalendarIndexService calendarIndex, IAttachmentService attachments)
    {
        (_settingsService, _paths, _files, _calendarIndex, _attachments) = (settingsService, paths, files, calendarIndex, attachments);
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _autosaveTimer.Tick += async (_, _) => { _autosaveTimer.Stop(); await SaveAsync(); };
        _calendarIndex.IndexChanged += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () => await RebuildCalendarAsync()));
        ToggleExpandedCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        ToggleCalendarCommand = new RelayCommand(_ => { IsCalendarOpen = !IsCalendarOpen; if (IsCalendarOpen) _ = RebuildCalendarAsync(); });
        OpenCalendarDateCommand = new RelayCommand(async item => { if (item is CalendarCellViewModel cell) await OpenDateAsync(cell.Day.Date); });
        PreviousMonthCommand = new RelayCommand(_ => { DisplayedMonth = DisplayedMonth.AddMonths(-1); _ = RebuildCalendarAsync(); });
        NextMonthCommand = new RelayCommand(_ => { DisplayedMonth = DisplayedMonth.AddMonths(1); _ = RebuildCalendarAsync(); });
        TodayCommand = new RelayCommand(async _ => { DisplayedMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1); await OpenDateAsync(DateOnly.FromDateTime(DateTime.Today)); });
        SaveCommand = new RelayCommand(async _ => await SaveAsync());
    }

    public ObservableCollection<CalendarCellViewModel> CalendarDays { get; } = new();
    public RelayCommand ToggleExpandedCommand { get; }
    public RelayCommand ToggleCalendarCommand { get; }
    public RelayCommand OpenCalendarDateCommand { get; }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }
    public RelayCommand SaveCommand { get; }
    public event EventHandler? ExternalConflictDetected;
    public AppSettings Settings => _settings;
    public bool HasDiaryRoot => !string.IsNullOrWhiteSpace(_settings.DiaryRoot);
    public IReadOnlyList<string> RecoverableTemporaryFiles { get => _recoverableTemporaryFiles; private set => SetProperty(ref _recoverableTemporaryFiles, value); }
    public string CurrentFileName => _document is null ? "未打开日记" : Path.GetFileName(_document.Path);
    public string CurrentJournalDirectory => _document is null ? Path.Combine(_settings.DiaryRoot, "Journal") : Path.GetDirectoryName(_document.Path)!;
    public string CalendarTitle => $"{DisplayedMonth.Year} 年 {DisplayedMonth.Month} 月";
    public DateOnly DisplayedMonth { get => _displayedMonth; private set { if (SetProperty(ref _displayedMonth, value)) OnPropertyChanged(nameof(CalendarTitle)); } }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsCalendarOpen { get => _isCalendarOpen; set => SetProperty(ref _isCalendarOpen, value); }
    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public WpfBrush EditorForeground { get => _editorForeground; private set => SetProperty(ref _editorForeground, value); }
    public WpfBrush SecondaryForeground { get => _secondaryForeground; private set => SetProperty(ref _secondaryForeground, value); }
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
            await OpenDateAsync(_settings.LastOpenedDate is { } date ? DateOnly.FromDateTime(date) : DateOnly.FromDateTime(DateTime.Today));
            return true;
        }
        catch (Exception exception) { Status = exception.Message; return false; }
    }

    public async Task ConfigureDiaryRootAsync(string root)
{
    _settings.DiaryRoot = root;
    _paths.Validate(_settings);

    Directory.CreateDirectory(root);
    await EnsureReadyAsync();

    _settings.WindowLeft =
        double.IsFinite(_settings.WindowLeft) ? _settings.WindowLeft : 100;

    _settings.WindowTop =
        double.IsFinite(_settings.WindowTop) ? _settings.WindowTop : 100;

    _settings.ExpandedWidth =
        double.IsFinite(_settings.ExpandedWidth) ? _settings.ExpandedWidth : 520;

    _settings.ExpandedHeight =
        double.IsFinite(_settings.ExpandedHeight) ? _settings.ExpandedHeight : 300;

    _settings.TextOpacity =
        double.IsFinite(_settings.TextOpacity) ? _settings.TextOpacity : 1;

    await _settingsService.SaveAsync(_settings);

    OnPropertyChanged(nameof(HasDiaryRoot));
    await OpenDateAsync(DateOnly.FromDateTime(DateTime.Today));
}

    public async Task ApplySettingsAsync(AppSettings updated)
    {
        _paths.Validate(updated); Directory.CreateDirectory(updated.DiaryRoot);
        _settings = updated; await _settingsService.SaveAsync(_settings); await EnsureReadyAsync(); await OpenDateAsync(_selectedDate);
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(HasDiaryRoot));
    }

    public async Task OpenTodayAsync() => await OpenDateAsync(DateOnly.FromDateTime(DateTime.Today));
    public async Task OpenDateAsync(DateOnly date)
    {
        if (!HasDiaryRoot) { Status = "请先选择日记库根目录。"; return; }
        if (!await SaveAsync()) return;
        try
        {
            _document = await _files.OpenOrCreateAsync(date, _settings); _selectedDate = date; DisplayedMonth = new DateOnly(date.Year, date.Month, 1);
            _editorText = _document.Content; OnPropertyChanged(nameof(EditorText)); OnPropertyChanged(nameof(CurrentFileName)); OnPropertyChanged(nameof(CurrentJournalDirectory)); IsDirty = false;
            _settings.LastOpenedDate = date.ToDateTime(TimeOnly.MinValue); await _settingsService.SaveAsync(_settings);
            Status = _document.WasCreated ? "已按模板创建日记。" : "已打开日记。"; IsExpanded = true; IsCalendarOpen = false;
            await RebuildCalendarAsync();
        }
        catch (Exception exception) { Status = exception.Message; }
    }

    public async Task<bool> SaveAsync()
    {
        if (_document is null || !IsDirty) return true;
        if (!_conflictPending && File.Exists(_document.Path) && File.GetLastWriteTimeUtc(_document.Path) > _document.LastWriteTimeUtc)
        {
            _conflictPending = true;
            Status = $"检测到磁盘版本更新（{File.GetLastWriteTime(_document.Path):G}）；请选择保留我的内容、重新加载或另存副本。";
            ExternalConflictDetected?.Invoke(this, EventArgs.Empty);
            return false;
        }
        var result = await _files.SaveAsync(_document, EditorText);
        if (!result.Succeeded) { Status = result.ErrorMessage!; return false; }
        _document = _document with { Content = EditorText, LastWriteTimeUtc = File.GetLastWriteTimeUtc(_document.Path) }; IsDirty = false; Status = "已保存"; return true;
    }

    public async Task KeepMyContentAfterConflictAsync()
    {
        if (_document is null) return;
        var disk = await _files.LoadAsync(_selectedDate, _settings);
        _document = disk with { Content = EditorText };
        _conflictPending = false;
        await SaveAsync();
    }

    public async Task ReloadDiskContentAfterConflictAsync()
    {
        if (_document is null) return;
        var disk = await _files.LoadAsync(_selectedDate, _settings);
        _document = disk; _editorText = disk.Content; OnPropertyChanged(nameof(EditorText)); IsDirty = false; _conflictPending = false; Status = "已重新加载磁盘内容。";
    }

    public async Task SaveCopyAfterConflictAsync(string destination)
    {
        try
        {
            await File.WriteAllTextAsync(destination, EditorText); _conflictPending = false; Status = $"已另存为副本：{destination}";
        }
        catch (Exception exception) { Status = $"无法另存副本：{destination}。正文仍安全：{exception.Message}"; }
    }

    public async Task RestoreRecoverableFilesAsync()
    {
        foreach (var path in RecoverableTemporaryFiles) await _files.RestoreTemporaryFileAsync(path);
        RecoverableTemporaryFiles = Array.Empty<string>();
        Status = "已恢复崩溃前未完成的保存。";
        if (_document is not null) await OpenDateAsync(_selectedDate);
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
        if (_document is null) { Status = "请先打开一篇日记后再粘贴图片。"; return; }
        try
        {
            var attachments = await _attachments.CopyImageFilesAsync(files, _selectedDate, _settings);
            var markdown = string.Join(Environment.NewLine, attachments.Select(path => _attachments.BuildMarkdownReference(path, _selectedDate, _settings)));
            if (markdown.Length > 0) InsertText(caretIndex, markdown + Environment.NewLine);
            else Status = "剪贴板中没有受支持的本地图片文件。";
        }
        catch (Exception exception) { Status = $"无法保存图片附件。正文仍安全：{exception.Message}"; }
    }

    public async Task InsertClipboardPngAsync(byte[] png, int caretIndex)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"zHWriter-{Guid.NewGuid():N}.png");
        try { await File.WriteAllBytesAsync(temporary, png); await InsertImageFilesAsync(new[] { temporary }, caretIndex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

   public void UpdateWindowMetrics(
    double left,
    double top,
    double width,
    double height,
    double opacity)
{
    if (double.IsFinite(left))
        _settings.WindowLeft = left;

    if (double.IsFinite(top))
        _settings.WindowTop = top;

    if (double.IsFinite(width))
        _settings.ExpandedWidth = Math.Max(300, width);

    if (double.IsFinite(height))
        _settings.ExpandedHeight = Math.Max(96, height);

    if (double.IsFinite(opacity))
        _settings.TextOpacity = Math.Clamp(opacity, 0.1, 1);

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
        var entries = await _calendarIndex.GetExistingDatesAsync(DisplayedMonth.Year, DisplayedMonth.Month, _settings);
        var first = DisplayedMonth;
        var offset = ((int)first.DayOfWeek - (_settings.WeekStartsOnMonday ? 1 : 0) + 7) % 7;
        var start = first.AddDays(-offset); var today = DateOnly.FromDateTime(DateTime.Today);
        CalendarDays.Clear();
        for (var index = 0; index < 42; index++)
        {
            var day = start.AddDays(index);
            CalendarDays.Add(new CalendarCellViewModel(new CalendarDay(day, day.Month == DisplayedMonth.Month, day == today, day == _selectedDate, entries.Contains(day))));
        }
    }

    private async Task EnsureReadyAsync()
    {
        var template = new zHWriter.Core.Services.TemplateService(_paths); await template.EnsureDefaultTemplateAsync(_settings);
        _calendarIndex.StartWatching(_settings); await _calendarIndex.RefreshAsync(_settings);
        RecoverableTemporaryFiles = await _files.FindRecoverableTemporaryFilesAsync(_settings);
        if (RecoverableTemporaryFiles.Count > 0) Status = $"发现 {RecoverableTemporaryFiles.Count} 个可恢复的未完成保存文件。";
    }

    public void Dispose() { _autosaveTimer.Stop(); _calendarIndex.Dispose(); }
}
