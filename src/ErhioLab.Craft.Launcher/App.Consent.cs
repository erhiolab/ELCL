using System.IO;
using System.Text.Json;
using System.Windows;

using ErhioLab.Craft.Launcher.Core.Storage;
using ErhioLab.Craft.Launcher.Dialogs;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Notifications;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher;

public partial class App
{
    private static JsonFileStore<LauncherConsent>? _consentStore;
    private static LauncherConsent _consent = new();

    internal static bool AnonymousAnalyticsEnabled => _consent.AnalyticsAllowed == true;
    internal static bool DailyLuckDisclaimerAccepted => _consent.DailyLuckDisclaimerAccepted;

    private bool EnsureStartupConsent(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "consent.json");
        var store = new JsonFileStore<LauncherConsent>(path);
        LauncherConsent saved;
        try
        {
            saved = store.Load(new LauncherConsent());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warning("Privacy", "无法读取用户选择, 将重新询问且不会提前启用匿名统计.");
            Logger.Error("Privacy", $"读取用户选择失败: {path}.", error);
            saved = new LauncherConsent();
        }

        // 协议窗口关闭后还有统计选择; 启动完成前不能因最后一个窗口关闭而退出.
        ShutdownMode previous = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            LauncherConsent? consent = saved.Resolve(
                () => AskConsent("legal.agreementTitle", ReadBundledAgreement(), "legal.acceptAgreement", "legal.declineAgreement", longForm: true),
                () => AskConsent("legal.analyticsTitle", LocalizationManager.Get("legal.analyticsQuestion"), "legal.acceptAnalytics", "legal.declineAnalytics"));
            if (consent is null)
            {
                Logger.Information("Privacy", "用户没有同意当前协议, 退出启动器.");
                return false;
            }

            if (consent != saved)
            {
                store.Save(consent);
                Logger.Information("Privacy", $"已保存用户选择: 协议版本={consent.AgreementRevision}, 匿名统计={consent.AnalyticsAllowed}.");
            }

            _consentStore = store;
            _consent = consent;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Error("Privacy", "无法完成协议确认.", error);
            LauncherDialogs.Error(LocalizationManager.Get("app.startFailed"), LocalizationManager.Get("legal.consentUnavailable"));
            return false;
        }
        finally
        {
            ShutdownMode = previous;
        }
    }

    private static bool AskConsent(string title, string body, string accept, string decline, bool longForm = false)
    {
        var dialog = new LauncherDialogWindow(LocalizationManager.Get(title), body, LauncherDialogLevel.Information, true, LocalizationManager.Get(accept));
        dialog.ConfigureDecision(LocalizationManager.Get(decline), longForm);
        return dialog.ShowDialog() == true;
    }

    private static string ReadBundledAgreement()
    {
        const string document = "ErhioLab.Craft.Launcher.Legal.Terms.zh_cn.md";
        using Stream stream = typeof(App).Assembly.GetManifestResourceStream(document) ?? throw new InvalidDataException("找不到随程序分发的用户协议.");
        using StreamReader reader = new(stream);
        return string.Join(Environment.NewLine, reader.ReadToEnd().Split('\n').Skip(1)
            .Select(line => line.TrimEnd('\r')).Select(line => line.StartsWith("## ", StringComparison.Ordinal) ? line[3..] : line)).Trim();
    }

    internal static bool UpdateAnalyticsConsent(bool enabled)
    {
        if (AnonymousAnalyticsEnabled == enabled) return true;
        if (_consentStore is null) return false;
        var updated = _consent with { AnalyticsAllowed = enabled };
        if (!enabled)
        {
            _analytics?.SetEnabled(false);
            _consent = updated;
        }

        try
        {
            _consentStore.Save(updated);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Logger.Error("Privacy", "保存匿名统计偏好失败.", error);
            LauncherToasts.Error(LocalizationManager.Get(enabled ? "legal.consentSaveFailed" : "legal.disableConsentSaveFailed"));
            return false;
        }

        _consent = updated;
        _analytics?.SetEnabled(enabled);
        return true;
    }

    internal static bool AcceptDailyLuckDisclaimer()
    {
        if (DailyLuckDisclaimerAccepted) return true;
        if (_consentStore is not { } store) return false;

        var updated = _consent with { DailyLuckDisclaimerAccepted = true };
        Logger.Information("Toolbox", "开始保存今日人品免责声明的确认状态.");
        try { store.Save(updated); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Logger.Error("Toolbox", "保存今日人品免责声明的确认状态失败.", error);
            LauncherToasts.Error(LocalizationManager.Get("toolbox.luckDisclaimerSaveFailed"));
            return false;
        }

        _consent = updated;
        Logger.Information("Toolbox", "已保存今日人品免责声明的确认状态.");
        return true;
    }
}
