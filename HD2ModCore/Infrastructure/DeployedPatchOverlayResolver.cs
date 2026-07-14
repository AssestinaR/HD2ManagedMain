using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Verifies deployed patch origins and builds AssetKey-level winners from activation state.
public sealed class DeployedPatchOverlayResolver
{
	private const string StateFileName = "activation-state.json";
	private readonly PatchFileNameParser fileNameParser = new();
	private readonly HD2ModAdaptation.PatchReconstruction.PatchTocScanner tocScanner = new();

	public async ValueTask<DeployedPatchOverlay> ResolveAsync(string gameDataDirectory, CancellationToken cancellationToken = default)
	{
		var statePath = Path.Combine(gameDataDirectory, StateFileName);
		if (!File.Exists(statePath)) return DeployedPatchOverlay.Empty;
		ActivationState? state;
		try
		{
			await using var stream = File.OpenRead(statePath);
			state = await JsonSerializer.DeserializeAsync<ActivationState>(stream, Options, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			return new DeployedPatchOverlay(Array.Empty<DeployedPatchGroup>(), new[] { new CoreIssue(CoreIssueSeverity.Error, "ActivationStateInvalid", exception.Message, statePath) });
		}
		if (state is null) return DeployedPatchOverlay.Empty;

		var groups = new List<DeployedPatchGroup>();
		var issues = new List<CoreIssue>();
		foreach (var group in state.Files.Where(file => file.SidecarKind == PatchSidecarKind.Base).GroupBy(file => (file.ArchiveHex16, file.TargetPatchIndex)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var baseFile = group.First();
			var groupFiles = state.Files.Where(file => string.Equals(file.ArchiveHex16, group.Key.ArchiveHex16, StringComparison.OrdinalIgnoreCase) && file.TargetPatchIndex == group.Key.TargetPatchIndex).ToArray();
			var groupIssues = new List<CoreIssue>();
			foreach (var file in groupFiles)
			{
				var issue = await ValidateFileAsync(file, cancellationToken).ConfigureAwait(false);
				if (issue is not null) groupIssues.Add(issue);
			}
			IReadOnlySet<HD2ModAdaptation.PatchReconstruction.AssetKey> keys = new HashSet<HD2ModAdaptation.PatchReconstruction.AssetKey>();
			if (groupIssues.Count == 0)
			{
				try { keys = (await tocScanner.ScanEntriesAsync(baseFile.TargetPath, cancellationToken).ConfigureAwait(false)).Select(entry => entry.AssetKey).ToHashSet(); }
				catch (Exception exception) { groupIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedPatchTocInvalid", exception.Message, baseFile.TargetPath, baseFile.NodeId)); }
			}
			issues.AddRange(groupIssues);
			groups.Add(new DeployedPatchGroup(baseFile.ArchiveHex16, baseFile.TargetPatchIndex, Path.GetFileName(baseFile.TargetPath), baseFile.NodeId,
				keys.Select(key => new AssetKey(key.TypeId, key.FileId)).ToHashSet(), groupIssues));
		}
		return new DeployedPatchOverlay(groups, issues);
	}

	private static async ValueTask<CoreIssue?> ValidateFileAsync(ActivationFile file, CancellationToken cancellationToken)
	{
		if (!File.Exists(file.TargetPath)) return new CoreIssue(CoreIssueSeverity.Error, "DeployedTargetMissing", $"Deployed target is missing: {file.TargetPath}", file.TargetPath, file.NodeId);
		if (!File.Exists(file.SourcePath)) return new CoreIssue(CoreIssueSeverity.Error, "DeployedSourceMissing", $"Mod library source is missing: {file.SourcePath}", file.SourcePath, file.NodeId);
		var target = new FileInfo(file.TargetPath); var source = new FileInfo(file.SourcePath);
		if (target.Length != source.Length) return new CoreIssue(CoreIssueSeverity.Error, "DeployedLengthMismatch", "Deployed file length differs from the mod library source.", file.TargetPath, file.NodeId);
		if (file.Method == DeploymentMethod.SymbolicLink)
		{
			var targetInfo = File.ResolveLinkTarget(file.TargetPath, returnFinalTarget: true);
			if (targetInfo is null || !string.Equals(Path.GetFullPath(targetInfo.FullName), Path.GetFullPath(file.SourcePath), StringComparison.OrdinalIgnoreCase))
				return new CoreIssue(CoreIssueSeverity.Error, "DeployedLinkMismatch", "Symbolic link no longer points to the recorded mod library source.", file.TargetPath, file.NodeId);
			return null;
		}
		if (file.Method == DeploymentMethod.Copy)
		{
			var targetHash = await HashFileAsync(file.TargetPath, cancellationToken).ConfigureAwait(false);
			var sourceHash = await HashFileAsync(file.SourcePath, cancellationToken).ConfigureAwait(false);
			if (!targetHash.AsSpan().SequenceEqual(sourceHash)) return new CoreIssue(CoreIssueSeverity.Error, "DeployedContentMismatch", "Deployed copy differs from the mod library source.", file.TargetPath, file.NodeId);
		}
		return null;
	}

	private static async Task<byte[]> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
		return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
	}

	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
	private sealed record ActivationState(int Version, ProfileId? ProfileId, DateTimeOffset AppliedUtc, IReadOnlyList<ActivationFile> Files);
	private sealed record ActivationFile(string TargetPath, string SourcePath, DeploymentMethod Method, string ArchiveHex16, int TargetPatchIndex, PatchSidecarKind SidecarKind, ModNodeId? NodeId);
}

public sealed record DeployedPatchOverlay(IReadOnlyList<DeployedPatchGroup> Groups, IReadOnlyList<CoreIssue> Issues)
{
	public static DeployedPatchOverlay Empty { get; } = new(Array.Empty<DeployedPatchGroup>(), Array.Empty<CoreIssue>());
}

public sealed record DeployedPatchGroup(string ArchiveId, int TargetPatchIndex, string PatchGroupName, ModNodeId? NodeId, IReadOnlySet<AssetKey> AssetKeys, IReadOnlyList<CoreIssue> Issues)
{
	public bool IsValid => Issues.Count == 0;
}
