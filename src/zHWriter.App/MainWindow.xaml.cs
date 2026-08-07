using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using zHWriter.App.ViewModels;
using zHWriter.App.Windows;

namespace zHWriter.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _collapseTimer = new();
    private readonly DispatcherTimer _colorTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool _resizing;
    private System.Windows.Point _resizeStart;
    private System.Windows.Size _resizeSize;
    public MainViewModel ViewModel { get; }
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); ViewModel = viewModel; DataContext = viewModel;
        _collapseTimer.Tick += async (_, _) => { _collapseTimer.Stop(); if (!IsMouseOver && !ViewModel.IsCalendarOpen) await CollapseAsync(); };
        _colorTimer.Tick += (_, _) => RefreshForegroundColor();
        ViewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ViewModel.IsExpanded)) UpdateVisualState(); };
        ViewModel.ExternalConflictDetected += async (_, _) => await HandleExternalConflictAsync();
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var initialized = await ViewModel.InitializeAsync(); if (!initialized) await ChooseDiaryRootAsync();
        if (ViewModel.RecoverableTemporaryFiles.Count > 0 && System.Windows.MessageBox.Show($"检测到 {ViewModel.RecoverableTemporaryFiles.Count} 个比正式日记更新的临时保存文件。是否恢复？\n恢复前会保留原文件备份。", "zHWriter：恢复未完成保存", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) await ViewModel.RestoreRecoverableFilesAsync();
        RestoreWindowPlacement(); ViewModel.IsExpanded = false; UpdateVisualState(); _colorTimer.Start();
    }
    private void RestoreWindowPlacement()
    {
        if (!double.IsNaN(ViewModel.Settings.WindowLeft) && SystemParameters.WorkArea.Contains(new System.Windows.Point(ViewModel.Settings.WindowLeft, ViewModel.Settings.WindowTop))) { Left = ViewModel.Settings.WindowLeft; Top = ViewModel.Settings.WindowTop; }
        else { Left = SystemParameters.WorkArea.Right - 70; Top = SystemParameters.WorkArea.Top + 32; }
        Width = ViewModel.Settings.ExpandedWidth; Height = ViewModel.Settings.ExpandedHeight; Topmost = ViewModel.Settings.AlwaysOnTop;
    }
    private void UpdateVisualState()
    {
        ExpandedPanel.Visibility = ViewModel.IsExpanded ? Visibility.Visible : Visibility.Collapsed; CollapsedPanel.Visibility = ViewModel.IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        if (ViewModel.IsExpanded) { Width = ViewModel.Settings.ExpandedWidth; Height = ViewModel.Settings.ExpandedHeight; Editor.Focus(); } else { Width = 52; Height = 28; }
    }
    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { _collapseTimer.Stop(); if (!ViewModel.IsExpanded) _ = ExpandDelayedAsync(); }
    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { if (ViewModel.IsExpanded && !ViewModel.IsCalendarOpen) { _collapseTimer.Interval = TimeSpan.FromMilliseconds(ViewModel.Settings.CollapseDelayMs); _collapseTimer.Start(); } }
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
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { await ViewModel.SaveAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { ViewModel.IsCalendarOpen = true; await ViewModel.RebuildCalendarAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape) { await CollapseAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Q) { await SaveAndExitAsync(); e.Handled = true; }
    }
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        if (e.ChangedButton == MouseButton.Left) { DragMove(); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.Right) { _resizing = true; _resizeStart = e.GetPosition(this); _resizeSize = new System.Windows.Size(Width, Height); CaptureMouse(); e.Handled = true; }
    }
    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_resizing) return;
        var current = e.GetPosition(this); Width = Math.Max(MinWidth, _resizeSize.Width + current.X - _resizeStart.X); Height = Math.Max(MinHeight, _resizeSize.Height + current.Y - _resizeStart.Y); e.Handled = true;
    }
    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing || e.ChangedButton != MouseButton.Right) return;
        _resizing = false; ReleaseMouseCapture(); SaveMetrics(); e.Handled = true;
    }
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e) { if (Keyboard.Modifiers != ModifierKeys.Alt) return; ViewModel.UpdateWindowMetrics(Left, Top, Width, Height, ViewModel.Settings.TextOpacity + (e.Delta > 0 ? .1 : -.1)); e.Handled = true; }
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) { _colorTimer.Stop(); SaveMetrics(); }
    private void SaveMetrics() => ViewModel.UpdateWindowMetrics(Left, Top, Width, Height, ViewModel.Settings.TextOpacity);
    private void Calendar_Click(object sender, RoutedEventArgs e) => ViewModel.IsCalendarOpen = true;
    private async void Save_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();
    private async void Exit_Click(object sender, RoutedEventArgs e) => await SaveAndExitAsync();
    public async Task SaveAndExitAsync() { if (await ViewModel.SaveAsync()) Close(); }
    public void ToggleVisibility() { if (IsVisible) Hide(); else { Show(); Activate(); } }
    public void OpenDiaryFolder() => OpenFolder(ViewModel.Settings.DiaryRoot);
    private void OpenDiaryFolder_Click(object sender, RoutedEventArgs e) => OpenDiaryFolder();
    private void OpenCurrentFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.CurrentJournalDirectory);
    private static void OpenFolder(string path) { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(ViewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true) await ViewModel.ApplySettingsAsync(dialog.Settings);
    }
    private async Task ChooseDiaryRootAsync()
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "请选择 zHWriter 日记库根目录", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) await ViewModel.ConfigureDiaryRootAsync(dialog.SelectedPath);
    }

    private async Task HandleExternalConflictAsync()
    {
        var choice = System.Windows.MessageBox.Show("当前日记已被外部程序修改。\n“是”：保留我的内容并覆盖磁盘版本；\n“否”：重新加载磁盘内容；\n“取消”：选择位置另存为副本。", "zHWriter：外部修改冲突", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
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
        var point = PointToScreen(new System.Windows.Point(Math.Max(12, Width * .5), Math.Max(36, Height * .5)));
        if (ScreenColorSampler.TryGetScreenColor((int)Math.Round(point.X), (int)Math.Round(point.Y), out var color)) ViewModel.SetBackgroundBrightness(ScreenColorSampler.IsLight(color));
    }
}
