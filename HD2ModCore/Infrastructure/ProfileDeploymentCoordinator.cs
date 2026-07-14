using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Runs one immediate serialized deployment lane and reloads the latest active profile before every apply.
public sealed class ProfileDeploymentCoordinator : IProfileDeploymentCoordinator
{
	private readonly IModLibraryManager _libraryManager;
	private readonly IProfileApplyService _profileApplyService;
	private readonly IApplyExecutor _applyExecutor;
	private readonly StoragePaths _paths;
	private readonly Func<string?> _gameDataDirectoryProvider;
	private readonly SemaphoreSlim _lane = new(1, 1);
	private readonly object _sync = new();
	private Task? _worker;
	private bool _deactivationRequested;
	private long _requestedRevision;
	private long _handledRevision;
	private ProfileDeploymentStatus _status = ProfileDeploymentStatus.Idle;

	public ProfileDeploymentCoordinator(
		IModLibraryManager libraryManager,
		IProfileApplyService profileApplyService,
		IApplyExecutor applyExecutor,
		StoragePaths paths,
		Func<string?> gameDataDirectoryProvider,
		IDeploymentDelay? delay = null,
		TimeSpan? bufferDuration = null)
	{
		_libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
		_profileApplyService = profileApplyService ?? throw new ArgumentNullException(nameof(profileApplyService));
		_applyExecutor = applyExecutor ?? throw new ArgumentNullException(nameof(applyExecutor));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_gameDataDirectoryProvider = gameDataDirectoryProvider ?? throw new ArgumentNullException(nameof(gameDataDirectoryProvider));
	}

	public ProfileDeploymentStatus Status { get { lock (_sync) return _status; } }
	public event EventHandler<ProfileDeploymentStatus>? StatusChanged;

	public void NotifyActiveProfileChanged()
	{
		lock (_sync)
		{
			if (_deactivationRequested) return;
			_requestedRevision++;
			if (_worker is null || _worker.IsCompleted) _worker = RunAsync();
		}
	}

	public async Task DeactivateAsync(CancellationToken cancellationToken = default)
	{
		Task? worker;
		lock (_sync)
		{
			_deactivationRequested = true;
			_requestedRevision++;
			worker = _worker;
		}
		if (worker is not null)
		{
			try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
		}
		await _lane.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			SetStatus(new ProfileDeploymentStatus(ProfileDeploymentStage.Deactivating, null, 0, null, null, "正在清理 Data 中的 Patch。", null));
			var gameData = _gameDataDirectoryProvider();
			if (string.IsNullOrWhiteSpace(gameData))
			{
				SetStatus(new ProfileDeploymentStatus(ProfileDeploymentStage.Failed, null, 0, null, null, "未设置游戏数据目录。", null));
				return;
			}
			var result = await _applyExecutor.DeactivateAsync(gameData, CancellationToken.None).ConfigureAwait(false);
			SetStatus(new ProfileDeploymentStatus(result.Success ? ProfileDeploymentStage.Completed : ProfileDeploymentStage.Failed, null, 0, null, null, result.Success ? "已停用活动配置并清理 Patch。" : "停用活动配置失败。", result));
		}
		finally
		{
			_lane.Release();
			lock (_sync)
			{
				_deactivationRequested = false;
				_handledRevision = _requestedRevision;
				_worker = null;
			}
		}
	}

	private async Task RunAsync()
	{
		await _lane.WaitAsync().ConfigureAwait(false);
		try
		{
			while (true)
			{
				long targetRevision;
				lock (_sync)
				{
					if (_deactivationRequested || _handledRevision == _requestedRevision) return;
					targetRevision = _requestedRevision;
				}

				var snapshot = await _libraryManager.LoadOrCreateAsync().ConfigureAwait(false);
				var profile = snapshot.ActiveProfileId is { } activeId ? snapshot.Profiles.FirstOrDefault(item => item.Id == activeId) : null;
				if (profile is null)
				{
					lock (_sync) _handledRevision = targetRevision;
					SetStatus(ProfileDeploymentStatus.Idle);
					continue;
				}

				lock (_sync)
				{
					if (_deactivationRequested) return;
				}
				var gameData = _gameDataDirectoryProvider();
				if (string.IsNullOrWhiteSpace(gameData))
				{
					lock (_sync) _handledRevision = targetRevision;
					SetStatus(new ProfileDeploymentStatus(ProfileDeploymentStage.Failed, profile.Id, targetRevision, null, null, "未设置游戏数据目录。", null));
					continue;
				}

				// Reload immediately before planning so notifications never capture an obsolete profile snapshot.
				snapshot = await _libraryManager.LoadOrCreateAsync().ConfigureAwait(false);
				profile = snapshot.ActiveProfileId is { } latestActiveId ? snapshot.Profiles.FirstOrDefault(item => item.Id == latestActiveId) : null;
				if (profile is null)
				{
					lock (_sync) _handledRevision = targetRevision;
					SetStatus(ProfileDeploymentStatus.Idle);
					continue;
				}
				SetStatus(new ProfileDeploymentStatus(ProfileDeploymentStage.Deploying, profile.Id, targetRevision, null, null, "正在部署最新活动配置。", null));
				ApplyResult result;
				try
				{
					result = await _profileApplyService.ApplyAsync(profile, snapshot, _paths.ModsDirectory, gameData, CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					SetStatus(new ProfileDeploymentStatus(ProfileDeploymentStage.Failed, profile.Id, targetRevision, null, null, exception.Message, null));
					lock (_sync) _handledRevision = targetRevision;
					continue;
				}
				lock (_sync) _handledRevision = targetRevision;
				SetStatus(new ProfileDeploymentStatus(result.Success ? ProfileDeploymentStage.Completed : ProfileDeploymentStage.Failed, profile.Id, targetRevision, null, null, result.Success ? "活动配置已部署。" : "活动配置部署失败。", result));
			}
		}
		finally
		{
			_lane.Release();
			lock (_sync)
			{
				if (!_deactivationRequested) _worker = null;
			}
		}
	}

	private void SetStatus(ProfileDeploymentStatus status)
	{
		lock (_sync) _status = status;
		StatusChanged?.Invoke(this, status);
	}

	public async ValueTask DisposeAsync()
	{
		Task? worker;
		lock (_sync)
		{
			_deactivationRequested = true;
			worker = _worker;
		}
		if (worker is not null)
		{
			try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
		}
		_lane.Dispose();
	}
}
