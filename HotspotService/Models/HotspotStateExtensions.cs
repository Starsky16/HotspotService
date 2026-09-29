using System.Globalization;

namespace HotspotService.Models;

public static class HotspotStateExtensions
{
    /// <summary>
    /// 组件侧的热点状态展示：不存在无线网卡（<see cref="HotspotSupportState.NotSupported"/>）
    /// 或状态未知时显示 <c>None</c>，热点开启时显示连接设备数，其余（关闭/切换中）显示 <c>Off</c>。
    /// 与设置页「未知」是同义展示，触发条件保持一致。
    /// </summary>
    public static string ToComponentDisplayText(
        this HotspotSupportState support,
        HotspotActualState state,
        int connectedClientCount)
    {
        if (support == HotspotSupportState.NotSupported || state == HotspotActualState.Unknown)
        {
            return "None";
        }

        return state == HotspotActualState.On
            ? connectedClientCount.ToString(CultureInfo.InvariantCulture)
            : "Off";
    }

    public static HotspotActualState ToActualState(this GuardTargetState target)
    {
        return target == GuardTargetState.On ? HotspotActualState.On : HotspotActualState.Off;
    }

    public static string ToDisplayText(this GuardTargetState target)
    {
        return target == GuardTargetState.On ? "开启热点" : "关闭热点";
    }

    /// <summary>设置页用的热点状态文案（含「切换中」细节与「未知」）。</summary>
    public static string ToDisplayText(this HotspotActualState state)
    {
        return state switch
        {
            HotspotActualState.On => "已开启",
            HotspotActualState.Off => "已关闭",
            HotspotActualState.Transitioning => "切换中",
            _ => "未知"
        };
    }
}
