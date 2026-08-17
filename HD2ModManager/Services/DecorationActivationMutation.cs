namespace HD2ModManager.Services;

// A single requested decoration state, resolved against either every currently
// available host or one host shown by a host-detail page.
public sealed record DecorationActivationMutation(
    string DecorationId,
    bool Enabled,
    string? HostId = null);

// Returned after one durable decoration-content mutation has completed.
public sealed record DecorationActivationBatchResult(
    int ChangedDecorationCount,
    int AffectedHostCount,
    IReadOnlyCollection<string> AffectedHostIds);
