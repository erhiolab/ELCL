namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>内存容量的显示与自动分配换算标准.</summary>
public enum MemoryUnitSystem
{
    /// <summary>按 1024 进制换算, 使用 GiB 和 MiB.</summary>
    Binary,

    /// <summary>按 1000 进制换算, 使用 GB 和 MB.</summary>
    Decimal
}
