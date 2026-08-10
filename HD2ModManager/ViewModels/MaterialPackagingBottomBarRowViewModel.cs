namespace HD2ModManager.ViewModels;

public enum MaterialPackagingBottomBarRowKind
{
    Output,
    Options,
    Candidates
}

// A row is a visual projection of one material-packaging operation. The
// operation owns state and commands; this type only selects the row template.
public sealed record MaterialPackagingBottomBarRowViewModel(
    MaterialPackagingPageViewModel Operation,
    MaterialPackagingBottomBarRowKind Kind)
{
    public bool IsOutput => Kind == MaterialPackagingBottomBarRowKind.Output;
    public bool IsOptions => Kind == MaterialPackagingBottomBarRowKind.Options;
    public bool IsCandidates => Kind == MaterialPackagingBottomBarRowKind.Candidates;
}
