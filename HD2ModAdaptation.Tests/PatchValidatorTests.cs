using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.Validation;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the detachable Patch validation tool reports structural and Unit readback defects.
public sealed class PatchValidatorTests : IDisposable
{
	private readonly string directory = Path.Combine(Path.GetTempPath(), "patch-validator-" + Guid.NewGuid().ToString("N"));

	public PatchValidatorTests()
	{
		Directory.CreateDirectory(directory);
	}

	[Fact]
	public async Task ValidateAsync_ReportsMissingSidecarAndDuplicateAssetKey()
	{
		var patch = Path.Combine(directory, "invalid.patch_0");
		var data = CreatePatch(
			new PatchTocEntry(new AssetKey(1, 2), patch, Path.GetFileName(patch), TocDataSize: 1, GpuResourceSize: 4),
			new PatchTocEntry(new AssetKey(1, 2), patch, Path.GetFileName(patch), TocDataSize: 1));
		BitConverter.GetBytes(4u).CopyTo(data, 72 + 32 + 64);
		await File.WriteAllBytesAsync(patch, data);

		var result = await new PatchValidator().ValidateAsync(patch);

		Assert.False(result.IsValid);
		Assert.Contains(result.Issues, issue => issue.Code == "DuplicateAssetKey");
		Assert.Contains(result.Issues, issue => issue.Code == "MissingSidecar");
	}

	[Fact]
	public async Task ValidateAsync_ReportsUnitReadbackFailure()
	{
		var patch = Path.Combine(directory, "bad-unit.patch_0");
		await File.WriteAllBytesAsync(patch, CreatePatch(
			new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 2), patch, Path.GetFileName(patch), TocDataSize: 24)));

		var result = await new PatchValidator().ValidateAsync(patch);

		Assert.False(result.IsValid);
		Assert.Equal(1, result.UnitsChecked);
		Assert.Contains(result.Issues, issue => issue.Code == "UnitReadbackFailed");
	}

	[Fact]
	public async Task ValidateAsync_ReportsExpectedVersionAsWarningOrError()
	{
		var patch = Path.Combine(directory, "version.patch_0");
		var toc = CreateInlineUnitToc();
		await File.WriteAllBytesAsync(patch, CreatePatch(new[] { new PatchTocEntry(
			new AssetKey(PatchUnitMeshReader.UnitTypeId, 2), patch, Path.GetFileName(patch), TocDataSize: (uint)toc.Length) }, toc));

		var warning = await new PatchValidator().ValidateAsync(patch, new PatchValidationOptions(ExpectedUnitVersion: 99));
		var error = await new PatchValidator().ValidateAsync(patch, new PatchValidationOptions(ExpectedUnitVersion: 99, TreatOutdatedUnitVersionAsError: true));

		Assert.True(warning.IsValid);
		Assert.Contains(warning.Issues, issue => issue.Code == "OutdatedUnitVersion" && issue.Severity == PatchValidationSeverity.Warning);
		Assert.False(error.IsValid);
		Assert.Contains(error.Issues, issue => issue.Code == "OutdatedUnitVersion" && issue.Severity == PatchValidationSeverity.Error);
	}

	private static byte[] CreatePatch(params PatchTocEntry[] entries)
		=> CreatePatch(entries, entries.Select(_ => new byte[] { 1 }).ToArray());

	private static byte[] CreatePatch(PatchTocEntry[] entries, params byte[][] payloads)
	{
		var header = new byte[72 + 32];
		BitConverter.GetBytes(4026531857u).CopyTo(header, 0);
		BitConverter.GetBytes(1u).CopyTo(header, 4);
		BitConverter.GetBytes((uint)entries.Length).CopyTo(header, 8);
		BitConverter.GetBytes(entries[0].AssetKey.TypeId).CopyTo(header, 80);
		BitConverter.GetBytes((ulong)entries.Length).CopyTo(header, 88);
		using var stream = new MemoryStream();
		stream.Write(header);
		stream.Position = header.Length + entries.Length * 80;
		for (var i = 0; i < entries.Length; i++)
		{
			var payload = payloads[i];
			var data = new byte[80];
			BitConverter.GetBytes(entries[i].AssetKey.FileId).CopyTo(data, 0);
			BitConverter.GetBytes(entries[i].AssetKey.TypeId).CopyTo(data, 8);
			BitConverter.GetBytes((ulong)stream.Position).CopyTo(data, 16);
			BitConverter.GetBytes((uint)payload.Length).CopyTo(data, 56);
			stream.Position = header.Length + i * 80;
			stream.Write(data);
			stream.Position = header.Length + entries.Length * 80;
			stream.Write(payload);
		}
		return stream.ToArray();
	}

	private static byte[] CreateInlineUnitToc()
	{
		var data = new byte[136];
		BitConverter.GetBytes(1u).CopyTo(data, 0x2c);
		BitConverter.GetBytes(96u).CopyTo(data, 0x5c);
		BitConverter.GetBytes(112u).CopyTo(data, 0x64);
		return data;
	}

	public void Dispose()
	{
		if (Directory.Exists(directory)) Directory.Delete(directory, true);
	}
}
