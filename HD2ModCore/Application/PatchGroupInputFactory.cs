using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Converts scanned patch-group filenames into Adaptation-owned file path inputs.
public sealed class PatchGroupInputFactory
{
	public IReadOnlyList<PatchGroupInput> Create(
		ModNode node,
		string modsRootDirectory,
		IReadOnlyList<PatchFileGroupFingerprint> groups)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentException.ThrowIfNullOrWhiteSpace(modsRootDirectory);
		ArgumentNullException.ThrowIfNull(groups);

		var nodeDirectory = Path.Combine(Path.GetFullPath(modsRootDirectory), node.RelativePath.Replace('/', Path.DirectorySeparatorChar));
		return groups
			.OrderBy(group => group.GroupName, StringComparer.OrdinalIgnoreCase)
			.Select(group =>
			{
				var files = group.Files
					.Where(file => !string.IsNullOrWhiteSpace(file))
					.Select(file => Path.Combine(nodeDirectory, file))
					.ToArray();
				var toc = files.FirstOrDefault(IsPatchToc);
				if (toc is null)
					toc = Path.Combine(nodeDirectory, group.GroupName);
				return new PatchGroupInput(
					toc,
					files.FirstOrDefault(IsStream),
					files.FirstOrDefault(IsGpuResources));
			})
			.ToArray();
	}

	private static bool IsPatchToc(string path) => path.EndsWith(".patch", StringComparison.OrdinalIgnoreCase);
	private static bool IsStream(string path) => path.EndsWith(".stream", StringComparison.OrdinalIgnoreCase);
	private static bool IsGpuResources(string path) => path.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase);
}
