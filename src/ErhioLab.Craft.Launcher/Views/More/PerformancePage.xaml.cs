using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Threading;

using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;
using ErhioLab.Craft.Launcher.SystemTools;

namespace ErhioLab.Craft.Launcher.Views.More;

public partial class PerformancePage
{
    private static readonly int[] Intervals = [500, 1000, 2000, 5000, 10000];
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly HashSet<string> _failedSamples = [];
    private SystemPerformanceSampler? _sampler;
    private bool _initializing = true;
    private bool _hardwareLoaded;
    private string _cpuName = "";
    private GraphicsAdapterInfo[] _graphicsAdapters = [];
    private ProcessorTopology? _topology;
    private long _workingSet;

    public PerformancePage()
    {
        InitializeComponent();
        RefreshLanguage();
        _timer.Tick += (_, _) => RefreshUsage();
        IsVisibleChanged += (_, _) => UpdateSampling();
        Loaded += (_, _) => UpdateSampling();
        Unloaded += (_, _) => StopSampling();
        LocalizationManager.Changed += (_, _) => RefreshLanguage();
        _ = LoadHardwareAsync();
    }

    private async Task LoadHardwareAsync()
    {
        (_cpuName, _topology, _graphicsAdapters) = await Task.Run(() =>
            (ReadHardware(SystemPerformanceSampler.CpuName, "", "处理器型号"),
             ReadHardware(ProcessorTopology.Read, null, "处理器拓扑"),
             ReadHardware(SystemPerformanceSampler.DisplayAdapters, Array.Empty<GraphicsAdapterInfo>(), "硬件显卡")));
        _hardwareLoaded = true;
        App.Logger.Information("Performance", $"硬件信息已读取: CPU={_cpuName}, 核心={_topology?.Cores}, 线程={_topology?.Threads}, 性能核={_topology?.PerformanceCores}, 能效核={_topology?.EfficiencyCores}, 硬件显卡={string.Join(", ", _graphicsAdapters.Select(adapter => adapter.Name + " (" + adapter.Kind + ")"))}.");
        RefreshHardwareText();
    }

    private static T ReadHardware<T>(Func<T> read, T fallback, string name)
    {
        try { return read(); }
        catch (Exception error)
        {
            App.Logger.Error("Performance", $"读取{name}失败.", error);
            return fallback;
        }
    }

    private void RefreshHardwareText()
    {
        CpuModelText.Text = !_hardwareLoaded ? L("performance.loading") : string.IsNullOrWhiteSpace(_cpuName) ? L("performance.unavailable") : _cpuName;
        CpuTopologyText.Text = !_hardwareLoaded ? L("performance.loading") : _topology switch
        {
            { PerformanceCores: { } performance, EfficiencyCores: { } efficiency } topology =>
                string.Format(L("performance.hybridCores"), performance, efficiency, topology.Threads),
            { } topology => string.Format(L("performance.cores"), topology.Cores, topology.Threads),
            _ => string.Format(L("performance.threadsOnly"), Environment.ProcessorCount)
        };
        GraphicsCards.ItemsSource = _graphicsAdapters.Length > 0
            ? _graphicsAdapters.Select((adapter, index) => new GraphicsRow(string.Format(L("performance.graphicsNumber"), index + 1), adapter.Name,
                L(adapter.Kind == GraphicsAdapterKind.Integrated ? "performance.integratedGraphics" : "performance.discreteGraphics"))).ToArray()
            : [new GraphicsRow(L("performance.graphics"), L(_hardwareLoaded ? "performance.unavailable" : "performance.loading"))];
        SystemNameText.Text = RuntimeInformation.OSDescription;
        SystemArchitectureText.Text = string.Format(L("performance.architectures"), RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);
    }

    private void RefreshLanguage()
    {
        _initializing = true;
        IntervalBox.ItemsSource = Intervals.Select(ms => string.Format(L("performance.refreshEvery"), ms / 1000d)).ToArray();
        IntervalBox.SelectedIndex = Array.IndexOf(Intervals, App.Settings.PerformanceRefreshMilliseconds);
        _initializing = false;
        RefreshHardwareText();
        if (IsVisible && IsLoaded) RefreshUsage();
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || IntervalBox.SelectedIndex < 0) return;
        int milliseconds = Intervals[IntervalBox.SelectedIndex];
        if (!App.UpdateSettings(App.Settings with { PerformanceRefreshMilliseconds = milliseconds })) return;
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        App.Logger.Information("Performance", $"性能采样间隔调整为 {milliseconds} ms.");
    }

    private void UpdateSampling()
    {
        StopSampling();
        if (!IsVisible || !IsLoaded) return;
        _sampler = CreateSampler();
        _timer.Interval = TimeSpan.FromMilliseconds(App.Settings.PerformanceRefreshMilliseconds);
        RefreshUsage();
        _timer.Start();
    }

    private void StopSampling()
    {
        _timer.Stop();
        _sampler?.Dispose();
        _sampler = null;
    }

    private static SystemPerformanceSampler CreateSampler() => new(error =>
        App.Logger.Error("Performance", "CPU 效用计数器不可用, 回退到处理器忙碌时间指标.", error));

    private void RefreshUsage()
    {
        var unit = App.Settings.MemoryUnit;
        string Capacity(double bytes)
        {
            var displayUnit = DataSizeFormatter.SelectUnit(bytes, unit);
            return string.Format(L("performance.capacityValue"), displayUnit.Convert(bytes), displayUnit.Symbol);
        }
        var animation = TimeSpan.FromMilliseconds(Math.Min(600, App.Settings.PerformanceRefreshMilliseconds));
        Sample("CPU", () =>
        {
            _sampler ??= CreateSampler();
            var sample = _sampler.Sample();
            _workingSet = sample.WorkingSet;
            CpuUsage.Value = string.Format(L("performance.percentValue"), sample.Cpu);
            ProcessCpuUsage.Value = string.Format(L("performance.percentValue"), sample.ProcessCpu);
            CpuBar.SetValues(sample.Cpu, 100 - sample.Cpu, 0, animation);
        }, () =>
        {
            CpuUsage.Value = ProcessCpuUsage.Value = L("performance.unavailable");
            _workingSet = 0;
            CpuBar.SetValues(0, 1, 0, animation);
        });
        Sample("Memory", () =>
        {
            var memory = GameMemoryAllocation.ReadPhysicalMemory();
            MemoryCapacityText.Text = string.Format(L("performance.physicalMemory"), Capacity(memory.TotalBytes));
            MemoryUsed.Value = Capacity(memory.UsedBytes);
            MemoryAvailable.Value = Capacity(memory.ClampedAvailableBytes);
            ProcessMemory.Value = _failedSamples.Contains("CPU") ? L("performance.unavailable") : Capacity(_workingSet);
            MemoryBar.SetValues(memory.UsedBytes, memory.ClampedAvailableBytes, 0, animation);
        }, () =>
        {
            MemoryCapacityText.Text = MemoryUsed.Value = MemoryAvailable.Value = ProcessMemory.Value = L("performance.unavailable");
            MemoryBar.SetValues(0, 1, 0, animation);
        });
        Sample("Disk", () =>
        {
            string path = App.Settings.SelectedGameFolderPath ?? AppContext.BaseDirectory;
            DiskPathText.Text = string.Format(L("performance.gameDirectory"), path);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
            if (!drive.IsReady) throw new IOException("游戏所在磁盘未就绪.");
            DiskNameText.Text = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : string.Format(L("performance.volumeName"), drive.Name, drive.VolumeLabel);
            long total = drive.TotalSize, free = drive.TotalFreeSpace, available = drive.AvailableFreeSpace;
            DiskUsed.Value = Capacity(total - free);
            DiskAvailable.Value = Capacity(available);
            DiskCapacity.Value = Capacity(total);
            DiskBar.SetValues(total - free, free, 0, animation);
        }, () =>
        {
            DiskNameText.Text = DiskUsed.Value = DiskAvailable.Value = DiskCapacity.Value = L("performance.unavailable");
            DiskBar.SetValues(0, 1, 0, animation);
        });
    }

    private void Sample(string component, Action sample, Action unavailable)
    {
        try
        {
            sample();
            if (_failedSamples.Remove(component)) App.Logger.Information("Performance", $"{component} 采样已恢复.");
        }
        catch (Exception error)
        {
            if (_failedSamples.Add(component)) App.Logger.Error("Performance", $"{component} 采样失败.", error);
            unavailable();
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);

    public sealed record GraphicsRow(string Title, string Name, string? Badge = null)
    {
        public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
    }
}
