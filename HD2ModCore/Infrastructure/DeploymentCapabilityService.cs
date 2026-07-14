using System.Runtime.InteropServices;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Probes source/target file-system capabilities and selects the only supported link deployment method.
public sealed class DeploymentCapabilityService
{
	public DeploymentCapability Probe(string modsDirectory, string gameDataDirectory)
	{
		if (string.IsNullOrWhiteSpace(modsDirectory) || string.IsNullOrWhiteSpace(gameDataDirectory))
			return DeploymentCapability.Unavailable("Mod 库或游戏 Data 目录尚未设置。");
		try
		{
			Directory.CreateDirectory(modsDirectory);
			Directory.CreateDirectory(gameDataDirectory);
		}
		catch (Exception exception)
		{
			return DeploymentCapability.Unavailable(exception.Message);
		}

		var token = Guid.NewGuid().ToString("N");
		var source = Path.Combine(modsDirectory, $".hd2-link-source-{token}.tmp");
		var target = Path.Combine(gameDataDirectory, $".hd2-link-target-{token}.tmp");
		try
		{
			File.WriteAllText(source, token);
			string? hardLinkError = "源目录与目标目录不在同一卷。";
			if (AreOnSameVolume(source, target) && TryCreateHardLink(target, source, out hardLinkError))
				return new DeploymentCapability(true, DeploymentMethod.HardLink, "Mod 库与游戏 Data 位于同一卷，硬链接测试通过。", null);
			TryDelete(target);
			if (TryCreateSymbolicLink(target, source, out var symbolicLinkError))
				return new DeploymentCapability(true, DeploymentMethod.SymbolicLink, "符号链接测试通过。", null);
			return DeploymentCapability.Unavailable($"硬链接不可用：{hardLinkError}; 符号链接不可用：{symbolicLinkError}");
		}
		catch (Exception exception)
		{
			return DeploymentCapability.Unavailable(exception.Message);
		}
		finally
		{
			TryDelete(target);
			TryDelete(source);
		}
	}

	private static bool AreOnSameVolume(string first, string second)
		=> string.Equals(Path.GetPathRoot(Path.GetFullPath(first)), Path.GetPathRoot(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

	private static bool TryCreateSymbolicLink(string linkPath, string targetPath, out string? error)
	{
		try { File.CreateSymbolicLink(linkPath, targetPath); error = null; return true; }
		catch (Exception exception) { error = exception.Message; return false; }
	}

	private static bool TryCreateHardLink(string linkPath, string targetPath, out string? error)
	{
		if (!OperatingSystem.IsWindows()) { error = "Hard links are currently implemented for Windows only."; return false; }
		if (CreateHardLinkW(linkPath, targetPath, IntPtr.Zero)) { error = null; return true; }
		error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
		return false;
	}

	private static void TryDelete(string path)
	{
		try { if (File.Exists(path) || Directory.Exists(path)) File.Delete(path); } catch { }
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}

public sealed record DeploymentCapability(bool IsAvailable, DeploymentMethod? Method, string Summary, string? Error)
{
	public static DeploymentCapability Unavailable(string error) => new(false, null, "当前无法部署 Mod。", error);
}
