using System.Windows;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Notifications;

namespace ErhioLab.Craft.Launcher.Views.Download;

/// <summary>展示原版游戏版本的下载前设置, 下载功能将在后续接入.</summary>
public partial class VanillaVersionDetailsPage
{
    private readonly MinecraftVersion _version;

    /// <summary>当前页面展示的可选加载器名称.</summary>
    public IReadOnlyList<string> LoaderNames { get; } = ["Forge", "NeoForge", "Fabric", "OptiFine"];

    /// <summary>请求返回原版游戏版本列表.</summary>
    internal event Action? BackRequested;

    /// <summary>创建指定游戏版本的下载详情页.</summary>
    internal VanillaVersionDetailsPage(MinecraftVersion version)
    {
        _version = version;
        InitializeComponent();
        VersionIcon.Data = LauncherIcons.ForVersion(version.Kind);
        VersionNameBox.Text = version.Id;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void StartDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        App.Logger.Information("Download", $"点击开始下载: {_version.Id}, 自定义名称: {VersionNameBox.Text}. 下载功能尚未实现.");
        LauncherToasts.Information("下载功能尚未实现.");
    }
}
