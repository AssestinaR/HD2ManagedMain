namespace HD2ModCore.Application;

public readonly record struct IndexBuildProgress(int Current, int Total, string? CurrentArchiveId = null);
