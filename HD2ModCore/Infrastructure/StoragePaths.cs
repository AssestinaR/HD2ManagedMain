namespace HD2ModCore.Infrastructure;

public sealed record StoragePaths(string AppRootDirectory)
{
  // 作用：程序根目录下的统一数据目录结构，便于便携与打包。
	// Purpose: Standard portable directory layout under app root for portability and packaging.
	public string DataDirectory => Path.Combine(AppRootDirectory, "data");
 public string LibraryDirectory => Path.Combine(DataDirectory, "library");
	public string ModsDirectory => Path.Combine(AppRootDirectory, "mods");
	public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
	public string IndexDirectory => Path.Combine(DataDirectory, "indexes");
	public string DbPath => Path.Combine(IndexDirectory, "asset-index.sqlite");
	public string ResourcesDirectory => Path.Combine(DataDirectory, "resources");
	public string ArchiveHashesPath => Path.Combine(ResourcesDirectory, "archivehashes.json");
}
