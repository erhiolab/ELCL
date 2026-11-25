namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>保存在本机独立文件中的用户选择, 不参与配置导入导出.</summary>
internal sealed record LauncherConsent(int AgreementRevision = 0, bool? AnalyticsAllowed = null)
{
    // 实质修改用户协议时更新此版本, 与启动器版本号无关.
    internal const int CurrentAgreementRevision = 1;
    public bool DailyLuckDisclaimerAccepted { get; init; }

    internal LauncherConsent? Resolve(Func<bool> acceptAgreement, Func<bool> allowAnalytics)
    {
        if (AgreementRevision != CurrentAgreementRevision && !acceptAgreement()) return null;
        return this with { AgreementRevision = CurrentAgreementRevision, AnalyticsAllowed = AnalyticsAllowed ?? allowAnalytics() };
    }
}
