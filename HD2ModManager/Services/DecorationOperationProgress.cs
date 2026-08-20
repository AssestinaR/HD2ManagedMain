namespace HD2ModManager.Services;

// Shared, UI-neutral progress contract for decoration payload generation and host rebuilding.
public sealed record DecorationOperationProgress(string Stage, int Completed, int Total)
{
    public double? Fraction => Total <= 0 ? null : (double)Completed / Total;
}
