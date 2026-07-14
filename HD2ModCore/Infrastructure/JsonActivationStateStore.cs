using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Persists activation-state.json through an atomic temporary-file replacement.
public sealed class JsonActivationStateStore : IActivationStateStore
{
	public const string StateFileName = "activation-state.json";
	public const int CurrentVersion = 2;
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	public async ValueTask<ActivationState?> TryLoadAsync(string gameDataDirectory, CancellationToken cancellationToken = default)
	{
		var path = GetPath(gameDataDirectory);
		if (!File.Exists(path)) return null;
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var state = await JsonSerializer.DeserializeAsync<ActivationState>(stream, Options, cancellationToken).ConfigureAwait(false);
		if (state is null || state.Version != CurrentVersion)
		{
			throw new InvalidDataException($"Unsupported activation state version. Expected {CurrentVersion}.");
		}
		return state;
	}

	public async ValueTask SaveAsync(string gameDataDirectory, ActivationState state, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(state);
		Directory.CreateDirectory(gameDataDirectory);
		var path = GetPath(gameDataDirectory);
		var tempPath = path + ".tmp";
		try
		{
			await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
			{
				await JsonSerializer.SerializeAsync(stream, state with { Version = CurrentVersion }, Options, cancellationToken).ConfigureAwait(false);
				await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			File.Move(tempPath, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(tempPath)) File.Delete(tempPath);
		}
	}

	public ValueTask DeleteAsync(string gameDataDirectory, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var path = GetPath(gameDataDirectory);
		if (File.Exists(path)) File.Delete(path);
		var tempPath = path + ".tmp";
		if (File.Exists(tempPath)) File.Delete(tempPath);
		return ValueTask.CompletedTask;
	}

	private static string GetPath(string gameDataDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		return Path.Combine(Path.GetFullPath(gameDataDirectory), StateFileName);
	}
}
