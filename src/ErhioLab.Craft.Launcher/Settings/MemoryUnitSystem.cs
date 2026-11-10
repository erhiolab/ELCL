namespace ErhioLab.Craft.Launcher.Settings;

/// <summary>容量/速度显示与自动内存分配的换算标准.</summary>
public enum MemoryUnitSystem
{
    /// <summary>按二进制换算, 使用 MiB/GiB/TiB 等单位.</summary>
    Binary,

    /// <summary>按十进制换算, 使用 MB/GB/TB 等单位.</summary>
    Decimal
}
