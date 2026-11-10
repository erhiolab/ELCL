using System.ComponentModel;
using System.Windows.Media;
using ErhioLab.Craft.Launcher.Icons;
using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Navigation;

/// <summary>为侧栏模板提供可识别的导航项数据.</summary>
internal sealed class SectionSidebarEntry : INotifyPropertyChanged
{
    public SectionSidebarEntry(SectionSidebarItem item, bool isFirst, string groupName)
    {
        Item = item;
        IsFirst = isFirst;
        GroupName = groupName;
        LocalizationManager.Changed += (_, _) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionToolTip)));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public SectionSidebarItem Item { get; }
    public bool IsFirst { get; }
    public string GroupName { get; }
    /// <summary>侧栏显示的名称.</summary>
    public string Title => LocalizationManager.Get(Item.Title);

    /// <summary>侧栏显示的图标.</summary>
    public Geometry Icon => Item.Icon;

    /// <summary>是否显示悬浮操作按钮.</summary>
    public bool CanShowAction => Item.CanReset || Item.CanRefresh;

    /// <summary>刷新和重置使用不同的操作图标.</summary>
    public Geometry ActionIcon => Item.CanRefresh ? LauncherIcons.Refresh : LauncherIcons.Reset;

    /// <summary>操作按钮的提示文字.</summary>
    public string ActionToolTip => Item.ActionToolTipKey is { } key ? LocalizationManager.Get(key)
        : Item.CanRefresh ? LocalizationManager.Get("sidebar.refresh") : string.Format(LocalizationManager.Get("sidebar.reset"), Title);
}
