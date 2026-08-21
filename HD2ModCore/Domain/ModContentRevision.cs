using System.Security.Cryptography;
using System.Text;

namespace HD2ModCore.Domain;

// 作用：表达源 Patch、Overwrite、有效视图、GameData 和分析器版本的独立修订。
// Purpose: Tracks independent revisions for source Patch, Overwrite, effective view, GameData, and analyzers.
public sealed record ModContentRevision(
	string SourceRevision,
	string OverwriteRevision,
	string EffectiveRevision,
	string GameDataRevision = "",
	string AnalyzerVersion = "")
{
	public string CacheKey => string.Join('|',
		$"source={SourceRevision}",
		$"overwrite={OverwriteRevision}",
		$"effective={EffectiveRevision}",
		$"gamedata={GameDataRevision}",
		$"analyzer={AnalyzerVersion}");

	public static ModContentRevision Create(
		string sourceRevision,
		string? overwriteRevision = null,
		string? gameDataRevision = null,
		string? analyzerVersion = null,
		string? effectiveRevision = null)
	{
		sourceRevision = Normalize(sourceRevision);
		overwriteRevision = Normalize(overwriteRevision);
		gameDataRevision = Normalize(gameDataRevision);
		analyzerVersion = Normalize(analyzerVersion);
		var effective = Normalize(effectiveRevision);
		if (effective.Length == 0)
		{
			var payload = Encoding.UTF8.GetBytes($"source={sourceRevision}\noverwrite={overwriteRevision}");
			effective = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
		}
		return new ModContentRevision(sourceRevision, overwriteRevision, effective, gameDataRevision, analyzerVersion);
	}

	public static ModContentRevision FromLegacyGeneration(string generation, string? analyzerVersion = null)
		=> Create(generation, analyzerVersion: analyzerVersion);

	private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
