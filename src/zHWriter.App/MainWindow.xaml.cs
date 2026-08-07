using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using zHWriter.App.ViewModels;
using zHWriter.App.Windows;
using zHWriter.Core.Models;

namespace zHWriter.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _collapseTimer = new();
    private readonly DispatcherTimer _colorTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(850) };
    private readonly DispatcherTimer _tabClickTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly DoubleAnimation _overlayFade = new() { To = 0, Duration = TimeSpan.FromMilliseconds(280) };
    private bool _resizing;
    private bool _isContextMenuOpen;
    private WpfPoint _resizeStart;
    private WpfSize _resizeSize;
    private PeriodicNoteType? _pendingTabSwitch;
    private static readonly SolidColorBrush _hitTestBackdrop = new(MediaColor.FromArgb(1, 0, 0, 0));
    public MainViewModel ViewModel { get; }
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); ViewModel = viewModel; DataContext = viewModel;
        _collapseTimer.Tick += async (_, _) => { _collapseTimer.Stop(); if (!IsMouseOver && !ViewModel.IsCalendarOpen && !_isContextMenuOpen) await CollapseAsync(); };
        ContextMenu.Opened += (_, _) => { _isContextMenuOpen = true; _collapseTimer.Stop(); };
        ContextMenu.Closed += (_, _) => { _isContextMenuOpen = false; if (ViewModel.IsExpanded && !ViewModel.IsCalendarOpen && !IsMouseOver) { _collapseTimer.Interval = TimeSpan.FromMilliseconds(ViewModel.Settings.CollapseDelayMs); _collapseTimer.Start(); } };
        _colorTimer.Tick += (_, _) => RefreshForegroundColor();
        _overlayTimer.Tick += OverlayTimer_Tick;
        _tabClickTimer.Tick += TabClickTimer_Tick;
        ViewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ViewModel.IsExpanded)) UpdateVisualState(); };
        ViewModel.ExternalConflictDetected += async (_, _) => await HandleExternalConflictAsync();
        ViewModel.ErrorOccurred += (_, message) => System.Windows.MessageBox.Show(message, "zHWriter", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var initialized = await ViewModel.InitializeAsync(); if (!initialized) await ChooseDiaryRootAsync();
        if (ViewModel.RecoverableTemporaryFiles.Count > 0 && System.Windows.MessageBox.Show($"检测到 {ViewModel.RecoverableTemporaryFiles.Count} 个比正式笔记更新的临时保存文件。是否恢复？\n恢复前会保留原文件备份。", "zHWriter：恢复未完成保存", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) await ViewModel.RestoreRecoverableFilesAsync();
        RestoreWindowPlacement(); ViewModel.IsExpanded = false; UpdateVisualState(); _colorTimer.Start();
    }
    private void RestoreWindowPlacement()
    {
        if (!double.IsNaN(ViewModel.Settings.WindowLeft) && SystemParameters.WorkArea.Contains(new WpfPoint(ViewModel.Settings.WindowLeft, ViewModel.Settings.WindowTop))) { Left = ViewModel.Settings.WindowLeft; Top = ViewModel.Settings.WindowTop; }
        else { Left = SystemParameters.WorkArea.Right - 70; Top = SystemParameters.WorkArea.Top + 32; }
        Width = ViewModel.Settings.ExpandedWidth; Height = ViewModel.Settings.ExpandedHeight; Topmost = ViewModel.Settings.AlwaysOnTop;
    }
    private void UpdateVisualState()
    {
        ExpandedPanel.Visibility = ViewModel.IsExpanded ? Visibility.Visible : Visibility.Collapsed; CollapsedPanel.Visibility = ViewModel.IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        if (ViewModel.IsExpanded) { Width = ViewModel.Settings.ExpandedWidth; Height = ViewModel.Settings.ExpandedHeight; Background = _hitTestBackdrop; Editor.Focus(); }
        else { Width = 52; Height = 28; Background = MediaBrushes.Transparent; /* 折叠时仅 zH 热点可交互，其余区域鼠标穿透，不会误触展开 */ }
    }
    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { _collapseTimer.Stop(); if (!ViewModel.IsExpanded) _ = ExpandDelayedAsync(); }
    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { if (ViewModel.IsExpanded && !ViewModel.IsCalendarOpen && !_isContextMenuOpen) { _collapseTimer.Interval = TimeSpan.FromMilliseconds(ViewModel.Settings.CollapseDelayMs); _collapseTimer.Start(); } }
    private async Task ExpandDelayedAsync() { await Task.Delay(ViewModel.Settings.ExpandDelayMs); if (IsMouseOver) ViewModel.IsExpanded = true; }
    private async Task CollapseAsync() { await ViewModel.CollapseAsync(); SaveMetrics(); }
    private void Collapsed_Click(object sender, MouseButtonEventArgs e) => ViewModel.IsExpanded = true;
    private async void Editor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V) e.Handled = await PasteAsync(); }
    private async Task<bool> PasteAsync()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsFileDropList()) { await ViewModel.InsertImageFilesAsync(System.Windows.Clipboard.GetFileDropList().Cast<string>(), Editor.CaretIndex); return true; }
            if (!System.Windows.Clipboard.ContainsImage()) return false;
            var image = System.Windows.Clipboard.GetImage(); if (image is null) return false;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); await using var stream = new MemoryStream(); encoder.Save(stream);
            await ViewModel.InsertClipboardPngAsync(stream.ToArray(), Editor.CaretIndex); return true;
        }
        catch (Exception exception) { System.Windows.MessageBox.Show($"无法读取或保存剪贴板图片。正文未受影响。\n{exception.Message}", "zHWriter", MessageBoxButton.OK, MessageBoxImage.Warning); return true; }
    }
    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsAltKey(e)) UpdateAltMode(true);
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { await ViewModel.SaveAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { await OpenCalendarAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape) { await CollapseAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Q) { await SaveAndExitAsync(); e.Handled = true; }
    }
    private void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e) { if (IsAltKey(e)) UpdateAltMode(false); }
    private void Window_Deactivated(object sender, EventArgs e) => UpdateAltMode(false);
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        UpdateAltMode(true);
        if (e.ChangedButton == MouseButton.Left) { DragMove(); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.Right) { _resizing = true; _resizeStart = e.GetPosition(this); _resizeSize = new WpfSize(Width, Height); CaptureMouse(); e.Handled = true; }
    }
    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_resizing) return;
        var current = e.GetPosition(this); Width = Math.Max(MinWidth, _resizeSize.Width + current.X - _resizeStart.X); Height = Math.Max(MinHeight, _resizeSize.Height + current.Y - _resizeStart.Y);
        ShowTransientOverlay($"{Width:0} × {Height:0}");
        e.Handled = true;
    }
    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing || e.ChangedButton != MouseButton.Right) return;
        _resizing = false; ReleaseMouseCapture(); SaveMetrics();
        if (Keyboard.Modifiers != ModifierKeys.Alt) UpdateAltMode(false);
        e.Handled = true;
    }
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        UpdateAltMode(true);
        var step = e.Delta > 0 ? 0.1 : -0.1;
        var next = Math.Clamp(Math.Round(ViewModel.Settings.TextOpacity * 10) / 10 + step, 0.1, 1);
        ViewModel.UpdateWindowMetrics(Left, Top, Width, Height, next);
        ShowTransientOverlay($"透明度 {next * 100:0}%");
        e.Handled = true;
    }
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) { _colorTimer.Stop(); SaveMetrics(); }
    private void SaveMetrics() => ViewModel.UpdateWindowMetrics(Left, Top, Width, Height, ViewModel.Settings.TextOpacity);
    private void OpenCalendar_Click(object sender, RoutedEventArgs e) => _ = OpenCalendarAsync();
    private async Task OpenCalendarAsync() { ViewModel.IsExpanded = true; ViewModel.IsCalendarOpen = true; await ViewModel.RebuildCalendarAsync(); }
    private async void OpenToday_Click(object sender, RoutedEventArgs e) => await ViewModel.OpenTodayAsync();
    private async void OpenThisWeek_Click(object sender, RoutedEventArgs e) => await ViewModel.OpenThisWeekAsync();
    private async void OpenThisMonth_Click(object sender, RoutedEventArgs e) => await ViewModel.OpenThisMonthAsync();
    private async void Save_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();
    private void OpenRootFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.Settings.DiaryRoot);
    private void OpenCurrentFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.CurrentJournalDirectory);
    private void OpenTemplatesFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(ViewModel.Settings.DiaryRoot, ViewModel.Settings.TemplatesDirectory));
    private async void Exit_Click(object sender, RoutedEventArgs e) => await SaveAndExitAsync();
    public async Task SaveAndExitAsync() { if (await ViewModel.SaveAsync()) Close(); }
    public void ToggleVisibility() { if (IsVisible) Hide(); else { Show(); Activate(); } }
    public void OpenDiaryFolder() => OpenFolder(ViewModel.Settings.DiaryRoot);
    private static void OpenFolder(string path) { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(ViewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true) await ViewModel.ApplySettingsAsync(dialog.Settings);
    }
    private async Task ChooseDiaryRootAsync()
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "请选择 zHWriter 笔记库根目录", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) await ViewModel.ConfigureDiaryRootAsync(dialog.SelectedPath);
    }

    private async Task HandleExternalConflictAsync()
    {
        var choice = System.Windows.MessageBox.Show("当前笔记已被外部程序修改。\n“是”：保留我的内容并覆盖磁盘版本；\n“否”：重新加载磁盘内容；\n“取消”：选择位置另存为副本。", "zHWriter：外部修改冲突", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Yes) await ViewModel.KeepMyContentAfterConflictAsync();
        else if (choice == MessageBoxResult.No) await ViewModel.ReloadDiskContentAfterConflictAsync();
        else
        {
            var dialog = new Forms.SaveFileDialog { Filter = "Markdown (*.md)|*.md", FileName = $"{DateTime.Today:yyyy-MM-dd}-副本.md" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) await ViewModel.SaveCopyAfterConflictAsync(dialog.FileName);
        }
    }

    private void RefreshForegroundColor()
    {
        if (!IsVisible || !ViewModel.IsExpanded) return;
        var point = PointToScreen(new WpfPoint(Math.Max(12, Width * .5), Math.Max(36, Height * .5)));
        if (ScreenColorSampler.TryGetScreenColor((int)Math.Round(point.X), (int)Math.Round(point.Y), out var color)) ViewModel.SetBackgroundBrightness(ScreenColorSampler.IsLight(color));
    }

    // Alt interaction mode: shows the temporary window outline while Alt is held.
    private static bool IsAltKey(System.Windows.Input.KeyEventArgs e) => e.Key is Key.LeftAlt or Key.RightAlt || e.SystemKey is Key.LeftAlt or Key.RightAlt;
    private void UpdateAltMode(bool on)
    {
        if (ViewModel.IsAltInteractionMode != on) ViewModel.IsAltInteractionMode = on;
        AltBorder.BorderThickness = on ? new Thickness(1) : new Thickness(0);
        AltBorder.BorderBrush = on ? new SolidColorBrush(MediaColor.FromArgb(110, 176, 192, 216)) : MediaBrushes.Transparent;
        if (!on && _resizing) { _resizing = false; ReleaseMouseCapture(); }
    }

    // Unified transient overlay for opacity / size feedback.
    private void ShowTransientOverlay(string text)
    {
        OverlayText.BeginAnimation(OpacityProperty, null);
        OverlayText.Text = text;
        OverlayText.Opacity = 1;
        _overlayTimer.Stop();
        _overlayTimer.Start();
    }
    private void OverlayTimer_Tick(object? sender, EventArgs e)
    {
        _overlayTimer.Stop();
        OverlayText.BeginAnimation(OpacityProperty, _overlayFade);
    }

    // Period tabs: single click switches after a short delay, double click opens today / this week / this month.
    private void TabDaily_Click(object sender, RoutedEventArgs e) => ScheduleTabSwitch(PeriodicNoteType.Daily);
    private void TabWeekly_Click(object sender, RoutedEventArgs e) => ScheduleTabSwitch(PeriodicNoteType.Weekly);
    private void TabMonthly_Click(object sender, RoutedEventArgs e) => ScheduleTabSwitch(PeriodicNoteType.Monthly);
    private void TabDaily_DoubleClick(object sender, MouseButtonEventArgs e) { CancelTabSwitch(); _ = ViewModel.OpenTodayAsync(); }
    private void TabWeekly_DoubleClick(object sender, MouseButtonEventArgs e) { CancelTabSwitch(); _ = ViewModel.OpenThisWeekAsync(); }
    private void TabMonthly_DoubleClick(object sender, MouseButtonEventArgs e) { CancelTabSwitch(); _ = ViewModel.OpenThisMonthAsync(); }
    private void ScheduleTabSwitch(PeriodicNoteType type) { _pendingTabSwitch = type; _tabClickTimer.Stop(); _tabClickTimer.Start(); }
    private void CancelTabSwitch() { _tabClickTimer.Stop(); _pendingTabSwitch = null; }
    private void TabClickTimer_Tick(object? sender, EventArgs e)
    {
        _tabClickTimer.Stop();
        if (_pendingTabSwitch is { } type) { _pendingTabSwitch = null; ViewModel.CalendarPeriod = type; _ = ViewModel.RebuildCalendarAsync(); }
    }
}
