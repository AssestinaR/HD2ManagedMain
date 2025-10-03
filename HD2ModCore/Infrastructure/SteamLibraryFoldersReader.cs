using System.Text.RegularExpressions;

namespace HD2ModCore.Infrastructure;

// 作用：从 Steam 的 libraryfolders.vdf 中提取库目录（最小实现，用于定位游戏安装路径）。
// Purpose: Extracts Steam library directories from libraryfolders.vdf (minimal implementation for install path detection).
public static partial class SteamLibraryFoldersReader
{
	private static readonly Regex LibraryPathRegex = new(
		"\"path\"\\s*\"(?<path>[^\"]+)\"",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	public static IReadOnlyList<string> TryGetLibraryFolders(string steamInstallDirectory)
	{
		var result = new List<string>();

		if (string.IsNullOrWhiteSpace(steamInstallDirectory))
		{
			return result;
		}

		var steamDir = steamInstallDirectory;
		try
		{
			steamDir = Path.GetFullPath(steamDir);
		}
		catch
		{
			return result;
		}

		result.Add(steamDir);

		var vdfPath = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
		if (!File.Exists(vdfPath))
		{
			return result;
		}

		try
		{
			var text = File.ReadAllText(vdfPath);
			foreach (Match m in LibraryPathRegex.Matches(text))
			{
				var path = m.Groups["path"].Value;
				if (string.IsNullOrWhiteSpace(path))
				{
					continue;
				}

				path = path.Replace("\\\\", "\\");
				try
				{
					path = Path.GetFullPath(path);
				}
				catch
				{
					continue;
				}

				if (Directory.Exists(path) && !result.Contains(path, StringComparer.OrdinalIgnoreCase))
				{
					result.Add(path);
				}
			}
		}
		catch
		{
			return result;
		}

		return result;
	}
}
