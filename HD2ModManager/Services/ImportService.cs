using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModManager.Services
{
    // 作用：将 UI 导入请求转交给 HD2ModCore，并补充 manager 层标签/图片元数据。
    public class ImportService
    {
        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };

        private readonly ModLibraryService _library;
        private readonly Action<string>? _onInfo;
        private readonly Action<string>? _onError;
        private readonly StoragePaths _paths;

        public ImportService(ModLibraryService library, Action<string>? onInfo = null, Action<string>? onError = null)
        {
            _library = library;
            _onInfo = onInfo;
            _onError = onError;
            _paths = SettingsService.CreateStoragePaths();
        }

        public Task EnqueueImportsAsync(IEnumerable<string> paths, CancellationToken ct = default)
        {
            return Task.WhenAll(paths.Select(p => ImportPathAsync(p, ct)));
        }

        public async Task<List<string>> ImportPathAsync(string path, CancellationToken ct)
        {
            try
            {
                var importer = CoreServices.CreateModLibraryImporter(_paths);
                var before = _library.Snapshot.Nodes.Keys.ToHashSet();
                ImportResult result;
                if (Directory.Exists(path))
                {
                    result = await importer.ImportFolderAsync(path, ct).ConfigureAwait(false);
                }
                else if (File.Exists(path) && ArchiveExtensions.Contains(Path.GetExtension(path)))
                {
                    result = await importer.ImportArchiveAsync(path, ct).ConfigureAwait(false);
                }
                else if (File.Exists(path))
                {
                    var dir = Path.GetDirectoryName(path) ?? throw new DirectoryNotFoundException(path);
                    result = await importer.ImportFolderAsync(dir, ct).ConfigureAwait(false);
                }
                else
                {
                    throw new FileNotFoundException("Import source not found.", path);
                }

                await EnrichImportedMetadataAsync(result, path, ct).ConfigureAwait(false);
                var importedGuids = _library.Snapshot.Nodes.Values
                    .Where(node => !before.Contains(node.Id))
                    .Select(node => node.Id.Value.ToString("N"))
                    .ToList();
                await _library.RefreshDerivedDataAsync(importedGuids, ct).ConfigureAwait(false);
                _onInfo?.Invoke($"Imported {result.SourceDisplayName}");
                return importedGuids.Where(g => _library.Get(g) != null).ToList();
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex.Message);
                throw;
            }
        }

        private async Task EnrichImportedMetadataAsync(ImportResult result, string sourcePath, CancellationToken ct)
        {
            var manager = CoreServices.CreateModLibraryManager(_paths);
            var snapshot = result.Snapshot;

            foreach (var node in result.Snapshot.Nodes.Values)
            {
                ct.ThrowIfCancellationRequested();
                var tags = ResolveTagsFromName(node.Metadata.Name);

                if (tags.Count == 0) continue;

                var metadata = node.Metadata with
                {
                    UserTags = tags,
                    ModifiedUtc = DateTimeOffset.UtcNow,
                };
                snapshot = await manager.UpdateNodeMetadataAsync(node.Id, metadata, ct).ConfigureAwait(false);
            }

            _library.ReplaceSnapshot(snapshot, buildDerivedData: false);
        }

        private static List<string> ResolveTagsFromName(string name)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(name)) return result;
            try
            {
                var src = NormalizeForLooseMatch(name);
                foreach (var item in TagCatalogService.Instance.GetAll())
                {
                    var hit = false;
                    if (!string.IsNullOrWhiteSpace(item.Code)) hit |= src.Contains(item.Code.ToLowerInvariant());
                    if (!string.IsNullOrWhiteSpace(item.EnglishName)) hit |= src.Contains(item.EnglishName.ToLowerInvariant());
                    if (!string.IsNullOrWhiteSpace(item.ChineseName)) hit |= src.Contains(NormalizeForLooseMatch(item.ChineseName));
                    if (!string.IsNullOrWhiteSpace(item.Name)) hit |= src.Contains(item.Name.ToLowerInvariant());
                    if (hit && !result.Contains(item.Name)) result.Add(item.Name);
                }

                foreach (Match match in Regex.Matches(name, @"\b([A-Z]{1,3}(?:/[A-Z]{1,3})?-\d{1,4})\b", RegexOptions.IgnoreCase))
                {
                    var code = match.Groups[1].Value.ToUpperInvariant();
                    var tag = TagCatalogService.Instance.GetAll().FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (tag != null && !result.Contains(tag.Name)) result.Add(tag.Name);
                }
            }
            catch { }
            return result;
        }

        private static string NormalizeForLooseMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.ToLowerInvariant()
                .Replace('（', ' ')
                .Replace('）', ' ')
                .Replace('【', ' ')
                .Replace('】', ' ')
                .Replace('[', ' ')
                .Replace(']', ' ');
        }
    }
}
