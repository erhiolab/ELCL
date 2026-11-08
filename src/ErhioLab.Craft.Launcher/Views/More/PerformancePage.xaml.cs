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
    private SystemPerformanceSampler _sampler = new();
    private bool _initializing = true;
    private bool _failed;
    private string _cpu = "", _gpu = "";

    public PerformancePage()
    {
        InitializeComponent();
        RefreshLanguage();
        _timer.Tick += (_, _) => RefreshUsage();
        IsVisibleChanged += (_, _) => UpdateSampling();
        Loaded += (_, _) => UpdateSampling();
        Unloaded += (_, _) => _timer.Stop();
        LocalizationManager.Changed += (_, _) => RefreshLanguage();
        _ = LoadHardwareAsync();
    }

    private async Task LoadHardwareAsync()
    {
        try
        {
            (_cpu, _gpu) = await Task.Run(() => (SystemPerformanceSampler.CpuName(), string.Join(", ", SystemPerformanceSampler.DisplayAdapters())));
            App.Logger.Information("Performance", "系统硬件信息已读取.");
            RefreshHardwareText();
        }
        catch (Exception error)
        {
            App.Logger.Error("Performance", "读取硬件信息失败.", error);
        }
    }

    private void RefreshHardwareText() => HardwareText.Text = string.Format(LocalizationManager.Get("performance.hardware"),
        RuntimeInformation.OSDescription, _cpu, Environment.ProcessorCount,
        string.IsNullOrEmpty(_gpu) ? LocalizationManager.Get("performance.unavailable") : _gpu, RuntimeInformation.ProcessArchitecture);

    private void RefreshLanguage()
    {
        _initializing = true;
        IntervalBox.ItemsSource = Intervals.Select(ms => string.Format(LocalizationManager.Get("performance.seconds"), ms / 1000d)).ToArray();
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
        _timer.Stop();
        if (!IsVisible || !IsLoaded) return;
        _sampler = new SystemPerformanceSampler();
        _timer.Interval = TimeSpan.FromMilliseconds(App.Settings.PerformanceRefreshMilliseconds);
        RefreshUsage();
        _timer.Start();
    }

    private void RefreshUsage()
    {
        try
        {
            var sample = _sampler.Sample();
            var memory = GameMemoryAllocation.ReadPhysicalMemory();
            var unit = App.Settings.MemoryUnit;
            string symbol = GameMemoryAllocation.LargeUnitSymbol(unit);
            CpuText.Text = string.Format(LocalizationManager.Get("performance.cpu"), sample.Cpu);
            MemoryText.Text = string.Format(LocalizationManager.Get("performance.memory"), memory.Used(unit), memory.Total(unit), symbol,
                memory.UsedBytes * 100d / memory.TotalBytes);
            ProcessText.Text = string.Format(LocalizationManager.Get("performance.process"), sample.ProcessCpu,
                GameMemoryAllocation.ToLargeUnits(sample.WorkingSet, unit), symbol);
            var animation = TimeSpan.FromMilliseconds(Math.Min(600, App.Settings.PerformanceRefreshMilliseconds));
            CpuBar.SetValues(sample.Cpu, 100 - sample.Cpu, 0, animation);
            MemoryBar.SetValues(memory.UsedBytes, memory.ClampedAvailableBytes, 0, animation);
            string path = App.Settings.SelectedGameFolderPath ?? AppContext.BaseDirectory;
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
            DiskText.Text = drive.IsReady
                ? string.Format(LocalizationManager.Get("performance.disk"), drive.Name,
                    GameMemoryAllocation.ToLargeUnits(drive.AvailableFreeSpace, unit), GameMemoryAllocation.ToLargeUnits(drive.TotalSize, unit), symbol)
                : "";
            _failed = false;
        }
        catch (Exception error)
        {
            if (!_failed) App.Logger.Error("Performance", "性能采样失败.", error);
            _failed = true;
            CpuText.Text = MemoryText.Text = LocalizationManager.Get("performance.unavailable");
        }
    }
}
