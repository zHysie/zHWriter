using System.Windows;
using Forms = System.Windows.Forms;
using zHWriter.App.ViewModels;
using zHWriter.Core.Services;
using zHWriter.Infrastructure.FileSystem;
using zHWriter.Infrastructure.Settings;

namespace zHWriter.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private Mutex? _singleInstanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, "Local\\zHWriter.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("zHWriter 已在运行。", "zHWriter", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        var paths = new JournalPathService(); var templates = new TemplateService(paths);
        var vm = new MainViewModel(new JsonSettingsService(), paths, new JournalFileService(paths, templates), new CalendarIndexService(paths), new AttachmentService(paths));
        _window = new MainWindow(vm); CreateTrayIcon(vm); _window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _tray?.Dispose(); _window?.ViewModel.Dispose(); _singleInstanceMutex?.Dispose(); base.OnExit(e); }
    private void CreateTrayIcon(MainViewModel vm)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(() => _window?.ToggleVisibility()));
        menu.Items.Add("打开今天的日记", null, (_, _) => Dispatcher.Invoke(async () => await vm.OpenTodayAsync()));
        menu.Items.Add("打开日历", null, (_, _) => Dispatcher.Invoke(() => { _window?.Show(); _window?.Activate(); vm.IsCalendarOpen = true; _ = vm.RebuildCalendarAsync(); }));
        menu.Items.Add("打开日记库文件夹", null, (_, _) => Dispatcher.Invoke(() => _window?.OpenDiaryFolder()));
        menu.Items.Add("保存并退出", null, (_, _) => Dispatcher.Invoke(async () => await _window!.SaveAndExitAsync()));
        _tray = new Forms.NotifyIcon { Text = "zHWriter", Icon = System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(async () => { await vm.OpenTodayAsync(); _window?.Show(); _window?.Activate(); });
    }
}
