using System.Text.RegularExpressions;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：解析 HD2 patch 文件名（hex16.patch_n，可选 .stream/.gpu_resources）为结构化元数据。
// Purpose: Parses HD2 patch filenames (hex16.patch_n with optional .stream/.gpu_resources) into structured metadata.
public sealed class PatchFileNameParser : IPatchFileNameParser
{
	private static readonly Regex PatchRegex = new(
		@"^(?<hex>[0-9a-fA-F]{16})\.patch_(?<n>\d+)(?<sidecar>(?:\.stream|\.gpu_resources)?)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public bool TryParse(string fileName, out PatchFileNameInfo? info)
	{
		info = null;
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		var name = Path.GetFileName(fileName);
		var m = PatchRegex.Match(name);
		if (!m.Success)
		{
			return false;
		}

		var hex = m.Groups["hex"].Value.ToLowerInvariant();
		if (!int.TryParse(m.Groups["n"].Value, out var n) || n < 0)
		{
			return false;
		}

		var sidecarText = m.Groups["sidecar"].Value;
		var kind = sidecarText switch
		{
			"" => PatchSidecarKind.Base,
			".stream" => PatchSidecarKind.Stream,
			".gpu_resources" => PatchSidecarKind.GpuResources,
			_ => throw new InvalidOperationException($"Unexpected sidecar: '{sidecarText}'"),
		};

     info = new PatchFileNameInfo(hex, n, kind, FullFileName: name);
		return true;
	}

	public PatchFileNameInfo Parse(string fileName)
		=> TryParse(fileName, out var info)
			? info!
			: throw new FormatException($"Not a valid HD2 patch filename: '{fileName}'");
}
