namespace HotspotService.Models;

/// <summary>
/// 组件里的单路网速块：下行与上行各占一行的小字文本。
/// 与设置页的单行状态行不同，组件受主界面固定行高约束，因此拆成两行并排展示。
/// </summary>
/// <param name="Down">下行文本，例如 <c>↓1.0 MB/s</c>；无速率时为占位符 <c>↓—</c>。</param>
/// <param name="Up">上行文本，例如 <c>↑512 KB/s</c>；无速率时为占位符 <c>↑—</c>。</param>
public sealed record ComponentThroughputSegment(string Down, string Up);