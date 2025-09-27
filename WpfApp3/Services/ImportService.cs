using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiberTeaManager.Services;

namespace LiberTeaManager.Services
{
    internal sealed class ImportService : IImportService
    {
        private readonly ILogService _log;
        private readonly ObservableCollection<MainModItem> _mods;
        private readonly object _addModSync = new(); // 同步添加
        private const int ParallelFactorDivider = 2; // 并行度因子
        private readonly TimeSpan _logInterval = TimeSpan.FromMilliseconds(350);
        private readonly ISettingsService _settings;

        public ImportService(ObservableCollection<MainModItem> mods, ILogService log, ISettingsService settings = null)
        {
            _mods = mods;
            _log = log;
            _settings = settings ?? SettingsContext.Instance ?? new SettingsService(log);
        }

        public async Task ImportArchivesAsync(IEnumerable<string> archivePaths)
        {
            var distinct = archivePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count == 0) return;
            int degree = Math.Max(1, Environment.ProcessorCount / ParallelFactorDivider);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int finished = 0; int total = distinct.Count; DateTime lastLog = DateTime.MinValue;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = degree };
            await Parallel.ForEachAsync(distinct, parallelOptions, async (path, ct) =>
            {
                try { await Task.Run(() => ImportSingleArchive(path), ct); }
                catch (Exception ex) { _log.Log($"导入失败 {Path.GetFileName(path)}: {ex.Message}"); }
                int done = Interlocked.Increment(ref finished);
                if (DateTime.UtcNow - lastLog > _logInterval)
                {
                    lock (_addModSync)
                    {
                        if (DateTime.UtcNow - lastLog > _logInterval)
                        {
                            lastLog = DateTime.UtcNow;
                            _log.Log($"进度: {done}/{total} ({(int)(done * 100.0 / total)}%)");
                        }
                    }
                }
            });
            sw.Stop();
            _log.Log($"归档导入完成: {finished}/{total} 用时 {sw.Elapsed.TotalSeconds:F1}s (并行 {degree})");
        }

        public int ImportDirectory(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory)) return 0;
            try
            {
                // 选中的目录视为一个主 Mod；其下一级目录为 Option，第三级为 SubOption
                var baseName = SanitizeName(new DirectoryInfo(rootDirectory).Name);
                if (string.IsNullOrWhiteSpace(baseName)) return 0;
                var modName = EnsureUniqueModName(baseName);

                // 拷贝到临时目录，生成/读取 manifest
                string temp = Path.Combine(Path.GetTempPath(), "_import_" + Guid.NewGuid().ToString("N"));
                CopyDirectory(rootDirectory, temp);

                MainModItem modItem;
                string manifestPath = Path.Combine(temp, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var json = File.ReadAllText(manifestPath);
                        json = RegexReplaceTrailingCommas(json);
                        modItem = System.Text.Json.JsonSerializer.Deserialize<MainModItem>(json) ?? ManifestGenerator.GenerateManifest(modName, temp);
                        modItem.Name = modName; modItem.RootModName = modName;
                    }
                    catch
                    {
                        // 若读取失败，按目录结构生成
                        modItem = ManifestGenerator.GenerateManifest(modName, temp);
                    }
                }
                else
                {
                    // 无 manifest：基于目录结构生成（主=当前文件夹；子目录=选项；下一级=子选项）
                    modItem = ManifestGenerator.GenerateManifest(modName, temp);
                }

                lock (_addModSync)
                {
                    ModListManager.AddMod(modItem, temp);
                }
                _log.Log($"已导入 Mod: {modItem.Name}");
                return 1;
            }
            catch (Exception ex)
            {
                _log.Log("导入目录失败: " + ex.Message);
                return 0;
            }
        }

        private void ImportSingleArchive(string path)
        {
            try
            {
                var ext = Path.GetExtension(path)?.ToLowerInvariant();
                if (ext is null || new[] { ".zip", ".7z", ".rar" }.Contains(ext) == false)
                {
                    _log.Log($"不支持的归档类型: {Path.GetFileName(path)}");
                    return;
                }
                bool useFast = _settings.FastImport;
                if (useFast)
                {
                    _log.Log($"导入: {Path.GetFileName(path)} (快速模式)");
                }
                var filtered = useFast ? ModFileHelper.DecompressToTempFiltered(path) : null;
                string tempRoot = useFast ? filtered.TempDir : ModFileHelper.DecompressToTemp(path);
                bool needFullFallback = false;
                if (useFast)
                {
                    if (filtered.ExtractedFiles.Count == 0 || !Directory.Exists(tempRoot)) needFullFallback = true;
                    string manifestFile = Path.Combine(tempRoot, "manifest.json");
                    if (!File.Exists(manifestFile) && !filtered.ExtractedFiles.Any(f => f.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!filtered.ExtractedFiles.Any(f => f.Contains(".patch_"))) needFullFallback = true;
                    }
                }
                if (!useFast || needFullFallback)
                {
                    tempRoot = ModFileHelper.DecompressToTemp(path);
                    if (string.IsNullOrEmpty(tempRoot) || !Directory.Exists(tempRoot)) { _log.Log("解压失败或目录不存在"); return; }
                }
                string workingDir = NormalizeSingleRoot(tempRoot);
                string inferredName = InferNameFromDirectoryOrArchive(workingDir, path);
                inferredName = SanitizeName(inferredName);
                if (string.IsNullOrWhiteSpace(inferredName)) inferredName = "Mod_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                inferredName = EnsureUniqueModName(inferredName);

                MainModItem modItem = null;
                if (useFast && !needFullFallback)
                {
                    var manifestFile = Path.Combine(workingDir, "manifest.json");
                    if (File.Exists(manifestFile))
                    {
                        try
                        {
                            var json = File.ReadAllText(manifestFile);
                            json = RegexReplaceTrailingCommas(json);
                            modItem = System.Text.Json.JsonSerializer.Deserialize<MainModItem>(json);
                            if (modItem != null) { modItem.Name = inferredName; modItem.RootModName = inferredName; }
                        }
                        catch { modItem = null; }
                    }
                    if (modItem == null)
                    {
                        modItem = ModFileHelper.BuildModFromExtracted(inferredName, workingDir, filtered.ExtractedFiles);
                    }
                }
                else
                {
                    modItem = ModFileHelper.BuildModFromDirectory(inferredName, workingDir);
                }

                string moveSource = PrepareMoveSource(tempRoot, workingDir);
                moveSource = EnsureDirectoryName(moveSource, inferredName);
                lock (_addModSync)
                {
                    ModListManager.AddMod(modItem!, moveSource);
                }
                _log.Log($"已导入 Mod: {modItem!.Name}");
            }
            catch (Exception ex)
            {
                _log.Log($"归档导入失败: {Path.GetFileName(path)} => {ex.Message}");
            }
        }

        private bool TryImportExtracted(string sourceDir)
        {
            try
            {
                if (!Directory.Exists(sourceDir)) return false;
                string name = SanitizeName(new DirectoryInfo(sourceDir).Name);
                if (string.IsNullOrWhiteSpace(name)) return false;
                name = EnsureUniqueModName(name);
                string temp = Path.Combine(Path.GetTempPath(), "_import_" + Guid.NewGuid().ToString("N"));
                CopyDirectory(sourceDir, temp);
                MainModItem modItem = ModFileHelper.BuildModFromDirectory(name, temp);
                modItem.RootModName = modItem.Name;
                lock (_addModSync)
                {
                    ModListManager.AddMod(modItem, temp);
                }
                _log.Log($"已导入 Mod: {modItem.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Log($"目录导入失败: {sourceDir} => {ex.Message}");
                return false;
            }
        }

        #region Helpers
        private static string NormalizeSingleRoot(string tempRoot)
        {
            var topFiles = Directory.GetFiles(tempRoot, "*", SearchOption.TopDirectoryOnly);
            var topDirs = Directory.GetDirectories(tempRoot, "*", SearchOption.TopDirectoryOnly);
            if (topDirs.Length == 1 && topFiles.Length == 0)
            {
                return topDirs[0];
            }
            return tempRoot;
        }
        private static string InferNameFromDirectoryOrArchive(string workingDir, string archivePath)
        {
            string inferredName = new DirectoryInfo(workingDir).Name;
            if (inferredName.StartsWith("_export_", StringComparison.OrdinalIgnoreCase) || inferredName.StartsWith("_temp_", StringComparison.OrdinalIgnoreCase))
                inferredName = Path.GetFileNameWithoutExtension(archivePath);
            return inferredName;
        }
        private string EnsureDirectoryName(string moveSource, string inferredName)
        {
            var finalRootName = new DirectoryInfo(moveSource).Name;
            if (!string.Equals(finalRootName, inferredName, StringComparison.OrdinalIgnoreCase))
            {
                var targetRenamed = Path.Combine(Path.GetDirectoryName(moveSource)!, inferredName);
                int tryIdx = 1;
                while (Directory.Exists(targetRenamed))
                    targetRenamed = Path.Combine(Path.GetDirectoryName(moveSource)!, inferredName + "_" + tryIdx++);
                Directory.Move(moveSource, targetRenamed);
                moveSource = targetRenamed;
            }
            return moveSource;
        }
        private static string PrepareMoveSource(string tempRoot, string workingDir)
        {
            string moveSource = workingDir;
            if (moveSource != tempRoot && Directory.GetDirectories(tempRoot).Length > 1)
            {
                var dedicated = Path.Combine(Path.GetTempPath(), "_single_" + Guid.NewGuid().ToString("N"));
                CopyDirectory(moveSource, dedicated);
                moveSource = dedicated;
            }
            return moveSource;
        }
        private static MainModItem LoadManifestOrCreate(string manifestPath, string fallbackName)
        {
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    json = RegexReplaceTrailingCommas(json);
                    return System.Text.Json.JsonSerializer.Deserialize<MainModItem>(json) ?? NewEmpty(fallbackName);
                }
                catch { return NewEmpty(fallbackName); }
            }
            return NewEmpty(fallbackName);
        }
        private static MainModItem NewEmpty(string name) => new MainModItem { Name = name, Guid = Guid.NewGuid(), Options = new ObservableCollection<OptionItem>(), FileGroups = new List<ModFileGroup>() };
        private static string RegexReplaceTrailingCommas(string json) => System.Text.RegularExpressions.Regex.Replace(json, @",(\s*[}\]])", "$1");
        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
        private string EnsureUniqueModName(string baseName)
        {
            string candidate = baseName; int idx = 1;
            while (_mods.Any(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase)) || Directory.Exists(Path.Combine(SettingsContext.ModFolder, candidate)))
                candidate = baseName + "_" + idx++;
            return candidate;
        }
        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                var target = Path.Combine(dest, Path.GetFileName(file));
                try
                {
                    if (File.Exists(target))
                    {
                        var srcInfo = new FileInfo(file);
                        var dstInfo = new FileInfo(target);
                        if (dstInfo.Length == srcInfo.Length && dstInfo.LastWriteTimeUtc == srcInfo.LastWriteTimeUtc)
                        {
                            continue;
                        }
                    }
                    File.Copy(file, target, true);
                }
                catch { }
            }
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
            {
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
            }
        }
        #endregion
    }
}
