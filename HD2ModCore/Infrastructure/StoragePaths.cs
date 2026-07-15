namespace HD2ModCore.Infrastructure;

public sealed record StoragePaths(string AppRootDirectory, string? ModsRootDirectory = null)
{
	// 作用：配置与缓存保留在程序根目录，同时允许 Mod 库使用独立的权威路径。
	// Purpose: Keeps configuration/cache under the app root while allowing an authoritative external Mod library.
	public string DataDirectory => Path.Combine(AppRootDirectory, "data");
	public string LibraryDirectory => ModsDirectory;
	public string ModsDirectory => string.IsNullOrWhiteSpace(ModsRootDirectory) ? Path.Combine(AppRootDirectory, "mods") : Path.GetFullPath(ModsRootDirectory);
	public string LibraryPath => Path.Combine(ModsDirectory, "library.json");
	public string ProfilesPath => Path.Combine(DataDirectory, "profiles.json");
	public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
	public string IndexDirectory => Path.Combine(DataDirectory, "indexes");
	public string DbPath => Path.Combine(IndexDirectory, "asset-index.sqlite");
	public string ModFactsDbPath => Path.Combine(IndexDirectory, "mod-facts.sqlite");
	public string ResourcesDirectory => Path.Combine(DataDirectory, "resources");
	public string ArchiveHashesPath => Path.Combine(ResourcesDirectory, "archivehashes.json");
	public string TypeHashesPath => Path.Combine(ResourcesDirectory, "typehash.txt");
	public string FriendlyNamesPath => Path.Combine(ResourcesDirectory, "friendlynames.txt");
	public string AssetMetadataManifestPath => Path.Combine(ResourcesDirectory, "asset-metadata-manifest.json");
	public string AssetAnalysisCacheDirectory => Path.Combine(DataDirectory, "asset-cache");
	public string PatchGroupAnalysisCacheDirectory => Path.Combine(DataDirectory, "patch-facts-cache");
	public string PatchFileGroupFingerprintManifestPath => Path.Combine(DataDirectory, "patch-group-fingerprints.json");
}
