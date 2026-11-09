using System.Windows;
using System.Windows.Controls;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>为后续标签保留宽度, 首个文本元素使用剩余空间并保持内容紧邻排列.</summary>
public sealed class TrimmingRowPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return default;
        double width = 0, height = 0;
        for (int index = 1; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var text = InternalChildren[0];
        text.Measure(new Size(Math.Max(0, availableSize.Width - width), availableSize.Height));
        return new Size(width + text.DesiredSize.Width, Math.Max(height, text.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;
        double badgesWidth = 0;
        for (int index = 1; index < InternalChildren.Count; index++) badgesWidth += InternalChildren[index].DesiredSize.Width;
        double textWidth = Math.Min(InternalChildren[0].DesiredSize.Width, Math.Max(0, finalSize.Width - badgesWidth));
        InternalChildren[0].Arrange(new Rect(0, 0, textWidth, finalSize.Height));
        double offset = textWidth;
        for (int index = 1; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            child.Arrange(new Rect(offset, 0, child.DesiredSize.Width, finalSize.Height));
            offset += child.DesiredSize.Width;
        }
        return finalSize;
    }
}
