namespace ErhioLab.Craft.Launcher.Views.Shared;

/// <summary>在功能页面尚未实现时显示说明内容.</summary>
public partial class PlaceholderPage
{
    /// <summary>初始化占位页面.</summary>
    /// <param name="code">页面编号和英文标识.</param>
    /// <param name="title">页面标题.</param>
    /// <param name="description">页面功能说明.</param>
    public PlaceholderPage(string code, string title, string description)
    {
        InitializeComponent();

        PageCodeText.Text = code;
        PageTitleText.Text = title;
        PageDescriptionText.Text = description;
    }
}
