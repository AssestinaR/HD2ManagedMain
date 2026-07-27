using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using System.Text.Json;
using CoreAssetKey = HD2ModCore.Domain.AssetKey;
using CoreGameDataStreamComponentFact = HD2ModCore.Domain.GameDataStreamComponentFact;
using CoreGameDataStreamLayoutFact = HD2ModCore.Domain.GameDataStreamLayoutFact;
using CorePatchTocEntry = HD2ModCore.Domain.PatchTocEntry;

namespace HD2ModCore.Tests;

// Purpose: Exercises the executable Same-key orchestration seam, including terminal events and owned-output cleanup.
public sealed class ModSameKeyReconstructionServiceTests
{
	[Fact]
	public async Task InspectSuccess_ReportsOneCompletedTerminalEventInOrder()
	{
		using var fixture = Fixture.Create();
		var events = new List<OperationProgressEvent>();
		var result = await fixture.Service.InspectAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.True(result.IsGameDataIndexCurrent);
		AssertTerminal(events, OperationState.Completed);
		AssertStageOrder(events, "InspectEligibility", "LoadFacts", "Finalize");
		AssertStrictSequence(events);
	}

	[Fact]
	public async Task InspectFailure_ReportsExactlyOneFailedTerminalEvent()
	{
		using var fixture = Fixture.Create(planException: new InvalidDataException("plan failed"));
		var events = new List<OperationProgressEvent>();

		var result = await fixture.Service.InspectAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.False(result.IsGameDataIndexCurrent);
		Assert.Contains(result.Issues, issue => issue.Code == "SameKeyInspectFailed");
		AssertTerminal(events, OperationState.Failed);
	}

	[Fact]
	public async Task GenerateSuccess_ReportsStagesAndWritesReportStatistics()
	{
		using var fixture = Fixture.Create(patchCount: 2, operation: new RecordingOperation());
		var events = new List<OperationProgressEvent>();
		var result = await fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.True(result.IsSuccessful);
		Assert.Equal(2, result.OutputUnitCount);
		Assert.NotNull(result.ReportJsonPath);
		using var report = JsonDocument.Parse(File.ReadAllText(result.ReportJsonPath!));
		Assert.Equal(2, report.RootElement.GetProperty("PatchCount").GetInt32());
		Assert.Equal(2, report.RootElement.GetProperty("Outputs").GetArrayLength());
		Assert.Equal(2, result.OutputUnitCount);
		Assert.Equal(2, result.ReplacementMeshCount);
		AssertStageOrder(events, "InspectEligibility", "Plan", "LoadFacts", "BuildCandidate", "WriteCandidate", "ValidateCandidate", "Finalize");
		AssertTerminal(events, OperationState.Completed);
		AssertStrictSequence(events);
		Assert.True(events.FindLastIndex(eventItem => eventItem.StageId == "Finalize") > events.FindLastIndex(eventItem => eventItem.StageId == "ValidateCandidate"));
	}

	[Fact]
	public async Task GenerateTwoPatches_AggregatesBuildCandidateTotalAndCompletedMonotonically()
	{
		using var fixture = Fixture.Create(patchCount: 2, operation: new RecordingOperation());
		var events = new List<OperationProgressEvent>();

		var result = await fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.True(result.IsSuccessful);
		var build = events.Where(item => item.StageId == "BuildCandidate").ToArray();
		Assert.NotEmpty(build);
		Assert.All(build, item => Assert.Equal(2, item.Total));
		Assert.Equal(new long[] { 0, 1, 1, 2 }, build.Select(item => item.Completed));
		Assert.True(build.Zip(build.Skip(1), (first, second) => second.Completed >= first.Completed).All(value => value));
	}

	[Fact]
	public async Task GeneratePrecheckFailure_ReportsOnlyFailedTerminalEvent()
	{
		using var fixture = Fixture.Create();
		var events = new List<OperationProgressEvent>();
		var result = await fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, string.Empty, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.False(result.IsSuccessful);
		AssertTerminal(events, OperationState.Failed);
		Assert.Contains(result.Issues, issue => issue.Code == "OutputDirectoryMissing");
	}

	[Fact]
	public async Task GeneratePlanFailure_ReportsExactlyOneFailedTerminalEvent()
	{
		using var fixture = Fixture.Create(planException: new InvalidDataException("plan failed"));
		var events = new List<OperationProgressEvent>();

		var result = await fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.False(result.IsSuccessful);
		Assert.Contains(result.Issues, issue => issue.Code == "SameKeyPlanFailed");
		AssertTerminal(events, OperationState.Failed);
	}

	[Fact]
	public async Task GenerateValidationFailure_CleansOnlyOwnedOutputAndReportsOnce()
	{
		using var fixture = Fixture.Create(operation: new RecordingOperation { ThrowOnValidation = true });
		var other = Path.Combine(fixture.OutputRoot, "other-operation");
		Directory.CreateDirectory(other);
		File.WriteAllText(Path.Combine(other, ".same-key-owner"), "other");
		File.WriteAllText(Path.Combine(other, "keep.txt"), "keep");
		var events = new List<OperationProgressEvent>();

		var result = await fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.False(result.IsSuccessful);
		AssertTerminal(events, OperationState.Failed);
		Assert.True(File.Exists(Path.Combine(other, "keep.txt")));
		Assert.True(File.Exists(Path.Combine(other, ".same-key-owner")));
		Assert.DoesNotContain(Directory.EnumerateDirectories(fixture.OutputRoot), directory => directory != other && File.Exists(Path.Combine(directory, ".same-key-owner")));
	}

	[Fact]
	public async Task InspectCancellation_ReportsOnlyCanceledTerminalEvent()
	{
		using var fixture = Fixture.Create();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var events = new List<OperationProgressEvent>();

		await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.InspectAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, cancellation.Token, new ImmediateProgress(events), fixture.OperationId).AsTask());

		AssertTerminal(events, OperationState.Canceled);
	}

	[Fact]
	public async Task GenerateCancellation_CleansOnlyOwnedOutputAndReportsOnce()
	{
		var operation = new RecordingOperation { CancelDuringExecution = true };
		using var fixture = Fixture.Create(operation: operation);
		var untouched = Path.Combine(fixture.OutputRoot, "not-owned");
		Directory.CreateDirectory(untouched);
		File.WriteAllText(Path.Combine(untouched, "keep.txt"), "keep");
		var events = new List<OperationProgressEvent>();

		await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId).AsTask());

		AssertTerminal(events, OperationState.Canceled);
		Assert.True(Directory.Exists(untouched));
		Assert.Equal("keep", File.ReadAllText(Path.Combine(untouched, "keep.txt")));
		Assert.DoesNotContain(Directory.EnumerateDirectories(fixture.OutputRoot), directory => File.Exists(Path.Combine(directory, ".same-key-owner")));
		AssertStrictSequence(events);
	}

	[Fact]
	public async Task GenerateCancellationDuringBuild_ReportsBuildProgressAndCleansOwnedOutput()
	{
		using var cancellation = new CancellationTokenSource();
		var operation = new RecordingOperation { DuringBuild = cancellation.Cancel };
		using var fixture = Fixture.Create(operation: operation);
		var events = new List<OperationProgressEvent>();

		await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, cancellation.Token, new ImmediateProgress(events), fixture.OperationId).AsTask());

		var build = events.Where(item => item.StageId == "BuildCandidate").ToArray();
		Assert.Contains(build, item => item.Completed == 0 && item.Total == 1);
		AssertTerminal(events, OperationState.Canceled);
		AssertStrictSequence(events);
	}

	[Fact]
	public async Task GenerateFailure_CleansOwnedDirectoryButPreservesOtherOperationDirectory()
	{
		using var fixture = Fixture.Create(operation: new RecordingOperation { ThrowOnExecute = true });
		var other = Path.Combine(fixture.OutputRoot, "other-operation");
		Directory.CreateDirectory(other);
		File.WriteAllText(Path.Combine(other, "keep.txt"), "keep");
		var events = new List<OperationProgressEvent>();

		var result = await fixture.Service.GenerateCandidateAsync(fixture.Node, fixture.ModsRoot, fixture.GameDataRoot, fixture.OutputRoot, progress: new ImmediateProgress(events), operationId: fixture.OperationId);

		Assert.False(result.IsSuccessful);
		AssertTerminal(events, OperationState.Failed);
		Assert.True(File.Exists(Path.Combine(other, "keep.txt")));
		Assert.DoesNotContain(Directory.EnumerateDirectories(fixture.OutputRoot), directory => File.Exists(Path.Combine(directory, ".same-key-owner")));
	}

	private static void AssertTerminal(IReadOnlyList<OperationProgressEvent> events, OperationState state)
	{
		var terminals = events.Where(eventItem => eventItem.IsTerminal).ToArray();
		Assert.Single(terminals);
		Assert.Equal(state, terminals[0].State);
		AssertStrictSequence(events);
	}

	private static void AssertStrictSequence(IReadOnlyList<OperationProgressEvent> events)
	{
		Assert.Equal(events.Count, events.Select(item => item.Sequence).Distinct().Count());
		Assert.True(events.Zip(events.Skip(1), (first, second) => second.Sequence > first.Sequence).All(value => value));
	}

	private static void AssertStageOrder(IReadOnlyList<OperationProgressEvent> events, params string[] stages)
	{
		var actual = events.Select(eventItem => eventItem.StageId).Where(stage => stage is not null).ToArray();
		var cursor = -1;
		foreach (var stage in stages)
		{
			var next = Array.IndexOf(actual, stage, cursor + 1);
			Assert.True(next > cursor, $"stage {stage} was not observed after index {cursor}");
			cursor = next;
		}
	}

	private sealed class ImmediateProgress(List<OperationProgressEvent> events) : IProgress<OperationProgressEvent>
	{
		public void Report(OperationProgressEvent value) => events.Add(value);
	}

	private sealed class Fixture : IDisposable
	{
		public string ModsRoot { get; }
		public string GameDataRoot { get; }
		public string OutputRoot { get; }
		public ModNode Node { get; }
		public Guid OperationId { get; } = Guid.NewGuid();
		public ModSameKeyReconstructionService Service { get; }
		private Fixture(string modsRoot, string gameDataRoot, string outputRoot, ModNode node, ModSameKeyReconstructionService service)
			=> (ModsRoot, GameDataRoot, OutputRoot, Node, Service) = (modsRoot, gameDataRoot, outputRoot, node, service);

		public static Fixture Create(int patchCount = 1, ISameKeyTargetShellReconstructionOperation? operation = null, Exception? planException = null)
		{
			var root = Path.Combine(Path.GetTempPath(), "same-key-service-" + Guid.NewGuid().ToString("N"));
			var mods = Path.Combine(root, "mods");
			var game = Path.Combine(root, "game");
			var output = Path.Combine(root, "output");
			var node = new ModNode(ModNodeId.New(), "mod", new ModNodeMetadata("test", null, DateTimeOffset.UtcNow, null), [], []);
			Directory.CreateDirectory(Path.Combine(mods, node.RelativePath));
			Directory.CreateDirectory(game);
			for (var index = 0; index < patchCount; index++) File.WriteAllBytes(Path.Combine(mods, node.RelativePath, $"1234567890abcdef_patch_{index}.toc"), [1]);
			var key = new CoreAssetKey(1, 2);
			var adaptation = new UnitMeshAdaptationPlan(new UnitMeshAdaptationIntent(new CorePatchTocEntry(key, "source", "source"), "target", null), true,
				[], [new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.ReplaceWithSource, 0, 0, "test", null)], "test");
			var parser = new FakeParser();
			var planning = new FakePlanning(key, adaptation, planException);
			var analyses = Enumerable.Range(0, patchCount).Select(index => new PatchGroupAnalysis(new PatchGroupInput(Path.Combine(mods, node.RelativePath, $"1234567890abcdef_patch_{index}.toc")), [], [], [], DateTimeOffset.UtcNow, "test", EntryCatalog: [new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(new HD2ModAdaptation.PatchReconstruction.AssetKey(key.TypeId, key.FileId), "source", "source")])).ToArray();
			var service = new ModSameKeyReconstructionService(parser, planning, new FakeIndex(), new FakeHashes(), new FakeAnalysis(analyses), operation);
			return new Fixture(mods, game, output, node, service);
		}
		public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(OutputRoot)!, true); } catch { } }
	}

	private sealed class FakeParser : IPatchFileNameParser
	{
		public bool TryParse(string fileName, out PatchFileNameInfo? info) { info = new PatchFileNameInfo("1234567890abcdef", 0, PatchSidecarKind.Base, fileName); return fileName.EndsWith(".toc", StringComparison.OrdinalIgnoreCase); }
		public PatchFileNameInfo Parse(string fileName) => new("1234567890abcdef", 0, PatchSidecarKind.Base, fileName);
	}
	private sealed class FakeHashes : IArchiveHashesProvider { public ValueTask<string> GetArchiveHashesJsonAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("{}"); }
	private sealed class FakeIndex : IAssetArchiveIndexService
	{
		public ValueTask<GameDataIndexStatus> GetIndexStatusAsync(string gameDataDirectory, string archiveHashesJson, CancellationToken cancellationToken = default) => ValueTask.FromResult(new GameDataIndexStatus(GameDataIndexState.Current, null, gameDataDirectory, "current"));
		public ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
		public ValueTask<GameDataIndexFingerprint?> GetFingerprintAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataIndexFingerprint?>(null);
		public ValueTask<IReadOnlyList<GameDataArchiveSummary>> GetArchiveSummariesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<GameDataArchiveSummary>>([]);
		public ValueTask<GameDataArchiveDetails?> GetArchiveDetailsAsync(string packageName, CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataArchiveDetails?>(null);
		public ValueTask<IReadOnlyDictionary<CoreAssetKey, IReadOnlyList<GameDataUnitPartFact>>> GetUnitPartFactsAsync(IReadOnlySet<CoreAssetKey> unitAssetKeys, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyDictionary<CoreAssetKey, IReadOnlyList<GameDataUnitPartFact>>>(new Dictionary<CoreAssetKey, IReadOnlyList<GameDataUnitPartFact>>());
		public ValueTask<IReadOnlyList<CoreGameDataStreamLayoutFact>> FindStreamLayoutsAsync(IReadOnlyList<CoreGameDataStreamComponentFact> components, uint vertexStride, bool requireSkinned = false, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<CoreGameDataStreamLayoutFact>>([]);
		public ValueTask<IReadOnlyList<CoreGameDataStreamLayoutFact>> GetStreamLayoutsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<CoreGameDataStreamLayoutFact>>([]);
		public ValueTask BuildOrRebuildAsync(string gameDataDirectory, string archiveHashesJson, IProgress<IndexBuildProgress>? progress = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
		public ValueTask<IReadOnlyList<AssetArchiveMatch>> FindAssetArchivesAsync(IReadOnlySet<CoreAssetKey> assetKeys, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<AssetArchiveMatch>>([]);
		public ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(IReadOnlySet<CoreAssetKey> assetKeys, IndexFilterSettings filterSettings, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
	}
	private sealed class FakePlanning(CoreAssetKey key, UnitMeshAdaptationPlan adaptation, Exception? planException) : ISameKeyReconstructionPlanningService
	{
		public ValueTask<SameKeyReconstructionPlan> CreatePlanAsync(SameKeyReconstructionRequest request, CancellationToken cancellationToken = default, IProgress<SameKeyPlanningProgress>? progress = null)
		{
			if (planException is not null) throw planException;
			var entry = request.PreparedSourceEntries!.Single();
			var unit = new SameKeyUnitReconstructionPlan(key, entry, new ArchiveMetadata("target", "type", "name"), [], adaptation, [], [], 1, 1);
			return ValueTask.FromResult(new SameKeyReconstructionPlan(request, [unit], []));
		}
	}
	private sealed class FakeAnalysis(IReadOnlyList<PatchGroupAnalysis> analyses) : IAdvancedModAnalysisService
	{
		public ValueTask<IReadOnlyList<PatchGroupAnalysis>> GetRequiredAnalysesAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(analyses);
		public ValueTask<AdvancedModAnalysisState> GetStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public ValueTask<AdvancedModAnalysisState> GetCachedStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
		public ValueTask<AdvancedModAnalysisState> AnalyzeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	}
	private sealed class RecordingOperation : ISameKeyTargetShellReconstructionOperation
	{
		public bool CancelDuringExecution { get; init; }
		public bool ThrowOnExecute { get; init; }
		public bool ThrowOnValidation { get; init; }
		public Action? DuringBuild { get; init; }
		public async ValueTask<SameKeyTargetShellReconstructionResult> ExecuteAsync(SameKeyTargetShellReconstructionRequest request, CancellationToken cancellationToken = default)
		{
			request.Progress?.Invoke("LoadFacts", 0, request.Units.Count);
			request.Progress?.Invoke("BuildCandidate", 0, request.Units.Count);
			if (CancelDuringExecution) { await Task.Yield(); throw new OperationCanceledException(cancellationToken); }
			DuringBuild?.Invoke();
			cancellationToken.ThrowIfCancellationRequested();
			if (ThrowOnExecute) throw new InvalidDataException("write failed");
			request.Progress?.Invoke("BuildCandidate", request.Units.Count, request.Units.Count);
			request.Progress?.Invoke("WriteCandidate", 1, 1);
			request.Progress?.Invoke("ValidateCandidate", 1, 1);
			if (ThrowOnValidation) throw new InvalidDataException("validation failed");
			return new SameKeyTargetShellReconstructionResult(new PatchArchiveFileWriteResult(request.OutputDirectory, Path.Combine(request.OutputDirectory, "x.toc"), Path.Combine(request.OutputDirectory, "x.stream"), Path.Combine(request.OutputDirectory, "x.gpu_resources"), 1, 1, 1), request.Units.Count, request.Units.Count, 0, request.Units.Count, 0);
		}
	}
}

