using HotspotService.Models;
using Microsoft.Extensions.Hosting;

namespace HotspotService.Services;

/// <summary>
/// 独立的网速采样后台服务：与热点守护循环相互独立，按配置间隔读取热点网卡与外网网卡的累计流量，
/// 换算成瞬时速率后写入运行状态。每个周期都重新读取设置，因此开关与间隔修改后立即生效。
/// </summary>
public sealed class HotspotThroughputBackgroundService : BackgroundService
{
    /// <summary>采样被关闭时的轮询间隔：只用于及时发现开关被重新打开。</summary>
    private static readonly TimeSpan DisabledInterval = TimeSpan.FromSeconds(5);

    private readonly HotspotPluginSettingsStore _settingsStore;
    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly INetworkTrafficReader _trafficReader;
    private readonly HotspotThroughputCalculator _calculator;
    private readonly TimeProvider _timeProvider;

    public HotspotThroughputBackgroundService(
        HotspotPluginSettingsStore settingsStore,
        HotspotGuardRuntimeState runtimeState,
        INetworkTrafficReader trafficReader,
        HotspotThroughputCalculator calculator,
        TimeProvider timeProvider)
    {
        _settingsStore = settingsStore;
        _runtimeState = runtimeState;
        _trafficReader = trafficReader;
        _calculator = calculator;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SampleOnce();
            }
            catch
            {
                // 单次采样失败（例如网卡临时不可读）不应终止采样循环，下一周期重试即可。
            }

            try
            {
                await Task.Delay(ResolveInterval(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 执行一次双目标采样：采样关闭时清空基线并标记为未采样，否则依次采样热点与外网网卡。
    /// 后台循环每个周期调用一次，自测程序也用该入口验证采样行为。
    /// </summary>
    public void SampleOnce()
    {
        if (!_settingsStore.Throughput.EnableSampling)
        {
            _calculator.ResetAll();
            _runtimeState.SetThroughput(NetworkThroughputReadout.NotSampling(NetworkTrafficTarget.Hotspot));
            _runtimeState.SetThroughput(NetworkThroughputReadout.NotSampling(NetworkTrafficTarget.Internet));
            return;
        }

        SampleTarget(NetworkTrafficTarget.Hotspot);
        SampleTarget(NetworkTrafficTarget.Internet);
    }

    private TimeSpan ResolveInterval()
    {
        var settings = _settingsStore.Throughput;
        return settings.EnableSampling
            ? TimeSpan.FromSeconds(HotspotThroughputSettings.ClampInterval(settings.SamplingIntervalSeconds))
            : DisabledInterval;
    }

    private void SampleTarget(NetworkTrafficTarget target)
    {
        var sampledAt = _timeProvider.GetUtcNow();
        var result = _trafficReader.Read(target);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.InterfaceName))
        {
            _runtimeState.SetThroughput(NetworkThroughputReadout.Unavailable(
                target,
                result.Error ?? NetworkInterfaceResolver.DescribeMissing(target),
                sampledAt));
            return;
        }

        var traffic = new NetworkInterfaceTraffic(
            result.InterfaceName,
            result.Counters,
            result.Timestamp == default ? sampledAt : result.Timestamp);
        var throughput = _calculator.Calculate(target, traffic);
        _runtimeState.SetThroughput(NetworkThroughputReadout.Sampling(target, result.InterfaceName, throughput, sampledAt));
    }
}
