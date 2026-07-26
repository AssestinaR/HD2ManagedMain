using System.Text.RegularExpressions;

namespace HD2ModCore.Tests;

// 作用：约束 HD2ModManager 不绕过共享信息中心创建稳定事实基础设施。
// Purpose: Prevent HD2ModManager from bypassing the shared information center.
public sealed class ModInformationCenterArchitectureTests
{
	[Fact]
	public void ManagerProductionCodeDoesNotCreateInformationInfrastructureOrUseObsoleteCenterlessFactories()
	{
		var managerRoot = FindRepositoryRoot();
		var sourceFiles = Directory.EnumerateFiles(Path.Combine(managerRoot, "HD2ModManager"), "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			.Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
		var forbidden = new Regex(
			@"(?:CoreServices\.)?CreateModDataIndex\s*\(|new\s+(?:ModDataIndex|JsonModInformationCache|SqliteModFactsStore)\s*\(|new\s+(?:ModFileFactsProducer|ModContentFactsService|ReferenceGraphProducer|MaintenanceAnalysisProducer|UnitVersionInformationProducer|AdvancedUnitAnalysisProducer|ModThumbnailProducer)\s*\(",
			RegexOptions.Compiled);
		var violations = sourceFiles
			.SelectMany(path => File.ReadLines(path).Select((line, number) => (path, line, number: number + 1)))
			.Where(item => forbidden.IsMatch(item.line))
			.Select(item => $"{Path.GetRelativePath(managerRoot, item.path)}:{item.number}: {item.line.Trim()}")
			.ToArray();

		Assert.Empty(violations);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WpfApp1.sln")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate the repository root.");
	}
}
