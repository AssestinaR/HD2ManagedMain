using System.Text.Json;

namespace HD2ModCore.Infrastructure;

// Community-facing manifest model. Unknown fields intentionally remain ignored.
internal sealed class StandardModManifest
{
    public string? Guid { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? IconPath { get; init; }
    public List<StandardModManifestOption> Options { get; init; } = [];
    public List<StandardModManifestNode>? Nodes { get; init; }

    public static StandardModManifest? TryLoad(string packageRoot)
    {
        var path = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<StandardModManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            });
            // A manager-only Nodes manifest is handled by the legacy identity restore path.
            return manifest is { Name: not null } || manifest?.Options.Count > 0 || manifest?.IconPath is not null ? manifest : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}

internal sealed class StandardModManifestOption
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Image { get; init; }
    public List<string>? Include { get; init; }
    public List<StandardModManifestSubOption>? SubOptions { get; init; }
}

internal sealed class StandardModManifestSubOption
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Image { get; init; }
    public List<string>? Include { get; init; }
}

internal sealed class StandardModManifestNode
{
    public string? RelativePath { get; init; }
    public string? Guid { get; init; }
    public string? Name { get; init; }
    public string? Notes { get; init; }
}
