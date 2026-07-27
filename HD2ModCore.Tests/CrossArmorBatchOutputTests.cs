using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using System.Buffers.Binary;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationPatchEntryPayloadReader = HD2ModAdaptation.PatchReconstruction.PatchEntryPayloadReader;

// Purpose: Verifies independently rebuilt cross-armor batches form one unique final target set.
namespace HD2ModCore.Tests;

public sealed class CrossArmorBatchOutputTests
{
	[Fact]
	public void CombineBatchOutputs_RejectsDuplicateTargetUnits()
	{
		var key = new AdaptationAssetKey(0xe0a48d0be9a7453f, 1);
		var output = Output(key);

		var method = typeof(CrossArmorTransferCandidateService).GetMethod("CombineBatchOutputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [new[] { output, output }]));

		Assert.IsType<InvalidDataException>(exception.InnerException);
	}

	[Fact]
	public void CrossArmorOutputBuild_ThrowsWhenCancellationAlreadyRequested()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var operation = new CrossArmorTargetShellPatchOperation();

		Assert.Throws<OperationCanceledException>(() => operation.BuildOutput(
			Array.Empty<SdkStyleTargetShellPatchWorkItem>(),
			null,
			null,
			null,
			cancellation.Token));
	}

	[Fact]
	public void CrossArmorOutput_RejectsOverwriteExistingBeforeWriting()
	{
		var request = new CrossArmorTargetShellPatchOperationRequest(
			"source.patch",
			"output",
			[],
			[],
			[],
			false,
			null)
		{
			OverwriteExisting = true
		};

		var exception = Assert.Throws<CrossArmorOverwriteNotAllowedException>(() => request.Validate(requireWorkItems: false));

		Assert.Equal("Cross-armor candidate output must not overwrite an existing Patch.", exception.Message);
	}

	[Fact]
	public async Task CrossArmorOutput_CancellationDuringPayloadRead_PreservesPreexistingFormalFilesAndWritesMarker()
	{
		var root = Path.Combine(Path.GetTempPath(), "cross-armor-cancel-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var sourcePath = Path.Combine(root, "source.patch");
		var outputDirectory = Path.Combine(root, "output");
		Directory.CreateDirectory(outputDirectory);
		await File.WriteAllBytesAsync(sourcePath, new byte[] { 1 });
		foreach (var suffix in new[] { "", ".stream", ".gpu_resources" }) await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "source.patch" + suffix), new byte[] { 1 });
		var entry = new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(new AdaptationAssetKey(PatchUnitMeshReader.UnitTypeId, 1), sourcePath, "source.patch");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var operation = new CrossArmorTargetShellPatchOperation();
		var request = new CrossArmorTargetShellPatchOperationRequest(sourcePath, outputDirectory, Array.Empty<byte>(), Array.Empty<SdkStyleTargetShellPatchWorkItem>(), Array.Empty<PatchArchiveAdditionalEntry>(), false, null, new[] { entry });

		try
		{
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.ExecuteOutputAsync(request, new SdkStyleTargetShellPatchOutput(Array.Empty<PatchArchiveAdditionalEntry>(), Array.Empty<AdaptationAssetKey>(), Array.Empty<SdkStyleTargetShellPatchUnitResult>()), cancellation.Token));
			Assert.True(File.Exists(Path.Combine(outputDirectory, "source.patch")));
			Assert.True(File.Exists(Path.Combine(outputDirectory, "source.patch.stream")));
			Assert.True(File.Exists(Path.Combine(outputDirectory, "source.patch.gpu_resources")));
			Assert.Contains("Canceled before a complete Patch output was committed.", await File.ReadAllTextAsync(Path.Combine(outputDirectory, "cross-armor-output.canceled")));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public async Task CrossArmorOutput_StagesValidatesCommitsAndReadsBackAllThreeFiles()
	{
		var root = Path.Combine(Path.GetTempPath(), "cross-armor-staged-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sourcePath = Path.Combine(root, "source.patch");
			var outputDirectory = Path.Combine(root, "output");
			await File.WriteAllBytesAsync(sourcePath, CreateLegacyToc(new byte[] { 1, 2, 3 }));
			var scanner = new AdaptationPatchTocScanner();
			var sourceEntry = (await scanner.ScanEntriesAsync(sourcePath)).Single();
			var targetKey = new AdaptationAssetKey(PatchUnitMeshReader.UnitTypeId, 2);
			var target = new PatchArchiveAdditionalEntry(targetKey, new byte[] { 9, 8, 7 }, new byte[] { 6, 5 }, new byte[] { 4, 3 });
			var output = new SdkStyleTargetShellPatchOutput([target], [targetKey], [new SdkStyleTargetShellPatchUnitResult(targetKey, 1, 0, 1, [], [], [], [])]);
			var request = new CrossArmorTargetShellPatchOperationRequest(sourcePath, outputDirectory, await File.ReadAllBytesAsync(sourcePath), [], [], false, null, [sourceEntry]);

			var result = await new CrossArmorTargetShellPatchOperation().ExecuteOutputAsync(request, output);

			Assert.True(result.Ownership.IsCommitted);
			Assert.Equal(new[] { targetKey }, (await scanner.ScanEntriesAsync(result.WriteResult.TocFilePath)).Where(e => e.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).Select(e => e.AssetKey));
			Assert.True(File.Exists(result.WriteResult.TocFilePath));
			Assert.True(File.Exists(result.WriteResult.StreamFilePath));
			Assert.True(File.Exists(result.WriteResult.GpuResourceFilePath));
			var entry = (await scanner.ScanEntriesAsync(result.WriteResult.TocFilePath)).Single();
			var payload = await new AdaptationPatchEntryPayloadReader().ReadPayloadAsync(entry);
			Assert.Equal(new byte[] { 9, 8, 7 }, payload.TocData);
			Assert.Equal(new byte[] { 6, 5 }, payload.StreamData);
			Assert.Equal(new byte[] { 4, 3 }, payload.GpuResourceData);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public async Task CrossArmorOutput_PreCommitValidationFailureLeavesNoFormalFiles()
	{
		var root = Path.Combine(Path.GetTempPath(), "cross-armor-validation-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sourcePath = Path.Combine(root, "source.patch");
			var outputDirectory = Path.Combine(root, "output");
			await File.WriteAllBytesAsync(sourcePath, CreateLegacyToc(new byte[] { 1 }));
			var sourceEntry = (await new AdaptationPatchTocScanner().ScanEntriesAsync(sourcePath)).Single();
			var targetKey = new AdaptationAssetKey(PatchUnitMeshReader.UnitTypeId, 2);
			var output = new SdkStyleTargetShellPatchOutput([], [targetKey], [new SdkStyleTargetShellPatchUnitResult(targetKey, 1, 0, 1, [], [], [], [])]);
			var request = new CrossArmorTargetShellPatchOperationRequest(sourcePath, outputDirectory, await File.ReadAllBytesAsync(sourcePath), [], [new PatchArchiveAdditionalEntry(targetKey, [9], [], [8])], false, null, [sourceEntry])
			{
				PreCommitValidation = (_, _) => ValueTask.FromException(new InvalidDataException("test validation failure"))
			};

			await Assert.ThrowsAsync<InvalidDataException>(async () => await new CrossArmorTargetShellPatchOperation().ExecuteOutputAsync(request, output));
			Assert.False(File.Exists(Path.Combine(outputDirectory, "source.patch")));
			Assert.False(File.Exists(Path.Combine(outputDirectory, "source.patch.stream")));
			Assert.False(File.Exists(Path.Combine(outputDirectory, "source.patch.gpu_resources")));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public async Task CrossArmorOutput_RejectsAnyPreexistingFormalFileWithoutChangingIt()
	{
		var root = Path.Combine(Path.GetTempPath(), "cross-armor-existing-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sourcePath = Path.Combine(root, "source.patch");
			var outputDirectory = Path.Combine(root, "output");
			Directory.CreateDirectory(outputDirectory);
			await File.WriteAllBytesAsync(sourcePath, CreateLegacyToc([1]));
			var existingFiles = new Dictionary<string, byte[]>
			{
				[Path.Combine(outputDirectory, "source.patch")] = [0x10, 0x11],
				[Path.Combine(outputDirectory, "source.patch.stream")] = [0xaa, 0xbb],
				[Path.Combine(outputDirectory, "source.patch.gpu_resources")] = [0xcc, 0xdd]
			};
			foreach (var pair in existingFiles) await File.WriteAllBytesAsync(pair.Key, pair.Value);
			var entry = (await new AdaptationPatchTocScanner().ScanEntriesAsync(sourcePath)).Single();
			var request = new CrossArmorTargetShellPatchOperationRequest(sourcePath, outputDirectory, await File.ReadAllBytesAsync(sourcePath), [], [], false, null, [entry]);

			await Assert.ThrowsAsync<CrossArmorOutputAlreadyExistsException>(async () => await new CrossArmorTargetShellPatchOperation().ExecuteOutputAsync(request, new SdkStyleTargetShellPatchOutput([], [], [])));
			foreach (var pair in existingFiles) Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public void CrossArmorCandidateResultPresenter_KeepsCommittedCancelAndFailureOutcomesSuccessful()
	{
		var committedCancel = new CrossArmorTransferCandidateResult(true, "output", null, 1, 0, 0, []) { IsCommitted = true, HasWarnings = true };
		var committedFailure = new CrossArmorTransferCandidateResult(true, "output", null, 1, 0, 0, []) { IsCommitted = true, HasWarnings = true };
		var ordinaryFailure = new CrossArmorTransferCandidateResult(false, "output", null, 0, 0, 0, []);

		Assert.False(CrossArmorCandidateResultPresenter.Map(committedCancel).IsFailure);
		Assert.False(CrossArmorCandidateResultPresenter.Map(committedFailure).IsFailure);
		Assert.True(CrossArmorCandidateResultPresenter.Map(committedCancel).IsWarning);
		Assert.Equal("候选已提交，但报告不完整/有告警", CrossArmorCandidateResultPresenter.Map(committedFailure).StatusText);
		Assert.True(CrossArmorCandidateResultPresenter.Map(ordinaryFailure).IsFailure);
		Assert.DoesNotContain("取消", CrossArmorCandidateResultPresenter.Map(committedCancel).StatusText);
		Assert.DoesNotContain("失败", CrossArmorCandidateResultPresenter.Map(committedFailure).StatusText);
	}

	[Fact]
	public async Task CrossArmorOutput_UsesStrictStagedOrder()
	{
		var root = Path.Combine(Path.GetTempPath(), "cross-armor-order-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sourcePath = Path.Combine(root, "source.patch");
			var outputDirectory = Path.Combine(root, "output");
			await File.WriteAllBytesAsync(sourcePath, [1]);
			var order = new List<string>();
			var writer = new RecordingWriter(order);
			var operation = new CrossArmorTargetShellPatchOperation(stagedWriter: writer, stagedVerifier: new RecordingVerifier(order), stagedCommitter: new RecordingCommitter(order));
			var request = new CrossArmorTargetShellPatchOperationRequest(sourcePath, outputDirectory, [], [], [], false, null, [new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(new AdaptationAssetKey(99, 1), sourcePath, "source.patch")])
			{
				PreCommitValidation = (_, _) => { order.Add("precommit validation"); return ValueTask.CompletedTask; }
			};

			await operation.ExecuteOutputAsync(request, new SdkStyleTargetShellPatchOutput([], [], []));

			Assert.Equal(new[] { "staging write", "verifier", "precommit validation", "sidecar publish", "TOC publish" }, order);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public async Task CrossArmorOutput_WhenTocPublishFails_CleansOnlyThisOperationFiles()
	{
		var root = Path.Combine(Path.GetTempPath(), "cross-armor-publish-failure-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sourcePath = Path.Combine(root, "source.patch");
			var outputDirectory = Path.Combine(root, "output");
			await File.WriteAllBytesAsync(sourcePath, [1]);
			Directory.CreateDirectory(outputDirectory);
			var writer = new RecordingWriter([]);
			var original = new byte[] { 0xf0, 0x0d };
			var operation = new CrossArmorTargetShellPatchOperation(stagedWriter: writer, stagedVerifier: new RecordingVerifier([]), stagedCommitter: new CollisionCommitter(original));
			var request = new CrossArmorTargetShellPatchOperationRequest(sourcePath, outputDirectory, [], [], [], false, null, [new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(new AdaptationAssetKey(99, 1), sourcePath, "source.patch")]);

			await Assert.ThrowsAsync<IOException>(async () => await operation.ExecuteOutputAsync(request, new SdkStyleTargetShellPatchOutput([], [], [])));

			Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(outputDirectory, "source.patch")));
			Assert.False(File.Exists(Path.Combine(outputDirectory, "source.patch.stream")));
			Assert.False(File.Exists(Path.Combine(outputDirectory, "source.patch.gpu_resources")));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	private sealed class RecordingWriter(List<string> order) : ICrossArmorStagedWriter
	{
		public ValueTask<PatchArchiveFileWriteResult> WriteAsync(string source, string staging, IReadOnlyCollection<PatchUnitMeshEditResult> edits, IReadOnlyCollection<PatchArchiveAdditionalEntry> additions, IReadOnlyCollection<HD2ModAdaptation.PatchReconstruction.PatchTocEntry> removals, bool preserve, byte[] header, bool overwrite, CancellationToken cancellationToken)
		{
			order.Add("staging write"); Directory.CreateDirectory(staging);
			var toc = Path.Combine(staging, Path.GetFileName(source));
			File.WriteAllBytes(toc, [1]); File.WriteAllBytes(toc + ".stream", [2]); File.WriteAllBytes(toc + ".gpu_resources", [3]);
			return ValueTask.FromResult(new PatchArchiveFileWriteResult(staging, toc, toc + ".stream", toc + ".gpu_resources", 1, 1, 1));
		}
	}

	private sealed class RecordingVerifier(List<string> order) : ICrossArmorStagedVerifier
	{
		public ValueTask VerifyAsync(string tocPath, IReadOnlySet<AdaptationAssetKey> expectedUnits, CancellationToken cancellationToken) { order.Add("verifier"); return ValueTask.CompletedTask; }
	}

	private sealed class RecordingCommitter(List<string> order) : ICrossArmorStagedCommitter
	{
		public PatchArchiveFileWriteResult Commit(CrossArmorOutputOwnership ownership, PatchArchiveFileWriteResult staged) { order.Add("sidecar publish"); order.Add("TOC publish"); return ownership.Commit(staged); }
	}

	private sealed class CollisionCommitter(byte[] original) : ICrossArmorStagedCommitter
	{
		public PatchArchiveFileWriteResult Commit(CrossArmorOutputOwnership ownership, PatchArchiveFileWriteResult staged)
		{
			File.Move(Path.Combine(ownership.StagingDirectory, "source.patch.stream"), Path.Combine(ownership.OutputDirectory, "source.patch.stream"));
			File.Move(Path.Combine(ownership.StagingDirectory, "source.patch.gpu_resources"), Path.Combine(ownership.OutputDirectory, "source.patch.gpu_resources"));
			File.WriteAllBytes(Path.Combine(ownership.OutputDirectory, "source.patch"), original);
			File.Delete(Path.Combine(ownership.OutputDirectory, "source.patch.stream"));
			File.Delete(Path.Combine(ownership.OutputDirectory, "source.patch.gpu_resources"));
			throw new IOException("simulated TOC publish failure");
		}
	}

	[Fact]
	public void TargetGroups_IncludeUnitsWithOnlyHiddenMappings()
	{
		var hiddenTarget = new AdaptationAssetKey(0xe0a48d0be9a7453f, 3);
		var mappings = new[]
		{
			new CrossArmorTransferMapping(
				new CrossArmorPhysicalTargetKey(new HD2ModCore.Domain.AssetKey(hiddenTarget.TypeId, hiddenTarget.FileId), 4),
				null!,
				null,
				false,
				"隐藏",
				Array.Empty<string>(),
				Array.Empty<string>(),
				false,
				false)
		};

		var groups = mappings.GroupBy(mapping => mapping.PhysicalTarget.UnitAssetKey).ToArray();

		Assert.Single(groups);
		Assert.Equal(new HD2ModCore.Domain.AssetKey(hiddenTarget.TypeId, hiddenTarget.FileId), groups[0].Key);
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_MultipleApprovedMappings_DoesNotInferAdditionalFamilyMembers()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, 0), (1, 1), (2, 2));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 1), (2, 2)));
		var approved = new[]
		{
			new TargetShellMeshMapping(sourceKey, 0, 0),
			new TargetShellMeshMapping(sourceKey, 1, 1)
		};

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Equal(approved, result);
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_RepresentativeMinusOne_UsesMatchingSourceLods()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 4), (2, 3), (3, 2), (4, 1), (5, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 5) };

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Collection(result,
			mapping => Assert.Equal((4, 5), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((1, 2), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((2, 3), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((3, 4), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)));
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_MissingSourceLod_FallsBackToSourceLod0()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 1), (2, 3)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 4) };

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Collection(result,
			mapping => Assert.Equal((0, 4), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((2, 1), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((0, 2), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((1, 3), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)));
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_NonUniqueSourceLod0_RemainsConservative()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 0)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 4) };

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Equal(approved, result);
	}

	private static IReadOnlyList<TargetShellMeshMapping> Expand(UnitMeshModel target, IReadOnlyDictionary<AdaptationAssetKey, PatchUnitMesh> sources, IReadOnlyList<TargetShellMeshMapping> approved)
	{
		var method = typeof(CrossArmorTransferCandidateService).GetMethod("ExpandCompleteLodFamilyMappings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		return (IReadOnlyList<TargetShellMeshMapping>)method.Invoke(null, [target, sources, approved])!;
	}

	private static PatchUnitMesh CreatePatchUnit(AdaptationAssetKey key, UnitMeshModel model)
		=> new(new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(key, "test.patch", "test.patch"), new HD2ModAdaptation.PatchReconstruction.PatchEntryPayload(new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(key, "test.patch", "test.patch"), [], [], []), model);

	private static UnitMeshModel CreateModel(params (int MeshInfoIndex, int LodIndex)[] specs)
	{
		var stream = new UnitStreamInfo(0, 0, 0, 0, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0, []);
		var meshes = specs.Select(spec => new UnitMeshInfo(spec.MeshInfoIndex, 0, (uint)spec.MeshInfoIndex, spec.LodIndex, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(spec.LodIndex, spec.MeshInfoIndex), [0], [new UnitMeshSectionInfo(0, 0, 0, 0, 3, 0, 3, 0)])).ToArray();
		var rawMeshes = specs.Select(spec => new UnitRawMeshData(spec.MeshInfoIndex, (uint)spec.MeshInfoIndex, spec.LodIndex, 0, [new UnitRawMeshSectionData(0, 0, [new UnitTriangleIndices(0, 1, 2)])], [new UnitTriangleIndices(0, 1, 2)], [])).ToArray();
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [stream], meshes, [], [], rawMeshes);
	}

	private static SdkStyleTargetShellPatchOutput Output(AdaptationAssetKey target)
		=> new([], [], [new SdkStyleTargetShellPatchUnitResult(target, 1, 0, 1, [], [], [], [])]);

	private static byte[] CreateLegacyToc(byte[] payload)
	{
		const int typeOffset = 60;
		const int entryOffset = typeOffset + 32;
		const int payloadOffset = entryOffset + 80;
		var data = new byte[payloadOffset + payload.Length];
		Write32(data, 0, 4026531857); Write32(data, 4, 1); Write32(data, 8, 1);
		Write64(data, typeOffset + 8, PatchUnitMeshReader.UnitTypeId); Write64(data, typeOffset + 16, 1);
		Write64(data, entryOffset, 1); Write64(data, entryOffset + 8, PatchUnitMeshReader.UnitTypeId); Write64(data, entryOffset + 16, payloadOffset);
		Write32(data, entryOffset + 56, (uint)payload.Length); Write32(data, entryOffset + 76, 1);
		payload.CopyTo(data, payloadOffset);
		return data;
	}

	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
}