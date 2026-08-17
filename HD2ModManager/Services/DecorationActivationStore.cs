using System.IO;
using System.Text.Json;

namespace HD2ModManager.Services;

// Local activation state is intentionally separate from portable decoration.json.
public sealed class DecorationActivationStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, HashSet<string>> _enabledByDecoration = new(StringComparer.OrdinalIgnoreCase);

    public DecorationActivationStore(string path)
    {
        _path = path;
        Load();
    }

    public IReadOnlySet<string> GetEnabledHosts(string decorationId)
        => _enabledByDecoration.TryGetValue(decorationId, out var hosts)
            ? new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public async Task SetEnabledHostsAsync(string decorationId, IEnumerable<string> hostIds, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetEnabledHostsCore(decorationId, hostIds);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // Commits related decoration state changes in one file write. The caller owns
    // planning and supplies complete host sets for the affected decoration ids.
    public async Task SetEnabledHostsBatchAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> hostsByDecoration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostsByDecoration);
        if (hostsByDecoration.Count == 0) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = _enabledByDecoration.ToDictionary(
                item => item.Key,
                item => new HashSet<string>(item.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var item in hostsByDecoration)
                    SetEnabledHostsCore(item.Key, item.Value);
                await SaveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _enabledByDecoration = previous;
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public Task RemoveDecorationAsync(string decorationId, CancellationToken cancellationToken = default)
        => SetEnabledHostsAsync(decorationId, Array.Empty<string>(), cancellationToken);

    public Task RemoveDecorationsAsync(IEnumerable<string> decorationIds, CancellationToken cancellationToken = default)
        => SetEnabledHostsBatchAsync(
            decorationIds.Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(id => id, _ => (IReadOnlyCollection<string>)Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
            cancellationToken);

    public async Task RemoveHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var hosts in _enabledByDecoration.Values) hosts.Remove(hostId);
            _enabledByDecoration = _enabledByDecoration.Where(item => item.Value.Count > 0)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var values = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (values is null) return;
            _enabledByDecoration = values.ToDictionary(item => item.Key, item => item.Value.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or IOException) { _enabledByDecoration.Clear(); }
    }

    private void SetEnabledHostsCore(string decorationId, IEnumerable<string> hostIds)
    {
        var normalized = hostIds.Where(id => Guid.TryParse(id, out _)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0) _enabledByDecoration.Remove(decorationId);
        else _enabledByDecoration[decorationId] = normalized;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var values = _enabledByDecoration.ToDictionary(item => item.Key, item => item.Value.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var temporaryPath = _path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(values, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
