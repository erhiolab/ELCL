namespace ErhioLab.Craft.Launcher.Core.Java;

/// <summary>全盘 Java 搜索的扫描进度. 目录总数未知, 因此不能计算百分比.</summary>
public sealed record JavaDiscoveryProgress(long DirectoriesScanned, int CandidatesFound, string CurrentDirectory);
