using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Core.Java;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Localization;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Settings;

/// <summary>显示本机 Java 列表并管理其选择顺序.</summary>
public partial class LaunchSettingsPage
{
    private const int PreviewCount = 3;
    private bool _showAllJava;
    private int _expansionVersion;

    public LaunchSettingsPage()
    {
        InitializeComponent();
        MoreJavaList.ItemTemplate = JavaList.ItemTemplate;
        App.JavaCatalog.Changed += RefreshControls;
        LocalizationManager.Changed += (_, _) => RefreshControls();
        RefreshControls();
    }

    private void RefreshControls()
    {
        IReadOnlyList<JavaInstallation> installed = App.Settings.JavaInstallations;
        JavaList.ItemsSource = installed.Take(PreviewCount).ToArray();
        MoreJavaList.ItemsSource = installed.Skip(PreviewCount).ToArray();
        JavaList.Visibility = installed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        JavaCountText.Text = string.Format(LocalizationManager.Get("java.count"), installed.Count);
        ExpandJavaButton.Visibility = installed.Count > PreviewCount ? Visibility.Visible : Visibility.Collapsed;
        if (installed.Count <= PreviewCount)
        {
            _showAllJava = false;
            _expansionVersion++;
            MoreJavaClip.BeginAnimation(HeightProperty, null);
            MoreJavaClip.BeginAnimation(OpacityProperty, null);
            MoreJavaClip.Height = 0;
            MoreJavaClip.Visibility = Visibility.Collapsed;
        }

        ExpandJavaButton.Content = _showAllJava ? LocalizationManager.Get("java.collapse")
            : string.Format(LocalizationManager.Get("java.expand"), installed.Count - PreviewCount);
        EmptyText.Visibility = installed.Count == 0 && !App.JavaCatalog.IsRefreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !App.JavaCatalog.IsRefreshing;
        AddButton.IsEnabled = !App.JavaCatalog.IsRefreshing;
        RefreshButton.Content = App.JavaCatalog.IsRefreshing ? LocalizationManager.Get("java.searching") : LocalizationManager.Get("java.search");
    }

    private void ExpandJavaButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllJava = !_showAllJava;
        ExpandJavaButton.Content = _showAllJava ? LocalizationManager.Get("java.collapse")
            : string.Format(LocalizationManager.Get("java.expand"), App.Settings.JavaInstallations.Count - PreviewCount);
        AnimateJavaExpansion();
    }

    private void AnimateJavaExpansion()
    {
        _expansionVersion++;
        int version = _expansionVersion;
        double startHeight = MoreJavaClip.Visibility == Visibility.Visible ? MoreJavaClip.ActualHeight : 0;
        double startOpacity = MoreJavaClip.Visibility == Visibility.Visible ? MoreJavaClip.Opacity : 0;
        MoreJavaClip.BeginAnimation(HeightProperty, null);
        MoreJavaClip.BeginAnimation(OpacityProperty, null);
        MoreJavaClip.Height = startHeight;
        MoreJavaClip.Opacity = startOpacity;
        MoreJavaClip.Visibility = Visibility.Visible;

        double width = Math.Max(JavaList.ActualWidth, 300);
        MoreJavaList.Measure(new Size(width, double.PositiveInfinity));
        double targetHeight = _showAllJava ? MoreJavaList.DesiredSize.Height : 0;
        DoubleAnimation height = new(startHeight, targetHeight, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        height.Completed += (_, _) =>
        {
            if (version != _expansionVersion) return;
            MoreJavaClip.BeginAnimation(HeightProperty, null);
            MoreJavaClip.BeginAnimation(OpacityProperty, null);
            MoreJavaClip.Height = _showAllJava ? double.NaN : 0;
            MoreJavaClip.Opacity = 1;
            if (!_showAllJava) MoreJavaClip.Visibility = Visibility.Collapsed;
        };
        MoreJavaClip.BeginAnimation(HeightProperty, height);
        MoreJavaClip.BeginAnimation(OpacityProperty, new DoubleAnimation(startOpacity, _showAllJava ? 1 : 0, TimeSpan.FromMilliseconds(200)));
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await App.JavaCatalog.RefreshAsync(true);

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = LocalizationManager.Get("launchSettings.chooseJavaExecutable"),
            Filter = LocalizationManager.Get("launchSettings.javaExecutableFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AddButton.IsEnabled = false;
            try
            {
                await App.JavaCatalog.AddAsync(dialog.FileName);
            }
            finally
            {
                AddButton.IsEnabled = true;
            }
        }
    }

    private static JavaInstallation? Entry(object sender) => (sender as Button)?.Tag as JavaInstallation;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is { } java) App.JavaCatalog.Move(java, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } java) return;
        // 折叠时若第三项下移, 展开列表以便继续看到它的新位置.
        if (!_showAllJava && App.Settings.JavaInstallations.Count > PreviewCount && App.Settings.JavaInstallations[PreviewCount - 1].Path == java.Path)
        {
            _showAllJava = true;
            ExpandJavaButton.Content = LocalizationManager.Get("java.collapse");
            // 排序完成后列表内容会刷新, 再启动展开动画.
            App.JavaCatalog.Move(java, 1);
            AnimateJavaExpansion();
            return;
        }

        App.JavaCatalog.Move(java, 1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is { } java) App.JavaCatalog.Remove(java);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Entry(sender) is not { } java)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(java.Path);
        if (folder is null || !Directory.Exists(folder))
        {
            LauncherToasts.Error(LocalizationManager.Get("launchSettings.javaFolderMissing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            App.Logger.Information("Java", $"打开 Java 文件夹: {folder}.");
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
        {
            App.Logger.Error("Java", $"无法打开 Java 文件夹: {folder}.", error);
            LauncherToasts.Error(LocalizationManager.Get("launchSettings.openJavaFolderFailed"));
        }
    }
}
