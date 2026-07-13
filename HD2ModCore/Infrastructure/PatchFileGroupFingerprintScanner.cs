using System.Security.Cryptography;
using System.Text;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Scans patch files without opening TOC or game data and fingerprints each patch group.
public sealed class PatchFileGroupFingerprintScanner : IPatchFileGroupFingerprintScanner
{
	private readonly IPatchFileNameParser _parser;

	public PatchFileGroupFingerprintScanner(IPatchFileNameParser parser)
	{
		_parser = parser ?? throw new ArgumentNullException(nameof(parser));
	}

	public async ValueTask<IReadOnlyDictionary<ModNodeId, IReadOnlyList<PatchFileGroupFingerprint>>> ScanAsync(LibrarySnapshot snapshot, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var result = new Dictionary<ModNodeId, IReadOnlyList<PatchFileGroupFingerprint>>();
		foreach (var node in snapshot.Nodes.Values)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var directory = Path.Combine(modsRootDirectory, node.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			var groups = new List<PatchFileGroupFingerprint>();
			if (Directory.Exists(directory))
			{
				var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
					.Select(path => (Path: path, Info: TryParse(path)))
					.Where(item => item.Info is not null)
					.GroupBy(item => $"{item.Info!.ArchiveHex16}:{item.Info.PatchIndex}", StringComparer.OrdinalIgnoreCase);

				foreach (var group in files.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
				{
					var groupFiles = group.OrderBy(item => item.Info!.SidecarKind).ThenBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase).ToList();
					var parts = group.Key.Split(':', 2);
					var fileFingerprints = new List<PatchFileFingerprint>();
					foreach (var item in groupFiles)
					{
						var name = Path.GetFileName(item.Path);
						var hash = await ComputeHashAsync(item.Path, cancellationToken).ConfigureAwait(false);
						fileFingerprints.Add(new PatchFileFingerprint(item.Info!.SidecarKind, name, hash));
					}
					groups.Add(new PatchFileGroupFingerprint(
						$"{parts[0]}.patch_{parts[1]}",
						ComputeHash(string.Join("\n", fileFingerprints.Select(file => $"{file.FileName}:{file.ContentHash}"))),
						fileFingerprints.Select(file => file.FileName).ToList(),
						fileFingerprints));
				}
			}
			result[node.Id] = groups;
		}
		return result;
	}

	private PatchFileNameInfo? TryParse(string path) => _parser.TryParse(Path.GetFileName(path), out var info) ? info : null;

	private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = File.OpenRead(path);
		using var sha = SHA256.Create();
		return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
	}

	private static string ComputeHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}