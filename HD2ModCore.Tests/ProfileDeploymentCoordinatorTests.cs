using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies immediate deployment is coalesced and serialized without buffering.
public sealed class ProfileDeploymentCoordinatorTests
{
	[Fact]
	public async Task NotifyActiveProfileChanged_DeploysImmediately()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-deployment-coordinator-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var profile = new Profile(ProfileId.New(), "Active", DateTimeOffset.UtcNow, null, Array.Empty<ProfileEntry>(), Revision: 1);
			var library = new FakeLibraryManager(new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode>(), [profile], profile.Id));
			var apply = new RecordingApplyService();
			var executor = new RecordingExecutor();
			await using var coordinator = new ProfileDeploymentCoordinator(library, apply, executor, new StoragePaths(root), () => root);

			coordinator.NotifyActiveProfileChanged();
			await apply.WaitForCallAsync();

			Assert.Equal(1, apply.CallCount);
			Assert.Equal(1, apply.LastProfile!.Revision);
			Assert.Equal(0, executor.DeactivateCount);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task DeactivateAsync_CancelsPendingBufferAndUsesOnlySerializedDeactivate()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-deployment-coordinator-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var profile = new Profile(ProfileId.New(), "Active", DateTimeOffset.UtcNow, null, Array.Empty<ProfileEntry>());
			var library = new FakeLibraryManager(new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode>(), [profile], profile.Id));
			var apply = new RecordingApplyService();
			var executor = new RecordingExecutor();
			await using var coordinator = new ProfileDeploymentCoordinator(library, apply, executor, new StoragePaths(root), () => root);
			await coordinator.DeactivateAsync();

			Assert.Equal(0, apply.CallCount);
			Assert.Equal(1, executor.DeactivateCount);
			Assert.Equal(ProfileDeploymentStage.Completed, coordinator.Status.Stage);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	private sealed class RecordingApplyService : IProfileApplyService
	{
		private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public int CallCount { get; private set; }
		public Profile? LastProfile { get; private set; }
		public ValueTask<ApplyResult> ApplyAsync(Profile profile, LibrarySnapshot snapshot, string modsRootDirectory, string gameDataDirectory, CancellationToken cancellationToken = default)
		{
			CallCount++;
			LastProfile = profile;
			_called.TrySetResult();
			return ValueTask.FromResult(new ApplyResult(true, [], null, []));
		}
		public Task WaitForCallAsync() => _called.Task;
	}

	private sealed class RecordingExecutor : IApplyExecutor
	{
		public int DeactivateCount { get; private set; }
		public ValueTask<ApplyResult> ExecuteAsync(ApplyPlan plan, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ApplyResult(true, [], null, []));
		public ValueTask<ApplyResult> DeactivateAsync(string gameDataDirectory, CancellationToken cancellationToken = default)
		{
			DeactivateCount++;
			return ValueTask.FromResult(new ApplyResult(true, [], null, []));
		}
	}

	private sealed class FakeLibraryManager : IModLibraryManager
	{
		public LibrarySnapshot Snapshot;
		public FakeLibraryManager(LibrarySnapshot snapshot) => Snapshot = snapshot;
		public ValueTask<LibrarySnapshot> LoadOrCreateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> DeleteNodeAsync(ModNodeId nodeId, bool deleteStoredFiles, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> UpsertProfileAsync(Profile profile, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> DeleteProfileAsync(ProfileId profileId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> RenameProfileAsync(ProfileId profileId, string newName, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> SetActiveProfileAsync(ProfileId? profileId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> AddProfileEntryAsync(ProfileId profileId, ModNodeId nodeId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> RemoveProfileEntryAsync(ProfileId profileId, ModNodeId nodeId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> MoveProfileEntryAsync(ProfileId profileId, ModNodeId nodeId, int direction, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
		public ValueTask<LibrarySnapshot> UpdateNodeMetadataAsync(ModNodeId nodeId, ModNodeMetadata metadata, CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
	}
}
