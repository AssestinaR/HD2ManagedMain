namespace HD2ModManager.Models;

// View and command policy derived from the persistent Mod kind.
public sealed record ModCapabilities(
    bool CanJoinProfile,
    bool CanDeploy,
    bool ShowsPatchAssets,
    bool ShowsCompatibility,
    bool SupportsPatchTools,
    bool SupportsDecorationPlan,
    bool SupportsDecorationAttachments)
{
    public static ModCapabilities For(bool isDecoration) => isDecoration
        ? new(false, false, false, false, false, true, false)
        : new(true, true, true, true, true, false, true);
}
