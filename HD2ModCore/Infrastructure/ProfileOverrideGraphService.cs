using System.Security.Cryptography;
using System.Text;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Builds expected strict AssetKey winners and separate coarse archive overlaps from authoritative content and mapping facts.
public sealed class ProfileOverrideGraphService : IProfileOverrideGraphService
{
	private readonly IModInformationCenter _informationCenter;
	private readonly IGameDataMappingFactsService _mappingFactsService;

	public ProfileOverrideGraphService(IModInformationCenter informationCenter, IGameDataMappingFactsService mappingFactsService)
	{
		_informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
		_mappingFactsService = mappingFactsService ?? throw new ArgumentNullException(nameof(mappingFactsService));
	}

	public async ValueTask<ProfileOverrideGraph> BuildAsync(Profile profile, LibrarySnapshot snapshot, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(snapshot);
		var orderedEntries = profile.Entries.OrderBy(entry => entry.LoadOrder).ThenBy(entry => entry.AddedUtc).ThenBy(entry => entry.NodeId.Value).ToList();
		var nodeIds = orderedEntries.Select(entry => entry.NodeId).ToHashSet();
		var contentByNode = await GetAssetInventoryAsync(orderedEntries, snapshot, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var assetKeys = contentByNode.Values.SelectMany(facts => facts.PatchGroups).SelectMany(group => group.AssetKeys).ToHashSet();
		var mapping = await _mappingFactsService.MapAsync(assetKeys, cancellationToken).ConfigureAwait(false);
		var issues = new List<CoreIssue>(contentByNode.Values.SelectMany(facts => facts.Issues));
		issues.AddRange(mapping.Issues);
		foreach (var entry in orderedEntries.Where(entry => !snapshot.Nodes.ContainsKey(entry.NodeId)))
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ProfileNodeMissing", $"Profile entry references a missing mod node: {entry.NodeId}", NodeId: entry.NodeId));
		}

		var participants = new List<Participant>();
		foreach (var entry in orderedEntries)
		{
			if (!snapshot.Nodes.TryGetValue(entry.NodeId, out var node) || !contentByNode.TryGetValue(entry.NodeId, out var content)) continue;
			foreach (var assetGroup in content.PatchGroups
				.SelectMany(group => group.AssetKeys.Select(assetKey => (assetKey, group.Id, group.NormalizedOrder)))
				.GroupBy(item => item.assetKey))
			{
				var mapped = mapping.Assets.TryGetValue(assetGroup.Key, out var gameMapped)
					? gameMapped
					: CreatePrivateAssetMapping(assetGroup.Key);
				participants.Add(new Participant(entry, node.Metadata.Name, mapped, assetGroup.Select(item => item.Id).ToList(), assetGroup.Max(item => item.NormalizedOrder)));
			}
		}

		var chains = participants
			.GroupBy(participant => participant.Mapping.AssetKey)
			.Select(group =>
			{
				var ordered = group.OrderBy(item => item.Entry.LoadOrder).ThenBy(item => item.MaxGroupOrder).ThenBy(item => item.Entry.NodeId.Value).ToList();
				return new ProfileAssetOverrideChain(group.Key, ordered.Select((item, index) => new ProfileAssetOverrideEntry(
					item.Entry.NodeId,
					item.ModName,
					item.Entry.LoadOrder,
					item.PatchGroups,
					item.Mapping,
					index == ordered.Count - 1)).ToList());
			})
			.OrderBy(chain => chain.AssetKey.TypeId)
			.ThenBy(chain => chain.AssetKey.FileId)
			.ToList();

		var archiveOverlaps = participants
			.SelectMany(participant => participant.Mapping.TargetArchives.Select(archive => (archive, participant.Entry.NodeId)))
			.GroupBy(item => item.archive.ArchiveId, StringComparer.OrdinalIgnoreCase)
			.Select(group => new ProfileArchiveOverlap(
				group.Key,
				group.First().archive.DisplayName,
				group.First().archive.Category,
				group.Select(item => item.NodeId).Distinct().ToList()))
			.Where(overlap => overlap.NodeIds.Count > 1)
			.OrderBy(overlap => overlap.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(overlap => overlap.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var coverages = orderedEntries
			.Where(entry => snapshot.Nodes.ContainsKey(entry.NodeId))
			.Select(entry =>
			{
				var related = chains.Where(chain => chain.Entries.Any(item => item.NodeId == entry.NodeId)).ToList();
				var won = related.Count(chain => chain.Winner.NodeId == entry.NodeId);
				return new ProfileModCoverage(entry.NodeId, snapshot.Nodes[entry.NodeId].Metadata.Name, related.Count, won, related.Count - won);
			})
			.ToList();
		var contentGenerations = contentByNode.ToDictionary(pair => pair.Key, pair => pair.Value.ContentGeneration);
		var graphGeneration = ComputeGraphGeneration(profile, contentGenerations, mapping.MappingGeneration);
		return new ProfileOverrideGraph(profile.Id, profile.Revision, graphGeneration, mapping.MappingGeneration, DateTimeOffset.UtcNow, contentGenerations, chains, archiveOverlaps, coverages, issues);
	}

	private async ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetAssetInventoryAsync(
		IReadOnlyList<ProfileEntry> entries,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken)
	{
		var result = new Dictionary<ModNodeId, ModContentFacts>();
		foreach (var nodeId in entries.Select(entry => entry.NodeId).Distinct())
		{
			if (!snapshot.Nodes.TryGetValue(nodeId, out var node)) continue;
			var inventory = await _informationCenter.RequestAssetInventoryAsync(
				node,
				modsRootDirectory,
				new ModInformationRequest(ModInformationKind.AssetInventory, "ProfilePreview"),
				cancellationToken).ConfigureAwait(false);
			if (inventory.Data is not null) result[nodeId] = inventory.Data;
		}
		return result;
	}

	private static string ComputeGraphGeneration(Profile profile, IReadOnlyDictionary<ModNodeId, string> contentGenerations, string mappingGeneration)
	{
		var builder = new StringBuilder().Append(profile.Id.Value.ToString("N")).Append(':').Append(profile.Revision).Append(':').Append(mappingGeneration).AppendLine();
		foreach (var entry in profile.Entries.OrderBy(entry => entry.LoadOrder).ThenBy(entry => entry.NodeId.Value))
		{
			contentGenerations.TryGetValue(entry.NodeId, out var generation);
			builder.Append(entry.NodeId.Value.ToString("N")).Append(':').Append(entry.LoadOrder).Append(':').Append(generation).AppendLine();
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private static GameDataMappedAssetFact CreatePrivateAssetMapping(AssetKey assetKey)
	{
		var (typeName, category) = assetKey.TypeId switch
		{
			0xe0a48d0be9a7453f => ("Unit", AssetTypeCategory.Model),
			0xc4f0f4be7fb0c8d6 => ("Composite Unit", AssetTypeCategory.Model),
			0xeac0b497876adedf => ("Material", AssetTypeCategory.Material),
			0xcd4238c6a0c69e32 => ("Texture", AssetTypeCategory.Texture),
			_ => ("Mod 私有资源", AssetTypeCategory.Unknown)
		};
		return new GameDataMappedAssetFact(assetKey, $"0x{assetKey.FileId:x16}", typeName, category, Array.Empty<ArchiveMetadata>());
	}

	private sealed record Participant(ProfileEntry Entry, string ModName, GameDataMappedAssetFact Mapping, IReadOnlyList<ModPatchGroupId> PatchGroups, int MaxGroupOrder);
}
