using System.Windows.Controls;
using System.Windows.Input;

using ErhioLab.Craft.Launcher.Localization;

namespace ErhioLab.Craft.Launcher.Controls;

/// <summary>可编辑的游戏版本选择器, 显示全部时不向请求传递占位文字.</summary>
public sealed class GameVersionComboBox : ComboBox
{
    private string AllLabel => LocalizationManager.Get("mods.allVersionsEditable");

    public GameVersionComboBox()
    {
        IsEditable = true;
        SetResourceReference(StyleProperty, "FormComboBoxStyle");
    }

    public string? Version
    {
        get => string.IsNullOrWhiteSpace(Text) || Text.Trim() == AllLabel ? null : Text.Trim();
        set => Text = string.IsNullOrWhiteSpace(value) ? AllLabel : value.Trim();
    }

    public void SetVersions(IEnumerable<string> versions, string? selectedVersion = null)
    {
        ItemsSource = new[] { AllLabel }.Concat(versions.Where(value => !string.IsNullOrWhiteSpace(value))).Distinct().ToArray();
        Version = selectedVersion;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (Version is null) Version = null;
        base.OnLostKeyboardFocus(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Version is null) Version = null;
        base.OnPreviewKeyDown(e);
    }
}
