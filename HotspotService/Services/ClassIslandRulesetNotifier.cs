using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;

namespace HotspotService.Services;

public sealed class ClassIslandRulesetNotifier : IGuardStatusNotifier
{
    private readonly IRulesetService _rulesetService;

    public ClassIslandRulesetNotifier(IRulesetService rulesetService)
    {
        _rulesetService = rulesetService;
    }

    public void NotifyGuardStatusChanged()
    {
        Dispatcher.UIThread.Invoke(() => _rulesetService.NotifyStatusChanged());
    }
}
