using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		private static readonly SemaphoreSlim ImportAnalysisGate = new(1, 1);

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
            return ImportSequentiallyAsync(paths, ct);
        }

        private async Task ImportSequentiallyAsync(IEnumerable<string> paths, CancellationToken ct)
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                await ImportPathAsync(path, ct).ConfigureAwait(false);
            }
        }

        public async Task<List<string>> ImportPathAsync(string path, CancellationToken ct, bool notifyLibraryChanged = true)
        {
            try
            {
                var importer = CoreServices.CreateModLibraryImporter(_paths);
                var before = _library.Snapshot.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
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

                _library.ReplaceSnapshot(result.Snapshot, buildDerivedData: false);
                var importedGuids = _library.Snapshot.Nodes.Values
                    .Where(node => !before.ContainsKey(node.Id))
                    .Select(node => node.Id.Value.ToString("N"))
                    .ToList();
                var changedExistingNodeIds = _library.Snapshot.Nodes.Values
                    .Where(node => before.TryGetValue(node.Id, out var previous) && !Equals(previous, node))
                    .Select(node => node.Id)
                    .ToArray();
                if (notifyLibraryChanged) _library.NotifyImportCompleted();
                _onInfo?.Invoke($"Imported {result.SourceDisplayName}");
                return importedGuids.Where(g => _library.Get(g) != null).ToList();
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex.Message);
                throw;
            }
        }

        public async Task AnalyzeImportedAsync(IEnumerable<string> importedGuids, CancellationToken ct)
        {
            var guids = importedGuids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (guids.Length == 0) return;
            await ImportAnalysisGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() => _library.RefreshDerivedDataAsync(guids, ModContentChangeKind.Added, ct), ct).ConfigureAwait(false);
            }
            finally
            {
                ImportAnalysisGate.Release();
            }
        }

    }
}
