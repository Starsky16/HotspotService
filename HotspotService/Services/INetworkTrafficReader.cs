using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 按目标读取网卡累计流量。实现必须自行消化异常，以失败结果返回原因，不要向调用方抛出：
/// 后台采样循环依赖这一点保持长时间稳定运行。
/// </summary>
public interface INetworkTrafficReader
{
    NetworkTrafficReadResult Read(NetworkTrafficTarget target);
}
