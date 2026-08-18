using System.Text.Json;

namespace HD2ModCore.Infrastructure;

// Persists host-scoped option state. EffectiveOrder is local to one host, so a
// future drag/drop reorder never mutates the portable source-package order.
public sealed class OptionActivationStore
{
	private const int CurrentVersion = 2;
	private readonly string _path;
	private readonly object _gate = new();
	private Dictionary<string, Dictionary<string, OptionHostState>> _states = new(StringComparer.OrdinalIgnoreCase);

	public OptionActivationStore(string path)
	{
		_path = path ?? throw new ArgumentNullException(nameof(path));
		Load();
	}

	public IReadOnlySet<string> GetEnabledHosts(string optionId)
	{
		lock (_gate)
			return _states.TryGetValue(optionId, out var hosts)
				? hosts.Where(pair => pair.Value.Enabled).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
				: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	public int? GetEffectiveOrder(string optionId, string hostId)
	{
		lock (_gate)
			return _states.TryGetValue(optionId, out var hosts) && hosts.TryGetValue(hostId, out var state)
				? state.EffectiveOrder
				: null;
	}

	public OptionActivationSnapshot CreateSnapshot()
	{
		lock (_gate)
		{
			var options = _states.ToDictionary(
				option => option.Key,
				option => (IReadOnlyDictionary<string, OptionHostState>)option.Value.ToDictionary(host => host.Key, host => host.Value, StringComparer.OrdinalIgnoreCase),
				StringComparer.OrdinalIgnoreCase);
			return new OptionActivationSnapshot(options);
		}
	}

	public async Task SetEnabledHostsAsync(string optionId, IEnumerable<string> hosts, CancellationToken cancellationToken = default)
	{
		var normalized = hosts.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		lock (_gate)
		{
			if (normalized.Count == 0) _states.Remove(optionId);
			else
			{
				if (!_states.TryGetValue(optionId, out var current))
					_states[optionId] = current = new Dictionary<string, OptionHostState>(StringComparer.OrdinalIgnoreCase);
				foreach (var host in current.Keys.Where(host => !normalized.Contains(host)).ToArray()) current.Remove(host);
				foreach (var host in normalized)
					current[host] = current.TryGetValue(host, out var existing) ? existing with { Enabled = true } : new OptionHostState(true, null);
			}
		}
		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task SetEffectiveOrderAsync(string optionId, string hostId, int? effectiveOrder, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(optionId);
		ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
		lock (_gate)
		{
			if (!_states.TryGetValue(optionId, out var hosts))
				_states[optionId] = hosts = new Dictionary<string, OptionHostState>(StringComparer.OrdinalIgnoreCase);
			hosts[hostId] = hosts.TryGetValue(hostId, out var existing)
				? existing with { EffectiveOrder = effectiveOrder }
				: new OptionHostState(false, effectiveOrder);
		}
		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoveAsync(string optionId, CancellationToken cancellationToken = default)
	{
		lock (_gate) _states.Remove(optionId);
		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoveHostAsync(string hostId, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(hostId)) return;
		lock (_gate)
		{
			foreach (var hosts in _states.Values) hosts.Remove(hostId);
			_states = _states.Where(item => item.Value.Count > 0)
				.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
		}
		await SaveAsync(cancellationToken).ConfigureAwait(false);
	}

	public IReadOnlyDictionary<string, IReadOnlySet<string>> Snapshot()
		=> CreateSnapshot().Options.ToDictionary(
			option => option.Key,
			option => (IReadOnlySet<string>)option.Value.Where(host => host.Value.Enabled).Select(host => host.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
			StringComparer.OrdinalIgnoreCase);

	private void Load()
	{
		try
		{
			if (!File.Exists(_path)) return;
			using var document = JsonDocument.Parse(File.ReadAllText(_path));
			if (document.RootElement.TryGetProperty("version", out _))
			{
				var persisted = JsonSerializer.Deserialize<PersistedState>(document.RootElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
				if (persisted?.Options is not null)
					_states = persisted.Options.ToDictionary(
						option => option.Key,
						option => option.Value.ToDictionary(host => host.Key, host => new OptionHostState(host.Value.Enabled, host.Value.EffectiveOrder), StringComparer.OrdinalIgnoreCase),
						StringComparer.OrdinalIgnoreCase);
				return;
			}

			// v1 compatibility: { optionId: [ hostId, ... ] }
			var legacy = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(document.RootElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
			if (legacy is null) return;
			_states = legacy.ToDictionary(
				option => option.Key,
				option => option.Value.Where(host => !string.IsNullOrWhiteSpace(host)).ToDictionary(host => host, _ => new OptionHostState(true, null), StringComparer.OrdinalIgnoreCase),
				StringComparer.OrdinalIgnoreCase);
		}
		catch (JsonException) { }
		catch (IOException) { }
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		PersistedState snapshot;
		lock (_gate)
		{
			snapshot = new PersistedState
			{
				Version = CurrentVersion,
				Options = _states.ToDictionary(
					option => option.Key,
					option => option.Value.ToDictionary(host => host.Key, host => new PersistedHostState { Enabled = host.Value.Enabled, EffectiveOrder = host.Value.EffectiveOrder }, StringComparer.OrdinalIgnoreCase),
					StringComparer.OrdinalIgnoreCase)
			};
		}
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		var temporary = _path + ".tmp";
		await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
		File.Move(temporary, _path, overwrite: true);
	}

	private sealed class PersistedState
	{
		public int Version { get; set; } = CurrentVersion;
		public Dictionary<string, Dictionary<string, PersistedHostState>> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class PersistedHostState
	{
		public bool Enabled { get; set; }
		public int? EffectiveOrder { get; set; }
	}
}

public sealed record OptionHostState(bool Enabled, int? EffectiveOrder);

public sealed record OptionActivationSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, OptionHostState>> Options)
{
	public bool IsEnabled(string optionId, string hostId)
		=> Options.TryGetValue(optionId, out var hosts)
			&& hosts.TryGetValue(hostId, out var state)
			&& state.Enabled;

	public int? GetEffectiveOrder(string optionId, string hostId)
		=> Options.TryGetValue(optionId, out var hosts) && hosts.TryGetValue(hostId, out var state)
			? state.EffectiveOrder
			: null;
}
