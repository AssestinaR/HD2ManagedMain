using System.Net.Http;

namespace HD2ModCore.Infrastructure;

// 作用：从 GitHub 下载最新版 archivehashes.json，并安全写入本地缓存（失败则不破坏旧文件）。
// Purpose: Downloads the latest archivehashes.json from GitHub and safely writes it to the local cache (failures do not corrupt the old file).
public sealed class GitHubArchiveHashesSync
{
	private readonly HttpClient _httpClient;
	private readonly StoragePaths _paths;
	private readonly Uri _uri;

	public GitHubArchiveHashesSync(HttpClient httpClient, StoragePaths paths, Uri uri)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_uri = uri ?? throw new ArgumentNullException(nameof(uri));
	}

	public async ValueTask<bool> TrySyncAsync(CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.ResourcesDirectory);

		string json;
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
			request.Headers.UserAgent.ParseAdd("HD2ModManager/1.0");

			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return false;
			}

			json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(json))
			{
				return false;
			}
		}
		catch
		{
			return false;
		}

		var tmp = _paths.ArchiveHashesPath + ".tmp";
		try
		{
			await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
			File.Copy(tmp, _paths.ArchiveHashesPath, overwrite: true);
			File.Delete(tmp);
			return true;
		}
		catch
		{
			try
			{
				if (File.Exists(tmp))
				{
					File.Delete(tmp);
				}
			}
			catch
			{
				// ignored
			}
			return false;
		}
	}
}
