namespace HD2ModManager.Services;

public sealed record DecorationActivationSummary(int EnabledHostCount, int AvailableHostCount, bool IsEnabledForAllAvailableHosts, string StatusText);
