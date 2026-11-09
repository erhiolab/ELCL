using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

using ErhioLab.Craft.Launcher.Accounts;
using ErhioLab.Craft.Launcher.Core.Accounts;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;

using Microsoft.Win32;

namespace ErhioLab.Craft.Launcher.Views.Launch;

internal sealed class CapeChoice(string? id, string name, MinecraftPlayerCape? cape) : INotifyPropertyChanged
{
    private string _name = name;
    private BitmapSource? _thumbnail;

    public string? Id { get; } = id;
    public MinecraftPlayerCape? Cape { get; } = cape;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }
    internal Task<BitmapSource?>? TextureTask { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Microsoft 账户的 3D 皮肤/披风预览和外观设置.</summary>
public partial class AccountAppearanceView
{
    private Guid? _accountUuid;
    private CancellationTokenSource? _loadCancellation;
    private int _selectionRevision;
    private int _capeRevision;
    private string _activeVariant = "CLASSIC";
    private string? _activeCapeId;
    private bool _busy;
    private bool _updatingChoices;
    private BitmapSource? _skin;
    private BitmapSource? _cape;
    private readonly SemaphoreSlim _capeLoadSlots = new(4);

    public AccountAppearanceView()
    {
        InitializeComponent();
        App.Skins.AvatarChanged += uuid =>
        {
            if (_accountUuid != uuid) return;
            if (Dispatcher.CheckAccess()) UpdateSkinFromCache();
            else _ = Dispatcher.InvokeAsync(UpdateSkinFromCache);
        };
        LocalizationManager.Changed += (_, _) =>
        {
            if (CapeComboBox.ItemsSource is IEnumerable<CapeChoice> choices)
                foreach (CapeChoice choice in choices)
                    if (choice.Id is null) choice.Name = LocalizationManager.Get("account.noCape");
        };
    }

    internal void ShowAccount(AccountEntry? account)
    {
        if (account?.IsMicrosoft == true && _accountUuid == account.Uuid)
        {
            UpdateSkinFromCache();
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _selectionRevision++;
        _capeRevision++;
        _accountUuid = account?.IsMicrosoft == true ? account.Uuid : null;
        _activeVariant = "CLASSIC";
        _activeCapeId = null;
        _skin = _cape = null;
        _updatingChoices = true;
        try
        {
            ClassicModelButton.IsChecked = true;
            CapeComboBox.ItemsSource = null;
        }
        finally
        {
            _updatingChoices = false;
        }

        CapePreviewImage.Source = null;
        CapeEmptyText.Visibility = Visibility.Visible;
        CapeNameText.Text = "";
        AccountAppearanceNameText.Text = account?.UserName ?? "";
        ModelPreview.SetAppearance(null, false, null);
        ModelEmptyText.Visibility = Visibility.Visible;

        UpdateActionButtons();
        if (account is not { IsMicrosoft: true }) return;

        UpdateSkinFromCache();
        App.Logger.Information("Skins", $"开始加载账户外观: {account.UserName} ({account.Uuid:N}).");
        CancellationTokenSource cancellation = new();
        _loadCancellation = cancellation;
        _ = LoadAppearanceAsync(account.Uuid, _selectionRevision, cancellation.Token);
    }

    private async Task LoadAppearanceAsync(Guid uuid, int revision, CancellationToken cancellationToken)
    {
        try
        {
            MinecraftPlayerAppearance appearance = await App.Skins.GetAppearanceAsync(uuid, cancellationToken);
            if (revision != _selectionRevision || _accountUuid != uuid) return;
            _activeVariant = string.Equals(appearance.ActiveSkin?.Variant, "SLIM", StringComparison.OrdinalIgnoreCase) ? "SLIM" : "CLASSIC";
            _updatingChoices = true;
            try
            {
                ClassicModelButton.IsChecked = _activeVariant == "CLASSIC";
                SlimModelButton.IsChecked = _activeVariant == "SLIM";
                _activeCapeId = appearance.Capes.FirstOrDefault(cape => cape.IsActive)?.Id;
                CapeChoice[] choices =
                [
                    new(null, LocalizationManager.Get("account.noCape"), null),
                    .. appearance.Capes.Select(cape => new CapeChoice(cape.Id, cape.Alias, cape))
                ];
                foreach (CapeChoice choice in choices)
                    if (choice.Cape is not null)
                        choice.TextureTask = LoadCapeTextureAsync(uuid, choice, revision, cancellationToken);
                CapeComboBox.ItemsSource = choices;
                CapeComboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Id == _activeCapeId) ?? choices[0];
            }
            finally
            {
                _updatingChoices = false;
            }

            UpdateSkinFromCache();
            App.Logger.Information("Skins", $"账户外观已加载: {uuid:N}, 皮肤={(appearance.ActiveSkin is null ? "无" : "有")}, 披风={appearance.Capes.Count}.");
            if (_skin is null && appearance.ActiveSkin is not null)
            {
                App.Logger.Information("Skins", $"开始下载账户皮肤: {uuid:N}.");
                App.Skins.RequestRefresh(uuid);
            }

            UpdateActionButtons();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            App.Logger.Information("Skins", $"账户外观加载已取消: {uuid:N}.");
        }
        catch (Exception error)
        {
            App.Logger.Error("Skins", $"无法加载账户外观: {uuid:N}.", error);
        }
    }

    private void UpdateSkinFromCache()
    {
        if (_accountUuid is not { } uuid) return;
        BitmapSource? previous = _skin;
        _skin = App.Skins.GetAvatar(uuid)?.Skin;
        ModelEmptyText.Visibility = _skin is null ? Visibility.Visible : Visibility.Collapsed;
        ModelPreview.SetAppearance(_skin, SlimModelButton.IsChecked == true, _cape);
        if (_skin is not null && !ReferenceEquals(previous, _skin))
        {
            FadeIn(ModelPreview);
            App.Logger.Information("Skins", $"账户皮肤已载入预览: {uuid:N}, {_skin.PixelWidth}x{_skin.PixelHeight}.");
        }

        UpdateActionButtons();
    }

    private void ModelChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (ModelPreview is null || SlimModelButton is null || ApplyModelButton is null) return;
        ModelPreview.SetAppearance(_skin, SlimModelButton.IsChecked == true, _cape);
        UpdateActionButtons();
        if (!_updatingChoices && _accountUuid is { } uuid) App.Logger.Information("Skins", $"已选择皮肤模型: account={uuid:N}, model={(SlimModelButton.IsChecked == true ? "SLIM" : "CLASSIC")} (待应用).");
    }

    private void CapeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionButtons();
        if (_accountUuid is { } uuid)
        {
            if (!_updatingChoices) App.Logger.Information("Skins", $"已选择披风: account={uuid:N}, cape={(CapeComboBox.SelectedItem as CapeChoice)?.Id ?? "无"} (待应用).");
            _ = ShowCapePreviewAsync(uuid, ++_capeRevision, _loadCancellation?.Token ?? CancellationToken.None);
        }
    }

    private void ResetModelPreview_Click(object sender, RoutedEventArgs e)
    {
        ModelPreview.ResetView();
        App.Logger.Information("Skins", $"已复位 3D 模型视角并重新开始空闲计时: account={_accountUuid:N}.");
    }

    private void ElytraPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (ModelPreview is null || CapePreviewImage is null) return;
        bool showElytra = ElytraPreviewCheckBox.IsChecked == true;
        ModelPreview.SetElytraPreview(showElytra);
        UpdateCapePreviewImage();
        if (_accountUuid is { } uuid && _cape is not null)
        {
            FadeIn(ModelPreview);
            FadeIn(CapePreviewImage);
            App.Logger.Information("Skins", $"已切换鞘翅预览: account={uuid:N}, enabled={showElytra}.");
        }
    }

    private async Task ShowCapePreviewAsync(Guid uuid, int revision, CancellationToken cancellationToken)
    {
        CapeChoice? choice = CapeComboBox.SelectedItem as CapeChoice;
        if (choice?.Cape is null)
        {
            _cape = null;
            CapePreviewImage.Source = null;
            CapeEmptyText.Visibility = Visibility.Visible;
            CapeEmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.account.noCapePreview");
            CapeNameText.Text = "";
            ModelPreview.SetAppearance(_skin, SlimModelButton.IsChecked == true, null);
            UpdateActionButtons();
            return;
        }

        CapeNameText.Text = choice.Name;
        App.Logger.Information("Skins", $"开始加载披风预览: account={uuid:N}, cape={choice.Id}.");
        _cape = null;
        CapePreviewImage.Source = null;
        CapeEmptyText.Visibility = Visibility.Visible;
        CapeEmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.account.capeLoading");
        ModelPreview.SetAppearance(_skin, SlimModelButton.IsChecked == true, null);
        UpdateActionButtons();
        BitmapSource? texture = await (choice.TextureTask ??= LoadCapeTextureAsync(uuid, choice, _selectionRevision, cancellationToken));

        if (revision != _capeRevision || _accountUuid != uuid) return;
        if (texture is null)
        {
            // 一次网络失败不应把这个披风固定为不可预览; 下次选择时重新尝试.
            choice.TextureTask = null;
            CapeEmptyText.SetResourceReference(TextBlock.TextProperty, "I18n.account.capePreviewUnavailable");
            return;
        }
        _cape = texture;
        UpdateCapePreviewImage();
        CapeEmptyText.Visibility = Visibility.Collapsed;
        FadeIn(CapePreviewImage);
        ModelPreview.SetAppearance(_skin, SlimModelButton.IsChecked == true, texture);
        UpdateActionButtons();
        App.Logger.Information("Skins", $"披风预览已加载: account={uuid:N}, cape={choice.Id}.");
    }

    private async Task<BitmapSource?> LoadCapeTextureAsync(Guid uuid, CapeChoice choice, int accountRevision,
        CancellationToken cancellationToken)
    {
        bool slotAcquired = false;
        try
        {
            await _capeLoadSlots.WaitAsync(cancellationToken);
            slotAcquired = true;
            byte[] png = await App.Skins.DownloadCapeAsync(uuid, choice.Cape!, cancellationToken);
            (int width, int height) = ReadPngDimensions(png);
            if (width is < 64 or > 1024 || width % 64 != 0 || height != width / 2) throw new InvalidDataException("披风图片尺寸无效.");
            BitmapSource texture = DecodeBitmap(png);
            if (texture.PixelWidth != width || texture.PixelHeight != height) throw new InvalidDataException("披风图片尺寸无效.");

            if (accountRevision == _selectionRevision && _accountUuid == uuid)
            {
                int scale = texture.PixelWidth / 64;
                CroppedBitmap thumbnail = new(texture, new Int32Rect(scale, scale, 10 * scale, 16 * scale));
                thumbnail.Freeze();
                choice.Thumbnail = thumbnail;
            }
            return texture;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error)
        {
            App.Skins.RemoveCapeCache(uuid, choice.Cape!);
            App.Logger.Error("Skins", $"无法缓存披风: account={uuid:N}, cape={choice.Id}.", error);
            return null;
        }
        finally
        {
            if (slotAcquired) _capeLoadSlots.Release();
        }
    }

    private void UpdateCapePreviewImage()
    {
        if (_cape is not { } texture)
        {
            CapePreviewImage.Source = null;
            return;
        }

        int scale = texture.PixelWidth / 64;
        if (ElytraPreviewCheckBox.IsChecked == true)
            CapePreviewImage.Source = CreateElytraPreview(texture, scale);
        else
        {
            CroppedBitmap preview = new(texture, new Int32Rect(scale, scale, 10 * scale, 16 * scale));
            preview.Freeze();
            CapePreviewImage.Source = preview;
        }
    }

    private static BitmapSource CreateElytraPreview(BitmapSource cape, int scale)
    {
        FormatConvertedBitmap converted = new(cape, PixelFormats.Pbgra32, null, 0);
        int capeStride = cape.PixelWidth * 4;
        byte[] capePixels = new byte[capeStride * cape.PixelHeight];
        converted.CopyPixels(capePixels, capeStride, 0);

        int wingWidth = 12 * scale;
        int wingHeight = 20 * scale;
        int previewWidth = 25 * scale;
        int previewHeight = 34 * scale;
        int previewStride = previewWidth * 4;
        byte[] previewPixels = new byte[previewStride * previewHeight];
        for (int y = 0; y < wingHeight; y++)
        for (int x = 0; x < wingWidth; x++)
        {
            int source = (2 * scale + y) * capeStride + (34 * scale + x) * 4;
            int row = (7 * scale + y) * previewStride;
            Buffer.BlockCopy(capePixels, source, previewPixels, row + x * 4, 4);
            Buffer.BlockCopy(capePixels, source, previewPixels, row + (13 * scale + wingWidth - 1 - x) * 4, 4);
        }

        BitmapSource preview = BitmapSource.Create(previewWidth, previewHeight, 96, 96,
            PixelFormats.Pbgra32, null, previewPixels, previewStride);
        preview.Freeze();
        return preview;
    }

    private async void UploadSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_accountUuid is not { } uuid || _busy) return;
        OpenFileDialog dialog = new() { Filter = "PNG (*.png)|*.png", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true) return;

        SetBusy(true);
        string variant = SlimModelButton.IsChecked == true ? "SLIM" : "CLASSIC";
        App.Logger.Information("Skins", $"开始上传皮肤: account={uuid:N}, file={dialog.FileName}.");
        LauncherToasts.Information(LocalizationManager.Get("account.skinUploading"));
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new InvalidDataException(LocalizationManager.Get("account.skinInvalidFile"));
            byte[] png = await File.ReadAllBytesAsync(dialog.FileName);
            (int width, int height) = ReadPngDimensions(png);
            if (width != 64 || height is not (32 or 64)) throw new InvalidDataException(LocalizationManager.Get("account.skinInvalidFile"));
            BitmapSource bitmap = DecodeBitmap(png);
            if (bitmap.PixelWidth != width || bitmap.PixelHeight != height) throw new InvalidDataException(LocalizationManager.Get("account.skinInvalidFile"));
            await App.Skins.UploadSkinAsync(uuid, png, variant);
            App.Logger.Information("Skins", $"皮肤上传成功: account={uuid:N}, model={variant}.");
            LauncherToasts.Success(LocalizationManager.Get("account.skinUploaded"));
            if (_accountUuid != uuid) return;
            _skin = bitmap;
            _activeVariant = variant;
            ModelEmptyText.Visibility = Visibility.Collapsed;
            ModelPreview.SetAppearance(bitmap, variant == "SLIM", _cape);
            FadeIn(ModelPreview);
            UpdateActionButtons();
        }
        catch (Exception error)
        {
            App.Logger.Error("Skins", $"上传皮肤失败: account={uuid:N}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("account.skinUploadFailed"), error.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ApplyModel_Click(object sender, RoutedEventArgs e)
    {
        if (_accountUuid is not { } uuid || _busy) return;
        SetBusy(true);
        App.Logger.Information("Skins", $"开始修改皮肤模型: account={uuid:N}, model={(SlimModelButton.IsChecked == true ? "SLIM" : "CLASSIC")}.");
        LauncherToasts.Information(LocalizationManager.Get("account.modelSaving"));
        try
        {
            byte[] png = App.Skins.GetCachedSkinBytes(uuid) ?? throw new InvalidDataException(LocalizationManager.Get("account.noCachedSkin"));
            string variant = SlimModelButton.IsChecked == true ? "SLIM" : "CLASSIC";
            await App.Skins.UploadSkinAsync(uuid, png, variant);
            App.Logger.Information("Skins", $"皮肤模型修改成功: account={uuid:N}, model={variant}.");
            LauncherToasts.Success(LocalizationManager.Get("account.modelSaved"));
            if (_accountUuid != uuid) return;
            _activeVariant = variant;
        }
        catch (Exception error)
        {
            App.Logger.Error("Skins", $"修改皮肤模型失败: account={uuid:N}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("account.modelSaveFailed"), error.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ApplyCape_Click(object sender, RoutedEventArgs e)
    {
        if (_accountUuid is not { } uuid || CapeComboBox.SelectedItem is not CapeChoice choice || _busy) return;
        SetBusy(true);
        App.Logger.Information("Skins", $"开始切换披风: account={uuid:N}, cape={choice.Id ?? "无"}.");
        LauncherToasts.Information(LocalizationManager.Get("account.capeSaving"));
        try
        {
            await App.Skins.SetActiveCapeAsync(uuid, choice.Id);
            App.Logger.Information("Skins", $"披风切换成功: account={uuid:N}, cape={choice.Id ?? "无"}.");
            LauncherToasts.Success(LocalizationManager.Get("account.capeSaved"));
            if (_accountUuid != uuid) return;
            _activeCapeId = choice.Id;
        }
        catch (Exception error)
        {
            App.Logger.Error("Skins", $"切换披风失败: account={uuid:N}.", error);
            LauncherToasts.Error(string.Format(LocalizationManager.Get("account.capeSaveFailed"), error.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        bool online = _accountUuid is not null;
        UploadSkinButton.IsEnabled = online && !_busy;
        ResetModelPreviewButton.IsEnabled = online && _skin is not null;
        ClassicModelButton.IsEnabled = SlimModelButton.IsEnabled = online && !_busy;
        CapeComboBox.IsEnabled = online && !_busy && CapeComboBox.Items.Count > 0;
        ElytraPreviewCheckBox.IsEnabled = online && _cape is not null;
        ApplyModelButton.IsEnabled = online && !_busy && _skin is not null && (SlimModelButton.IsChecked == true ? "SLIM" : "CLASSIC") != _activeVariant;
        ApplyCapeButton.IsEnabled = online && !_busy && CapeComboBox.SelectedItem is CapeChoice choice && choice.Id != _activeCapeId;
    }

    private static BitmapSource DecodeBitmap(byte[] png)
    {
        using MemoryStream stream = new(png, writable: false);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static (int Width, int Height) ReadPngDimensions(byte[] png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 24 || !png.AsSpan().StartsWith(signature) ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("图片不是有效的 PNG.");
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    private static void FadeIn(UIElement element)
    {
        DoubleAnimation fade = new(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
