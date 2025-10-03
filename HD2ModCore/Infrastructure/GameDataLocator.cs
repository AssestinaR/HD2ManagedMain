using HD2ModCore.Application;
using Microsoft.Win32;

namespace HD2ModCore.Infrastructure;

// 作用：定位 HD2 的 data 目录（优先使用手动设置，其次通过 Steam 注册表 + libraryfolders.vdf 自动探测）。
// Purpose: Locates the HD2 data directory (prefers manual override, otherwise auto-detects via Steam registry + libraryfolders.vdf).
public sealed class GameDataLocator : IGameDataLocator
{
	private readonly IGameDataSettings _settings;

	public GameDataLocator(IGameDataSettings settings)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
	}

	public ValueTask<string?> TryGetGameDataDirectoryAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var overridePath = _settings.GameDataDirectoryOverride;
		if (!string.IsNullOrWhiteSpace(overridePath))
		{
			var dataDir = NormalizeDataDirectory(overridePath);
			if (dataDir is not null)
			{
				return ValueTask.FromResult<string?>(dataDir);
			}
		}

		var steamPath = TryGetSteamInstallPath();
		if (steamPath is null)
		{
			return ValueTask.FromResult<string?>(null);
		}

		var libraries = SteamLibraryFoldersReader.TryGetLibraryFolders(steamPath);
		foreach (var libraryPath in libraries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var candidate = Path.Combine(libraryPath, "steamapps", "common", "Helldivers 2", "data");
			candidate = NormalizeDataDirectory(candidate);
			if (candidate is not null)
			{
				return ValueTask.FromResult<string?>(candidate);
			}
		}

		return ValueTask.FromResult<string?>(null);
	}

	private static string? NormalizeDataDirectory(string path)
	{
		try
		{
			var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
			if (!Directory.Exists(full))
			{
				return null;
			}
			return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return null;
		}
	}

	private static string? TryGetSteamInstallPath()
	{
		static string? ReadValue(RegistryKey baseKey, string subKey, string valueName)
		{
			using var key = baseKey.OpenSubKey(subKey);
			return key?.GetValue(valueName) as string;
		}

	var path = ReadValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
			?? ReadValue(Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath")
			?? ReadValue(Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");

	if (string.IsNullOrWhiteSpace(path))
	{
		return null;
	}

	try
	{
		path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
		return Directory.Exists(path) ? path : null;
	}
	catch
	{
		return null;
	}
	}
}
