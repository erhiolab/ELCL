using System.Windows;
using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>仅为字符串悬浮提示选择换行模板, 保留其他内容的默认呈现方式.</summary>
public sealed class ToolTipContentTemplateSelector : DataTemplateSelector
{
    /// <summary>纯文字提示使用的模板.</summary>
    public DataTemplate? TextTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => item is string ? TextTemplate : base.SelectTemplate(item, container);
}
