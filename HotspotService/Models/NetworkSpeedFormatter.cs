using System.Globalization;

namespace HotspotService.Models;

/// <summary>
/// 网速文本格式化：把字节/秒换算成易读单位，并组装组件与设置页需要的展示文本。
/// 全部为纯函数，便于自测程序直接验证。
/// </summary>
public static class NetworkSpeedFormatter
{
    private const double Kilobyte = 1024d;
    private const double Megabyte = 1024d * 1024d;
    private const double Gigabyte = 1024d * 1024d * 1024d;

    /// <summary>把字节/秒格式化为 B/s、KB/s、MB/s 或 GB/s；非法值一律按 0 处理。</summary>
    public static string FormatRate(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond <= 0)
        {
            return "0 B/s";
        }

        if (bytesPerSecond >= Gigabyte)
        {
            return $"{(bytesPerSecond / Gigabyte).ToString("0.00", CultureInfo.InvariantCulture)} GB/s";
        }

        if (bytesPerSecond >= Megabyte)
        {
            return $"{(bytesPerSecond / Megabyte).ToString("0.0", CultureInfo.InvariantCulture)} MB/s";
        }

        if (bytesPerSecond >= Kilobyte)
        {
            return $"{(bytesPerSecond / Kilobyte).ToString("0", CultureInfo.InvariantCulture)} KB/s";
        }

        return $"{bytesPerSecond.ToString("0", CultureInfo.InvariantCulture)} B/s";
    }

    /// <summary>
    /// 设置页用的单目标状态行：说明当前是否在采样、当前选中的网卡、速率或失败原因。
    /// </summary>
    public static string FormatStatusLine(NetworkThroughputReadout readout)
    {
        if (readout is null)
        {
            return "无数据";
        }

        if (!readout.IsSampling)
        {
            return "未启用采样";
        }

        if (!string.IsNullOrWhiteSpace(readout.Error))
        {
            return readout.Error;
        }

        if (string.IsNullOrWhiteSpace(readout.InterfaceName))
        {
            return "未找到可用网卡";
        }

        if (readout.Throughput is not { } throughput)
        {
            return $"{readout.InterfaceName}：正在采集首个采样点…";
        }

        return $"{readout.InterfaceName}：↓{FormatRate(throughput.DownloadBytesPerSecond)} ↑{FormatRate(throughput.UploadBytesPerSecond)}";
    }

    /// <summary>
    /// 组件用的展示文本：热点网卡显示 “↓下行 ↑上行”，外网网卡额外加 “WAN” 前缀；
    /// 两个目标都不可见时返回空串（组件据此隐藏该文本块）。
    /// </summary>
    public static string FormatComponentText(
        NetworkThroughputReadout hotspot,
        NetworkThroughputReadout internet,
        bool showHotspot,
        bool showInternet)
    {
        var segments = new List<string>(2);
        if (showHotspot && FormatSegment(hotspot, label: null) is { } hotspotSegment)
        {
            segments.Add(hotspotSegment);
        }

        if (showInternet && FormatSegment(internet, "WAN") is { } internetSegment)
        {
            segments.Add(internetSegment);
        }

        return string.Join(" | ", segments);
    }

    private static string? FormatSegment(NetworkThroughputReadout readout, string? label)
    {
        if (readout is null || !readout.IsSampling)
        {
            return null;
        }

        // 采样成功但还没有速率（首个采样点、刚切换网卡）或采样失败时显示占位符，避免数字跳动成 0。
        var body = readout.Throughput is { } throughput
            ? $"↓{FormatRate(throughput.DownloadBytesPerSecond)} ↑{FormatRate(throughput.UploadBytesPerSecond)}"
            : "↓— ↑—";
        return label is null ? body : $"{label} {body}";
    }
}
