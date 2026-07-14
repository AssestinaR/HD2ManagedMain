using System.Security.Cryptography;
using System.Text;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Reconciles Data files and activation state, validates origins and computes actual AssetKey winners by target index.
public sealed class DeployedOverrideGraphService : IDeployedOverrideGraphService
{
	private readonly IActivationStateStore _activationStateStore;
	private readonly IPatchFileNameParser _fileNameParser;
	private readonly HD2ModAdaptation.PatchReconstruction.PatchTocScanner _tocScanner;

	public DeployedOverrideGraphService(IActivationStateStore activationStateStore, IPatchFileNameParser fileNameParser)
	{
		_activationStateStore = activationStateStore ?? throw new ArgumentNullException(nameof(activationStateStore));
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_tocScanner = new HD2ModAdaptation.PatchReconstruction.PatchTocScanner();
	}

	public async ValueTask<DeployedOverrideGraph> BuildAsync(string gameDataDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		var directory = Path.GetFullPath(gameDataDirectory);
		var issues = new List<CoreIssue>();
		ActivationState? state = null;
		try
		{
			state = await _activationStateStore.TryLoadAsync(directory, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "ActivationStateInvalid", exception.Message, Path.Combine(directory, JsonActivationStateStore.StateFileName), ExceptionMessage: exception.ToString()));
		}

		var dataFiles = EnumerateDataFiles(directory).ToList();
		var stateByTarget = (state?.Files ?? Array.Empty<ActivationStateFileEntry>())
			.GroupBy(file => Path.GetFullPath(file.TargetPath), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
		var dataPaths = dataFiles.Select(file => Path.GetFullPath(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var recorded in stateByTarget.Where(pair => !dataPaths.Contains(pair.Key)))
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "RecordedTargetMissing", "Activation state records a target that is missing from Data.", recorded.Key, recorded.Value.NodeId));
		}

		var groups = new List<DeployedPatchGroupFact>();
		foreach (var dataGroup in dataFiles.GroupBy(file => (Archive: file.Info.ArchiveHex16.ToLowerInvariant(), file.Info.PatchIndex)).OrderBy(group => group.Key.Archive).ThenBy(group => group.Key.PatchIndex))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var groupIssues = new List<CoreIssue>();
			var files = new List<DeployedPatchFileFact>();
			foreach (var observed in dataGroup.OrderBy(file => file.Info.SidecarKind))
			{
				stateByTarget.TryGetValue(Path.GetFullPath(observed.Path), out var activationEntry);
				if (activationEntry is null)
				{
					groupIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "UntrackedDataPatch", "Data contains a patch file not recorded by activation state.", observed.Path));
				}
				else
				{
					groupIssues.AddRange(await ValidateFileAsync(observed.Path, observed.Info, activationEntry, cancellationToken).ConfigureAwait(false));
				}
				var info = new FileInfo(observed.Path);
				files.Add(new DeployedPatchFileFact(observed.Path, observed.Info.SidecarKind, info.Length, info.LastWriteTimeUtc, activationEntry));
			}
			if (files.All(file => file.SidecarKind != PatchSidecarKind.Base))
			{
				groupIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedSidecarWithoutBase", "Deployed sidecar has no base patch.", files.FirstOrDefault()?.TargetPath));
			}
			var baseFile = files.FirstOrDefault(file => file.SidecarKind == PatchSidecarKind.Base);
			var assetKeys = new HashSet<AssetKey>();
			if (baseFile is not null)
			{
				try
				{
					foreach (var entry in await _tocScanner.ScanEntriesAsync(baseFile.TargetPath, cancellationToken).ConfigureAwait(false)) assetKeys.Add(new AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId));
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					groupIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedPatchTocInvalid", exception.Message, baseFile.TargetPath, baseFile.ActivationEntry?.NodeId, exception.ToString()));
				}
			}
			var representative = baseFile?.ActivationEntry ?? files.Select(file => file.ActivationEntry).FirstOrDefault(entry => entry is not null);
			ModPatchGroupId? sourceGroupId = representative?.NodeId is { } nodeId ? new ModPatchGroupId(nodeId, representative.ArchiveHex16, representative.SourcePatchIndex) : null;
			issues.AddRange(groupIssues);
			groups.Add(new DeployedPatchGroupFact(dataGroup.Key.Archive, dataGroup.Key.PatchIndex, sourceGroupId, representative?.NodeId, files, assetKeys, groupIssues));
		}

		var chains = groups
			.SelectMany(group => group.AssetKeys.Select(assetKey => (assetKey, group)))
			.GroupBy(item => item.assetKey)
			.Select(group =>
			{
				var ordered = group.Select(item => item.group).OrderBy(item => item.TargetPatchIndex).ThenBy(item => item.ArchiveHex16, StringComparer.OrdinalIgnoreCase).ToList();
				return new DeployedAssetOverrideChain(group.Key, ordered.Select((item, index) => new DeployedAssetOverrideEntry(item.ArchiveHex16, item.TargetPatchIndex, item.SourcePatchGroupId, item.NodeId, index == ordered.Count - 1)).ToList());
			})
			.OrderBy(chain => chain.AssetKey.TypeId)
			.ThenBy(chain => chain.AssetKey.FileId)
			.ToList();
		var generation = ComputeGeneration(dataFiles, state);
		return new DeployedOverrideGraph(directory, generation, DateTimeOffset.UtcNow, state?.ProfileId, state?.ProfileRevision ?? 0, groups, chains, issues);
	}

	private IEnumerable<(string Path, PatchFileNameInfo Info)> EnumerateDataFiles(string directory)
	{
		if (!Directory.Exists(directory)) yield break;
		foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
		{
			if (_fileNameParser.TryParse(Path.GetFileName(path), out var info) && info is not null) yield return (path, info);
		}
	}

	private static async ValueTask<IReadOnlyList<CoreIssue>> ValidateFileAsync(string observedPath, PatchFileNameInfo observed, ActivationStateFileEntry recorded, CancellationToken cancellationToken)
	{
		var issues = new List<CoreIssue>();
		if (!string.Equals(observed.ArchiveHex16, recorded.ArchiveHex16, StringComparison.OrdinalIgnoreCase) || observed.PatchIndex != recorded.TargetPatchIndex || observed.SidecarKind != recorded.SidecarKind)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedIdentityMismatch", "Data filename identity differs from activation state.", observedPath, recorded.NodeId));
		}
		if (recorded.Method == DeploymentMethod.SymbolicLink)
		{
			var resolved = File.ResolveLinkTarget(observedPath, returnFinalTarget: true);
			if (resolved is null || !string.Equals(Path.GetFullPath(resolved.FullName), Path.GetFullPath(recorded.SourcePath), StringComparison.OrdinalIgnoreCase))
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedLinkMismatch", "Symbolic link no longer points to the recorded source.", observedPath, recorded.NodeId));
				return issues;
			}
		}
		else if (new FileInfo(observedPath).Length != recorded.Length)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedLengthMismatch", "Deployed file length differs from activation state.", observedPath, recorded.NodeId));
			return issues;
		}
		var targetHash = await HashFileAsync(observedPath, cancellationToken).ConfigureAwait(false);
		if (!string.Equals(targetHash, recorded.ContentSha256, StringComparison.OrdinalIgnoreCase)) issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedContentMismatch", "Deployed content differs from activation state.", observedPath, recorded.NodeId));
		if (!File.Exists(recorded.SourcePath)) issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedSourceMissing", "Recorded source file no longer exists.", recorded.SourcePath, recorded.NodeId));
		else
		{
			var sourceHash = await HashFileAsync(recorded.SourcePath, cancellationToken).ConfigureAwait(false);
			if (!string.Equals(sourceHash, recorded.ContentSha256, StringComparison.OrdinalIgnoreCase)) issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedSourceChanged", "Recorded source content changed after deployment.", recorded.SourcePath, recorded.NodeId));
		}
		return issues;
	}

	private static async ValueTask<string> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
		return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
	}

	private static string ComputeGeneration(IEnumerable<(string Path, PatchFileNameInfo Info)> files, ActivationState? state)
	{
		var builder = new StringBuilder().Append(state?.ProfileId?.Value.ToString("N")).Append(':').Append(state?.ProfileRevision ?? 0).AppendLine();
		foreach (var item in files.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
		{
			var file = new FileInfo(item.Path);
			builder.Append(file.Name.ToLowerInvariant()).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks).AppendLine();
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}
}
