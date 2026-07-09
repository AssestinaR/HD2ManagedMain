using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：使用 _diagnostics 中复制的真实新旧模组样本验证 patch TOC 元数据能定位 Unit payload 与 sidecar 范围。
// Purpose: Verifies patch TOC metadata from copied real diagnostics samples can locate Unit payload and sidecar ranges.
public sealed class DiagnosticsPatchSampleTests
{
	private const ulong UnitTypeId = 0xe0a48d0be9a7453f;
	private const int SidecarAlignment = 64;

	[Fact]
	public async Task ScanEntriesAsync_DiagnosticsSamples_ReturnsValidUnitSidecarRanges()
	{
		var diagnostics = FindDiagnosticsDirectory();
		if (diagnostics is null)
		{
			return;
		}

		var scanner = new PatchTocScanner();
		var totalUnitEntries = 0;
		var validUnitEntries = 0;
		var malformedUnitEntries = 0;
		var rootsWithValidUnitEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var rootName in new[] { "旧版", "新版" })
		{
			var root = Path.Combine(diagnostics, rootName);
			if (!Directory.Exists(root))
			{
				continue;
			}

			var patchFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
				.Where(path => IsPatchTocFile(Path.GetFileName(path)))
				.Take(40)
				.ToArray();

			foreach (var patchFile in patchFiles)
			{
				var entries = await scanner.ScanEntriesAsync(patchFile);
				var unitEntries = entries.Where(entry => entry.AssetKey.TypeId == UnitTypeId).ToArray();
				if (unitEntries.Length == 0)
				{
					continue;
				}

				totalUnitEntries += unitEntries.Length;
				var patchLength = new FileInfo(patchFile).Length;
				var gpuLength = GetOptionalFileLength(patchFile + ".gpu_resources");
				var streamLength = GetOptionalFileLength(patchFile + ".stream");

				foreach (var entry in unitEntries.Take(8))
				{
					if (IsRangeValid(patchLength, entry.TocDataOffset, entry.TocDataSize) &&
						IsSidecarRangeValid(gpuLength, entry.GpuResourceOffset, entry.GpuResourceSize) &&
						IsSidecarRangeValid(streamLength, entry.StreamOffset, entry.StreamSize))
					{
						validUnitEntries++;
						rootsWithValidUnitEntries.Add(rootName);
					}
					else
					{
						malformedUnitEntries++;
					}
				}
			}
		}

		Assert.Contains("旧版", rootsWithValidUnitEntries);
		Assert.Contains("新版", rootsWithValidUnitEntries);
		Assert.True(totalUnitEntries > 0);
		Assert.True(validUnitEntries > malformedUnitEntries);
	}

	[Fact]
	public async Task ReadPayloadAsync_DiagnosticsSamples_CanParseSomeRealUnitMeshes()
	{
		var diagnostics = FindDiagnosticsDirectory();
		if (diagnostics is null)
		{
			return;
		}

		var scanner = new PatchTocScanner();
		var unitReader = new PatchUnitMeshReader(new PatchEntryPayloadReader(), new UnitMeshReader());
		var parsedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var parsedUnits = 0;
		var failedParses = 0;

		foreach (var rootName in new[] { "旧版", "新版" })
		{
			var root = Path.Combine(diagnostics, rootName);
			if (!Directory.Exists(root))
			{
				continue;
			}

			var patchFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
				.Where(path => IsPatchTocFile(Path.GetFileName(path)))
				.Take(40)
				.ToArray();

			foreach (var patchFile in patchFiles)
			{
				var entries = await scanner.ScanEntriesAsync(patchFile);
				var candidates = entries
					.Where(entry => entry.AssetKey.TypeId == UnitTypeId && HasValidRanges(patchFile, entry))
					.Take(4)
					.ToArray();

				foreach (var entry in candidates)
				{
					try
					{
						await unitReader.ReadUnitMeshAsync(entry);
						parsedUnits++;
						parsedRoots.Add(rootName);
					}
					catch (InvalidDataException)
					{
						failedParses++;
					}
					catch (ArgumentOutOfRangeException)
					{
						failedParses++;
					}
					catch (OverflowException)
					{
						failedParses++;
					}

					if (parsedRoots.Count == 2 && parsedUnits >= 2)
					{
						break;
					}
				}

				if (parsedRoots.Count == 2 && parsedUnits >= 2)
				{
					break;
				}
			}
		}

		Assert.True(parsedUnits + failedParses > 0);
		Assert.True(parsedUnits >= parsedRoots.Count);
	}

	private static string? FindDiagnosticsDirectory()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current is not null)
		{
			var candidate = Path.Combine(current.FullName, "_diagnostics");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			current = current.Parent;
		}

		return null;
	}

	private static bool IsPatchTocFile(string fileName)
	{
		var marker = fileName.LastIndexOf(".patch_", StringComparison.OrdinalIgnoreCase);
		if (marker < 0)
		{
			return false;
		}

		var suffix = fileName.AsSpan(marker + ".patch_".Length);
		return suffix.Length > 0 && suffix.IndexOf('.') < 0 && suffix.IndexOfAnyExceptInRange('0', '9') < 0;
	}

	private static long GetOptionalFileLength(string path)
		=> File.Exists(path) ? new FileInfo(path).Length : 0L;

	private static bool IsRangeValid(long containerLength, ulong offset, uint size)
	{
		if (size == 0)
		{
			return true;
		}

		return offset <= (ulong)containerLength && offset + size <= (ulong)containerLength;
	}

	private static bool IsSidecarRangeValid(long containerLength, ulong offset, uint size)
	{
		if (size == 0)
		{
			return true;
		}

		if (containerLength <= 0)
		{
			return false;
		}

		var alignedLength = AlignUp((ulong)containerLength, SidecarAlignment);
		return offset <= alignedLength && offset + size <= alignedLength;
	}

	private static bool HasValidRanges(string patchFile, PatchTocEntry entry)
		=> IsRangeValid(new FileInfo(patchFile).Length, entry.TocDataOffset, entry.TocDataSize) &&
			IsSidecarRangeValid(GetOptionalFileLength(patchFile + ".gpu_resources"), entry.GpuResourceOffset, entry.GpuResourceSize) &&
			IsSidecarRangeValid(GetOptionalFileLength(patchFile + ".stream"), entry.StreamOffset, entry.StreamSize);

	private static ulong AlignUp(ulong value, int alignment)
	{
		var mask = checked((ulong)alignment - 1UL);
		return (value + mask) & ~mask;
	}
}
