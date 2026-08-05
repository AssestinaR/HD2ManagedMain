using System.Globalization;
using System.Text;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Emits opt-in, stage-level position diagnostics for Canonical reconstruction investigations.
public static class CanonicalPositionDiagnostics
{
	private const string EnabledVariable = "HD2_CANONICAL_POSITION_DIAGNOSTICS";
	private static readonly object Sync = new();
	private static readonly AsyncLocal<DiagnosticScope?> CurrentScope = new();

	public static bool IsEnabled
		=> string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal)
			|| string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase);

	public static IDisposable BeginUnit(ulong unitFileId)
	{
		if (!IsEnabled)
			return NoopScope.Instance;
		var previous = CurrentScope.Value;
		CurrentScope.Value = new DiagnosticScope($"0x{unitFileId:x16}", previous);
		Write($"unit={CurrentScope.Value.UnitFileId} begin");
		return CurrentScope.Value;
	}

	public static void RecordMesh(string stage, UnitRawMeshData mesh, UnitStreamInfo? stream = null)
	{
		if (!IsEnabled)
			return;
		var positions = mesh.Vertices
			.SelectMany(vertex => vertex.Components)
			.Where(component => component.Type == 0 && component.Index == 0 && component.FloatValues.Length >= 3)
			.Select(component => component.FloatValues)
			.ToArray();
		var bounds = positions.Length == 0
			? "position=none"
			: string.Format(
				CultureInfo.InvariantCulture,
				"position=x[{0:R},{1:R}] y[{2:R},{3:R}] z[{4:R},{5:R}] count={6}",
				positions.Min(value => value[0]), positions.Max(value => value[0]),
				positions.Min(value => value[1]), positions.Max(value => value[1]),
				positions.Min(value => value[2]), positions.Max(value => value[2]),
				positions.Length);
		var sample = positions.Length == 0
			? ""
			: string.Format(CultureInfo.InvariantCulture, " sample=({0:R},{1:R},{2:R})", positions[0][0], positions[0][1], positions[0][2]);
		var data = mesh.Vertices.FirstOrDefault()?.Data ?? Array.Empty<byte>();
		var bytes = data.Length == 0 ? string.Empty : $" bytes={Convert.ToHexString(data.AsSpan(0, Math.Min(data.Length, 16)))}";
		var streamText = stream is null ? string.Empty : $" stream={stream.Index} stride={stream.VertexStride}";
		Write($"unit={CurrentScope.Value?.UnitFileId ?? "unknown"} stage={stage} mesh={mesh.MeshInfoIndex} vertices={mesh.Vertices.Count} triangles={mesh.Triangles.Count}{streamText} {bounds}{sample}{bytes}");
	}

	public static void RecordGpuVertex(string stage, int streamIndex, int meshInfoIndex, uint vertexOffset, uint vertexStart, uint vertexStride, IReadOnlyList<byte> gpuData)
	{
		if (!IsEnabled)
			return;
		var offset = checked((int)(vertexStart + vertexOffset * vertexStride));
		var length = offset >= 0 && offset < gpuData.Count ? Math.Min(16, gpuData.Count - offset) : 0;
		var bytes = length == 0 ? "out-of-range" : Convert.ToHexString(gpuData.Skip(offset).Take(length).ToArray());
		Write($"unit={CurrentScope.Value?.UnitFileId ?? "unknown"} stage={stage} mesh={meshInfoIndex} stream={streamIndex} vertexStart={vertexStart} vertexOffset={vertexOffset} stride={vertexStride} gpuBytes={bytes}");
	}

	public static void RecordGpuAppend(string stage, int streamIndex, int meshInfoIndex, int vertexIndex, int gpuCountBefore, uint vertexStart, uint vertexOffset, uint vertexStride, IReadOnlyList<byte> vertexData, IReadOnlyList<byte> gpuData)
	{
		if (!IsEnabled)
			return;
		var appended = vertexData.Count == 0 ? "empty" : Convert.ToHexString(vertexData.Take(Math.Min(16, vertexData.Count)).ToArray());
		var tail = gpuData.Count == 0 ? "empty" : Convert.ToHexString(gpuData.Skip(Math.Max(0, gpuData.Count - Math.Min(16, vertexData.Count))).Take(Math.Min(16, vertexData.Count)).ToArray());
		Write($"unit={CurrentScope.Value?.UnitFileId ?? "unknown"} stage={stage} mesh={meshInfoIndex} stream={streamIndex} vertex={vertexIndex} gpuBefore={gpuCountBefore} gpuAfter={gpuData.Count} vertexStart={vertexStart} vertexOffset={vertexOffset} stride={vertexStride} vertexData={appended} appendedTail={tail}");
	}

	private static void Write(string message)
	{
		try
		{
			var directory = Path.Combine(AppContext.BaseDirectory, "logs");
			Directory.CreateDirectory(directory);
			var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
			lock (Sync)
				File.AppendAllText(Path.Combine(directory, "canonical-position-diagnostics.log"), line, Encoding.UTF8);
		}
		catch
		{
			// Diagnostics must never affect a reconstruction attempt.
		}
	}

	private sealed class DiagnosticScope(string unitFileId, DiagnosticScope? previous) : IDisposable
	{
		public string UnitFileId { get; } = unitFileId;

		public void Dispose()
		{
			Write($"unit={UnitFileId} end");
			CurrentScope.Value = previous;
		}
	}

	private sealed class NoopScope : IDisposable
	{
		public static NoopScope Instance { get; } = new();
		public void Dispose() { }
	}
}