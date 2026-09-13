namespace HotspotService.Models;

/// <summary>
/// 由两次采样差值换算出的瞬时吞吐（字节/秒）。
/// </summary>
public readonly record struct NetworkThroughput(double DownloadBytesPerSecond, double UploadBytesPerSecond);
