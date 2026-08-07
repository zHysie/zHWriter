using System.Windows;
using Forms = System.Windows.Forms;
using zHWriter.Core.Models;

namespace zHWriter.App;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; }
    public SettingsWindow(AppSettings source)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            DiaryRoot = source.DiaryRoot,
            TemplatesDirectory = source.TemplatesDirectory,
            AttachmentFolderName = source.AttachmentFolderName,
            LastOpenedDate = source.LastOpenedDate,
            WindowLeft = source.WindowLeft,
            WindowTop = source.WindowTop,
            ExpandedWidth = source.ExpandedWidth,
            ExpandedHeight = source.ExpandedHeight,
            TextOpacity = source.TextOpacity,
            AlwaysOnTop = source.AlwaysOnTop,
            CollapseDelayMs = source.CollapseDelayMs,
            ExpandDelayMs = source.ExpandDelayMs,
            ShowExistingEntryMarks = source.ShowExistingEntryMarks,
            WeekStartsOnMonday = source.WeekStartsOnMonday,
            LaunchAtStartup = source.LaunchAtStartup
        };
        Root.Text = Settings.DiaryRoot;
        TemplatesDir.Text = Settings.TemplatesDirectory;
        AttachmentFolder.Text = Settings.AttachmentFolderName;
        Monday.IsChecked = Settings.WeekStartsOnMonday;
        EntryMarks.IsChecked = Settings.ShowExistingEntryMarks;
        AlwaysTop.IsChecked = Settings.AlwaysOnTop;
        ExpandDelay.Text = Settings.ExpandDelayMs.ToString();
        CollapseDelay.Text = Settings.CollapseDelayMs.ToString();
    }
    private void SelectRoot_Click(object sender, RoutedEventArgs e) { using var dialog = new Forms.FolderBrowserDialog { Description = "选择笔记库根目录" }; if (dialog.ShowDialog() == Forms.DialogResult.OK) Root.Text = dialog.SelectedPath; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ExpandDelay.Text, out var expand) || !int.TryParse(CollapseDelay.Text, out var collapse) || expand < 0 || collapse < 0) { System.Windows.MessageBox.Show("延迟必须是非负整数。", "zHWriter"); return; }
        Settings.DiaryRoot = Root.Text.Trim();
        Settings.TemplatesDirectory = TemplatesDir.Text.Trim();
        Settings.AttachmentFolderName = AttachmentFolder.Text.Trim();
        Settings.WeekStartsOnMonday = Monday.IsChecked == true;
        Settings.ShowExistingEntryMarks = EntryMarks.IsChecked == true;
        Settings.AlwaysOnTop = AlwaysTop.IsChecked == true;
        Settings.ExpandDelayMs = expand;
        Settings.CollapseDelayMs = collapse;
        DialogResult = true;
    }
}
