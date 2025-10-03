using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证从 libraryfolders.vdf 中解析 Steam 库目录的逻辑。
// Purpose: Verifies parsing Steam library directories from libraryfolders.vdf.
public sealed class SteamLibraryFoldersReaderTests
{
	[Fact]
	public void TryGetLibraryFolders_NoVdf_ReturnsSteamDirOnly()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tmp);

		try
		{
			var libs = SteamLibraryFoldersReader.TryGetLibraryFolders(tmp);
			Assert.Contains(tmp, libs, StringComparer.OrdinalIgnoreCase);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
		}
	}

	[Fact]
	public void TryGetLibraryFolders_WithVdf_ExtractsPaths()
	{
		var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		var steamapps = Path.Combine(tmp, "steamapps");
		Directory.CreateDirectory(steamapps);

		var lib1 = Path.Combine(Path.GetTempPath(), "SteamLib1");
		var lib2 = Path.Combine(Path.GetTempPath(), "SteamLib2");
		Directory.CreateDirectory(lib1);
		Directory.CreateDirectory(lib2);

		try
		{
           var vdf = $$"""
			"libraryfolders"
			{
			  "contentstatsid"    "123"
			  "1"
			  {
				"path"   "{{lib1}}"
			  }
			  "2"
			  {
				"path"   "{{lib2}}"
			  }
			}
			""";
			File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"), vdf);

			var libs = SteamLibraryFoldersReader.TryGetLibraryFolders(tmp);
			Assert.Contains(tmp, libs, StringComparer.OrdinalIgnoreCase);
			Assert.Contains(lib1, libs, StringComparer.OrdinalIgnoreCase);
			Assert.Contains(lib2, libs, StringComparer.OrdinalIgnoreCase);
		}
		finally
		{
			try { Directory.Delete(tmp, recursive: true); } catch { }
			try { Directory.Delete(lib1, recursive: true); } catch { }
			try { Directory.Delete(lib2, recursive: true); } catch { }
		}
	}
}
