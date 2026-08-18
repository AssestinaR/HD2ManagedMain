using System.Text.Json;

namespace HD2ModCore.Infrastructure;

// Persists option enablement separately from profiles. Options are attached to
// hosts and therefore must never become ordinary profile entries.
public sealed class OptionActivationStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, HashSet<string>> _enabledByOption = new(StringComparer.OrdinalIgnoreCase);

    public OptionActivationStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        Load();
    }

    public IReadOnlySet<string> GetEnabledHosts(string optionId)
    {
        lock (_gate)
            return _enabledByOption.TryGetValue(optionId, out var hosts)
                ? hosts.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetEnabledHostsAsync(string optionId, IEnumerable<string> hosts, CancellationToken cancellationToken = default)
    {
        var normalized = hosts.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            if (normalized.Count == 0) _enabledByOption.Remove(optionId);
            else _enabledByOption[optionId] = normalized;
        }
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string optionId, CancellationToken cancellationToken = default)
    {
        lock (_gate) _enabledByOption.Remove(optionId);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostId)) return;
        lock (_gate)
        {
            foreach (var hosts in _enabledByOption.Values) hosts.Remove(hostId);
            _enabledByOption = _enabledByOption
                .Where(item => item.Value.Count > 0)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        }
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyDictionary<string, IReadOnlySet<string>> Snapshot()
    {
        lock (_gate)
            return _enabledByOption.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var values = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (values is null) return;
            _enabledByOption = values.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>> values;
        lock (_gate) values = _enabledByOption.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
    }
}
