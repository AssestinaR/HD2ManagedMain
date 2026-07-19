using System.IO;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModManager.Services;
using Microsoft.Win32;

namespace HD2ModManager.ViewModels;

// Purpose: Hosts non-destructive material split and merge candidate selection, output, and progress in the workspace.
public sealed class MaterialPackagingPageViewModel : PageViewModel
{
    private readonly ModNode source;
    private readonly string modsRootDirectory;
    private readonly IMaterialPackagingApplicationService packaging;
    private readonly ModLibraryService library;
    private readonly NotificationService? notifications;
    private readonly bool requireAllExternalMaterials;
    private string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
    private MaterialPackageCandidateViewModel? selectedCandidate;
    private bool importToLibrary;
    private bool isRunning;
    private string state;

    public string SourceName => source.Metadata.Name;
    public bool RequiresCandidate { get; }
    public string OperationTitle { get; }
    public string Explanation { get; }
    public IReadOnlyList<MaterialPackageCandidateViewModel> Candidates { get; private set; } = Array.Empty<MaterialPackageCandidateViewModel>();
    public MaterialPackageCandidateViewModel? SelectedCandidate
    {
        get => selectedCandidate;
        set { if (SetField(ref selectedCandidate, value)) GenerateCommand.RaiseCanExecuteChanged(); }
    }
    public string OutputDirectory { get => outputDirectory; private set { if (SetField(ref outputDirectory, value)) GenerateCommand.RaiseCanExecuteChanged(); } }
    public bool ImportToLibrary { get => importToLibrary; set => SetField(ref importToLibrary, value); }
    public bool IsRunning { get => isRunning; private set { if (SetField(ref isRunning, value)) GenerateCommand.RaiseCanExecuteChanged(); } }
    public string State { get => state; private set => SetField(ref state, value); }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand GenerateCommand { get; }

    public MaterialPackagingPageViewModel(
        ModNode source,
        string modsRootDirectory,
        IMaterialPackagingApplicationService packaging,
        ModLibraryService library,
        NotificationService? notifications,
        bool splitEmbeddedMaterials,
        bool requireAllExternalMaterials = false)
    {
        this.source = source;
        this.modsRootDirectory = modsRootDirectory;
        this.packaging = packaging;
        this.library = library;
        this.notifications = notifications;
        this.requireAllExternalMaterials = requireAllExternalMaterials;
        RequiresCandidate = !splitEmbeddedMaterials;
        OperationTitle = splitEmbeddedMaterials ? "拆分内嵌材质" : requireAllExternalMaterials ? "嵌入外部材质" : "替换内嵌材质";
        Explanation = splitEmbeddedMaterials
            ? "将内嵌 Material/Texture 拆分为独立输出；不会修改源 Mod 或当前配置。"
            : "候选按精确 Material AssetKey 匹配；确认后生成独立候选，不会修改源 Mod 或当前配置。";
        state = RequiresCandidate ? "正在读取 Mod 库中的材质候选…" : "选择输出方式后即可开始。";
        Title = OperationTitle;
        BrowseCommand = new RelayCommand(_ => Browse());
        GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => CanGenerate());
        if (RequiresCandidate) _ = LoadCandidatesAsync();
    }

    private async Task LoadCandidatesAsync()
    {
        try
        {
            var candidates = await packaging.FindCandidatesAsync(source, library.Snapshot.Nodes.Values.ToArray(), modsRootDirectory, requireAllExternalMaterials);
            Candidates = candidates.Select(candidate => new MaterialPackageCandidateViewModel(candidate)).ToArray();
            SelectedCandidate = Candidates.FirstOrDefault(candidate => candidate.IsCompatible);
            State = Candidates.Count == 0 ? "Mod 库中没有精确匹配 Material AssetKey 的材质包。" : $"找到 {Candidates.Count} 个候选，请选择完整适配项。";
            OnPropertyChanged(nameof(Candidates));
            GenerateCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            State = $"读取材质候选失败：{exception.Message}";
            notifications?.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(12));
        }
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "选择材质候选输出根目录", Multiselect = false, InitialDirectory = OutputDirectory };
        if (dialog.ShowDialog() == true) OutputDirectory = dialog.FolderName;
    }

    private bool CanGenerate() => !IsRunning
        && !string.IsNullOrWhiteSpace(OutputDirectory)
        && (!RequiresCandidate || SelectedCandidate?.IsCompatible == true);

    private async Task GenerateAsync()
    {
        var root = ImportToLibrary
            ? Path.Combine(Path.GetTempPath(), "HD2ModManager", "MaterialPackaging", Guid.NewGuid().ToString("N"))
            : BuildDestinationDirectory();
        IsRunning = true;
        State = "正在验证 Payload 与资源图并生成材质候选…";
        try
        {
            MaterialPackagingOperationResult result;
            if (!RequiresCandidate)
            {
                result = await packaging.SplitAsync(source, modsRootDirectory, root);
            }
            else
            {
                if (SelectedCandidate is null || !library.Snapshot.Nodes.TryGetValue(SelectedCandidate.NodeId, out var candidate)) return;
                result = await packaging.MergeAsync(source, candidate, modsRootDirectory, root, requireAllExternalMaterials);
            }

            if (!result.IsSuccessful)
            {
                State = $"生成失败：{string.Join("；", result.Issues.Select(issue => issue.Message).Take(3))}";
                notifications?.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(12));
                return;
            }
            if (ImportToLibrary)
            {
                var importer = new ImportService(library);
                foreach (var directory in result.OutputDirectories) await importer.ImportPathAsync(directory, default);
            }
            State = $"完成：{result.AssetCount} 个资源，{result.GraphEdgeCount} 条引用。{(ImportToLibrary ? "已导入 Mod 库。" : $"输出：{root}")}";
            notifications?.Show("材质候选生成完成。", NotificationLevel.Info, TimeSpan.FromSeconds(8));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            State = $"生成材质候选失败：{exception.Message}";
            notifications?.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(12));
        }
        finally { IsRunning = false; }
    }

    private string BuildDestinationDirectory()
    {
        var candidateName = SelectedCandidate is null ? string.Empty : $"-{SelectedCandidate.Name}";
        return Path.Combine(OutputDirectory, $"{Sanitize(SourceName)}{Sanitize(candidateName)}+{DateTime.Now:yyyyMMdd-HHmmssfff}+{Sanitize(OperationTitle)}");
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
}

public sealed record MaterialPackageCandidateViewModel(MaterialPackageCandidate Candidate)
{
    public ModNodeId NodeId => Candidate.NodeId;
    public string Name => Candidate.Name;
    public bool IsCompatible => Candidate.IsCompatible;
    public int MatchingMaterialCount => Candidate.MatchingMaterialCount;
    public int MissingMaterialCount => Candidate.MissingMaterialCount;
    public int MissingTextureCount => Candidate.MissingTextureCount;
    public string BlockerSummary => Candidate.Blockers.Count == 0 ? string.Empty : string.Join("；", Candidate.Blockers);
    public string StatusText => IsCompatible ? (Candidate.Blockers.Count == 0 ? "完整适配" : "可写出（提醒）") : "不匹配";
    public string StatusBrush => IsCompatible ? (Candidate.Blockers.Count == 0 ? "#237A3B" : "#B06A00") : "#A33";
}
