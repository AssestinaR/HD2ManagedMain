using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HD2ModManager.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace HD2ModManager.Services
{
    public class ImportService
    {
        private readonly ModLibraryService _library;
        private readonly Action<string>? _onInfo;
        private readonly Action<string>? _onError;
        public ImportService(ModLibraryService library, Action<string>? onInfo = null, Action<string>? onError = null)
        {
            _library = library; _onInfo = onInfo; _onError = onError;
        }

        private static readonly Regex PatchRegex = new Regex(
            @"^([a-fA-F0-9]{16})\.patch_(\d+)(?:\.stream|\.gpu_resources)?$",
            RegexOptions.Compiled);
        private static readonly HashSet<string> AllowedImages = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        private static readonly HashSet<string> AllowedOthers = new(StringComparer.OrdinalIgnoreCase) { ".json", ".txt" };

        public Task EnqueueImportsAsync(IEnumerable<string> paths, CancellationToken ct = default)
        {
            var queue = new ConcurrentQueue<string>(paths);
            int degree = Math.Max(1, Math.Min(Environment.ProcessorCount / 2, 4));
            var tasks = new List<Task>();
            for (int i = 0; i < degree; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (!queue.IsEmpty && !ct.IsCancellationRequested)
                    {
                        if (!queue.TryDequeue(out var p)) break;
                        try { await ImportPathAsync(p, ct); }
                        catch (Exception ex) { _onError?.Invoke($"Import failed: {p} => {ex.Message}"); }
                    }
                }, ct));
            }
            return Task.WhenAll(tasks);
        }

        public async Task<List<string>> ImportPathAsync(string path, CancellationToken ct)
        {
            var created = new List<string>();
            if (ct.IsCancellationRequested) return created;
            if (Directory.Exists(path))
            {
                // Determine base name and parent image from root folder
                var root = path;
                var rootManifest = ReadManifest(root);
                var baseName = !string.IsNullOrWhiteSpace(rootManifest.Name) ? rootManifest.Name! : new DirectoryInfo(path).Name;
                string? parentImage = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(rootManifest.Image))
                    {
                        var imgRel = rootManifest.Image!.Replace('/', Path.DirectorySeparatorChar);
                        var imgAbs = Path.Combine(root, imgRel);
                        if (File.Exists(imgAbs)) parentImage = imgAbs;
                    }
                    if (string.IsNullOrWhiteSpace(parentImage))
                    {
                        parentImage = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                            .FirstOrDefault(f => AllowedImages.Contains(Path.GetExtension(f)));
                    }
                }
                catch { }
                created.AddRange(await ImportDirectoryAsync(path, ct, sourceName: baseName, parentImage: parentImage));
            }
            else if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".zip" or ".rar" or ".7z")
                {
                    created.AddRange(await ImportArchiveAsync(path, ct));
                }
                else
                {
                    // single file: treat as patch dropped
                    var dir = Path.GetDirectoryName(path)!;
                    created.AddRange(await ImportDirectoryAsync(dir, ct, sourceName: Path.GetFileNameWithoutExtension(path)));
                }
            }
            return created;
        }

        private async Task<List<string>> ImportArchiveAsync(string archivePath, CancellationToken ct)
        {
            var staging = NewTempDir("import_");
            var archiveName = Path.GetFileNameWithoutExtension(archivePath);
            var entryExpectations = new List<(string Key, string OutPath, long ExpectedSize, bool CheckSize)>();
            var failedEntries = new List<string>();
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (ct.IsCancellationRequested) break;
                    if (entry.IsDirectory) continue;
                    var key = entry.Key.Replace('\\', '/');
                    var name = Path.GetFileName(key);
                    if (!AllowFile(name)) continue;
                    var outPath = Path.Combine(staging, key);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    await CopyEntryAsync(entry, outPath, ct);
                    var expectedSize = SafeGetEntrySize(entry);
                    var checkSize = ShouldCheckSize(name, expectedSize);
                    if (checkSize)
                    {
                        try
                        {
                            var actual = new FileInfo(outPath).Length;
                            if (expectedSize >= 0 && actual != expectedSize)
                            {
                                failedEntries.Add(key);
                            }
                            else if (expectedSize < 0 && actual == 0)
                            {
                                failedEntries.Add(key);
                            }
                        }
                        catch { failedEntries.Add(key); }
                    }
                    entryExpectations.Add((key, outPath, expectedSize, checkSize));
                }
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"Archive read failed: {Path.GetFileName(archivePath)} => {ex.Message}");
            }

            // Fallback: if any entry failed size verification, use 7z.exe to extract
            if (failedEntries.Count > 0)
            {
                TrySevenZipExtract(archivePath, staging);
                // Verify again after 7z: compare each expected file at outPath
                var stillFailed = new List<string>();
                foreach (var e in entryExpectations.Where(t => t.CheckSize))
                {
                    try
                    {
                        var actual = new FileInfo(e.OutPath).Length;
                        if (e.ExpectedSize >= 0 && actual != e.ExpectedSize)
                        {
                            stillFailed.Add(e.Key);
                        }
                        else if (e.ExpectedSize < 0 && actual == 0)
                        {
                            stillFailed.Add(e.Key);
                        }
                    }
                    catch { stillFailed.Add(e.Key); }
                }
                if (stillFailed.Count > 0)
                {
                    _onError?.Invoke($"Archive appears corrupted or protected: {Path.GetFileName(archivePath)}. Failed entries: {stillFailed.Count}");
                    return new List<string>();
                }
            }
            // determine base name and parent image at staging root
            var rootManifest = ReadManifest(staging);
            var baseName = !string.IsNullOrWhiteSpace(rootManifest.Name) ? rootManifest.Name! : archiveName;
            string? parentImage = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(rootManifest.Image))
                {
                    var imgRel = rootManifest.Image!.Replace('/', Path.DirectorySeparatorChar);
                    var imgAbs = Path.Combine(staging, imgRel);
                    if (File.Exists(imgAbs)) parentImage = imgAbs;
                }
                if (string.IsNullOrWhiteSpace(parentImage))
                {
                    parentImage = Directory.EnumerateFiles(staging, "*.*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => AllowedImages.Contains(Path.GetExtension(f)));
                }
            }
            catch { }
            return await ImportDirectoryAsync(staging, ct, sourceName: baseName, parentImage: parentImage);
        }

        private static bool ShouldCheckSize(string name, long expectedSize)
        {
            // ignore known zero-length placeholders (*.patch_*.stream)
            var lname = name.ToLowerInvariant();
            if (lname.Contains(".patch_") && lname.EndsWith(".stream")) return false;
            // if archive couldn't provide size (<0), we still want actual>0
            return true;
        }

        private static long SafeGetEntrySize(IArchiveEntry entry)
        {
            try
            {
                var s = entry.Size;
                if (s < 0) return -1;
                return s;
            }
            catch
            {
                return -1;
            }
        }

        private void TrySevenZipExtract(string archivePath, string staging)
        {
            try
            {
                var sevenZip = LocateSevenZip();
                if (string.IsNullOrEmpty(sevenZip)) { _onError?.Invoke("7z.exe not found for fallback extraction"); return; }
                Directory.CreateDirectory(staging);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"x \"{archivePath}\" -y -aoa -o\"{staging}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit();
                if (p == null || p.ExitCode != 0)
                {
                    _onError?.Invoke($"7z extraction failed (exit {p?.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"7z fallback failed: {ex.Message}");
            }
        }

        private static string LocateSevenZip()
        {
            // Common locations
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            // try PATH
            try
            {
                var which = System.Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
                foreach (var dir in which)
                {
                    var p = Path.Combine(dir, "7z.exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return string.Empty;
        }

        private static bool AllowFile(string fileName)
        {
            if (string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase)) return true;
            var ext = Path.GetExtension(fileName);
            if (PatchRegex.IsMatch(fileName)) return true;
            if (AllowedImages.Contains(ext) || AllowedOthers.Contains(ext)) return true;
            return false;
        }

        private static async Task CopyEntryAsync(IArchiveEntry entry, string outPath, CancellationToken ct)
        {
            // Robust streaming copy for very large entries; avoid relying on entry.Size
            FileStream? outStream = null;
            Stream? inStream = null;
            try
            {
                inStream = entry.OpenEntryStream();
                outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan);
                var buffer = new byte[1 << 20]; // 1MB buffer
                int read;
                while ((read = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await outStream.FlushAsync(ct);
            }
            catch (OverflowException)
            {
                // Some archives report invalid/overflow sizes; try best-effort copy by reopening stream
                try
                {
                    inStream?.Dispose();
                    outStream?.Dispose();
                    using var in2 = entry.OpenEntryStream();
                    using var out2 = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan);
                    await in2.CopyToAsync(out2, 1 << 20, ct);
                    await out2.FlushAsync(ct);
                }
                catch { /* swallow and continue; caller may log */ }
            }
            catch (InvalidDataException)
            {
                // Corrupt entry; skip but do not crash the whole import
            }
            finally
            {
                try { inStream?.Dispose(); } catch { }
                try { outStream?.Dispose(); } catch { }
            }
        }

        private async Task<List<string>> ImportDirectoryAsync(string root, CancellationToken ct, string? sourceName = null, string? parentImage = null)
        {
            var created = new List<string>();
            var destRoot = SettingsService.GetModLibraryFolder();
            try { Directory.CreateDirectory(destRoot); } catch { }
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;
                var groups = ScanFileGroups(root, dir);
                if (groups.Count == 0) continue;
                // Try read manifest.json under current dir
                var manifest = ReadManifest(dir);
                var mod = new ModEntity
                {
                    Name = !string.IsNullOrWhiteSpace(manifest.Name)
                        ? manifest.Name!
                        : SanitizeName(ComposeOptionName(sourceName, new DirectoryInfo(dir).Name)),
                    FileGroups = groups,
                    SourcePath = dir,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                if (!string.IsNullOrWhiteSpace(manifest.Description)) mod.Description = manifest.Description;
                if (!string.IsNullOrWhiteSpace(manifest.Image)) mod.Image = manifest.Image;
                if (!string.IsNullOrWhiteSpace(manifest.IconPath)) mod.IconPath = manifest.IconPath;
                // Auto-assign tags based on name and manifest
                try
                {
                    var autoTags = ResolveTagsFromName(mod.Name);
                    if (!string.IsNullOrWhiteSpace(manifest.Name))
                    {
                        foreach (var t in ResolveTagsFromName(manifest.Name)) if (!autoTags.Contains(t)) autoTags.Add(t);
                    }
                    mod.Tags = autoTags;
                    HD2ModManager.Services.LogService.Info($"Import tags for '{mod.Name}': [{string.Join(";", autoTags)}]");
                }
                catch (Exception ex)
                {
                    HD2ModManager.Services.LogService.Error($"Auto tag error for '{mod.Name}': {ex.Message}");
                }
                // Copy files into configured library folder
                var targetName = SanitizeName(mod.Name);
                var destDir = Path.Combine(destRoot, targetName);
                destDir = EnsureUniqueFolder(destDir);
                TryCopyDirectory(dir, destDir);
                // store relative path for portability
                // store folder name only to avoid '../' segments
                mod.SourcePath = targetName;
                // Resolve and set image path to copied location
                try
                {
                    string? img = null;
                    if (!string.IsNullOrWhiteSpace(manifest.Image))
                    {
                        var rel = manifest.Image!.Replace('/', Path.DirectorySeparatorChar);
                        var abs = Path.Combine(destDir, rel);
                        if (File.Exists(abs)) img = abs;
                    }
                    if (string.IsNullOrWhiteSpace(img) && !string.IsNullOrWhiteSpace(manifest.IconPath))
                    {
                        var rel = manifest.IconPath!.Replace('/', Path.DirectorySeparatorChar);
                        var abs = Path.Combine(destDir, rel);
                        if (File.Exists(abs)) img = abs;
                    }
                    if (string.IsNullOrWhiteSpace(img))
                    {
                        var candidate = Directory.EnumerateFiles(destDir, "*.*", SearchOption.AllDirectories)
                            .FirstOrDefault(f => AllowedImages.Contains(Path.GetExtension(f)));
                        if (candidate != null) img = candidate;
                    }
                    // inherit parent image in root case too when missing
                    if (string.IsNullOrWhiteSpace(img) && !string.IsNullOrWhiteSpace(parentImage) && File.Exists(parentImage))
                    {
                        var fileName = Path.GetFileName(parentImage);
                        var targetImg = Path.Combine(destDir, fileName);
                        try { File.Copy(parentImage, targetImg, overwrite: false); } catch { }
                        if (File.Exists(targetImg)) img = targetImg;
                        HD2ModManager.Services.LogService.Info($"Image inherited(root) for '{mod.Name}' from parent='{parentImage}' => '{targetImg}' exists={File.Exists(targetImg)}");
                    }
                    if (!string.IsNullOrWhiteSpace(img)) mod.Image = img;
                    HD2ModManager.Services.LogService.Info($"Import(root) image for '{mod.Name}' => '{mod.Image}' (candidate='{img}')");
                }
                catch { }
                _library.Add(mod);
                created.Add(mod.Guid);
            }
            // also consider root itself
            var rootGroups = ScanFileGroups(root, root);
            if (rootGroups.Count > 0)
            {
                var manifest = ReadManifest(root);
                var mod = new ModEntity { Name = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name! : SanitizeName(!string.IsNullOrWhiteSpace(sourceName) ? sourceName! : new DirectoryInfo(root).Name), FileGroups = rootGroups, SourcePath = root };
                if (!string.IsNullOrWhiteSpace(manifest.Description)) mod.Description = manifest.Description;
                if (!string.IsNullOrWhiteSpace(manifest.Image)) mod.Image = manifest.Image;
                if (!string.IsNullOrWhiteSpace(manifest.IconPath)) mod.IconPath = manifest.IconPath;
                try
                {
                    var autoTags = ResolveTagsFromName(mod.Name);
                    if (!string.IsNullOrWhiteSpace(manifest.Name))
                    {
                        foreach (var t in ResolveTagsFromName(manifest.Name)) if (!autoTags.Contains(t)) autoTags.Add(t);
                    }
                    mod.Tags = autoTags;
                    HD2ModManager.Services.LogService.Info($"Import(root) tags for '{mod.Name}': [{string.Join(";", autoTags)}]");
                }
                catch (Exception ex)
                {
                    HD2ModManager.Services.LogService.Error($"Auto tag(root) error for '{mod.Name}': {ex.Message}");
                }
                var targetName = SanitizeName(mod.Name);
                var destDir = Path.Combine(destRoot, targetName);
                destDir = EnsureUniqueFolder(destDir);
                TryCopyDirectory(root, destDir);
                mod.SourcePath = targetName;
                // Resolve and set image path to copied location (root case)
                try
                {
                    string? img = null;
                    if (!string.IsNullOrWhiteSpace(manifest.Image))
                    {
                        var rel = manifest.Image!.Replace('/', Path.DirectorySeparatorChar);
                        var abs = Path.Combine(destDir, rel);
                        if (File.Exists(abs)) img = abs;
                    }
                    if (string.IsNullOrWhiteSpace(img) && !string.IsNullOrWhiteSpace(manifest.IconPath))
                    {
                        var rel = manifest.IconPath!.Replace('/', Path.DirectorySeparatorChar);
                        var abs = Path.Combine(destDir, rel);
                        if (File.Exists(abs)) img = abs;
                    }
                    if (string.IsNullOrWhiteSpace(img))
                    {
                        var candidate = Directory.EnumerateFiles(destDir, "*.*", SearchOption.AllDirectories)
                            .FirstOrDefault(f => AllowedImages.Contains(Path.GetExtension(f)));
                        if (candidate != null) img = candidate;
                    }
                    if (!string.IsNullOrWhiteSpace(img)) mod.Image = img;
                }
                catch { }
                _library.Add(mod);
                created.Add(mod.Guid);
            }
            return created;
        }

        private sealed class ManifestMeta
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? Image { get; set; }
            public string? IconPath { get; set; }
        }

        private static ManifestMeta ReadManifest(string folder)
        {
            var path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path)) return new ManifestMeta();
            try
            {
                var json = File.ReadAllText(path);
                var opts = new System.Text.Json.JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip };
                var meta = System.Text.Json.JsonSerializer.Deserialize<ManifestMeta>(json, opts);
                return meta ?? new ManifestMeta();
            }
            catch
            {
                // Clean and retry
                try
                {
                    var cleaned = CleanJson(File.ReadAllText(path));
                    var opts = new System.Text.Json.JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip };
                    var meta = System.Text.Json.JsonSerializer.Deserialize<ManifestMeta>(cleaned, opts);
                    return meta ?? new ManifestMeta();
                }
                catch { return new ManifestMeta(); }
            }
        }

        private static string CleanJson(string input)
        {
            var s = System.Text.RegularExpressions.Regex.Replace(input, @",\s*(\}|\])", "$1");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"//.*", string.Empty);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            return s;
        }

        private static List<FileGroup> ScanFileGroups(string rootFolder, string currentFolder)
        {
            var groups = new Dictionary<string, FileGroup>();
            foreach (var file in Directory.EnumerateFiles(currentFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var m = PatchRegex.Match(name);
                if (!m.Success) continue;
                var hex = m.Groups[1].Value;
                var patchN = int.TryParse(m.Groups[2].Value, out var n) ? n : 0;
                // For a mod built from currentFolder, paths should be relative to currentFolder, not the extraction root
                string relDir = string.Empty;
                var relPath = hex;
                var key = $"{relPath}.{patchN}";
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new FileGroup { RelativePath = relPath, HexPrefix = hex, PatchN = patchN, Files = new List<string>() };
                    groups[key] = g;
                }
                string relFile;
                try { relFile = Path.GetRelativePath(currentFolder, file).Replace('\\', '/'); }
                catch { relFile = name; }
                g.Files.Add(relFile);
            }
            return groups.Values.ToList();
        }

        private static string SanitizeName(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "ImportedMod";
            return name;
        }

        private static string ComposeOptionName(string? baseName, string optionName)
        {
            var b = (baseName ?? string.Empty).Trim();
            var o = (optionName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(b)) return o;
            if (string.IsNullOrWhiteSpace(o)) return b;
            return $"{b}-{o}";
        }

        private static System.Collections.Generic.List<string> ParsePreciseCodes(string name)
        {
            var tags = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(name)) return tags;
            try
            {
                // Match codes like FS-55, I-44, AR-23, MG-43, and variants with prefix A/MG-43
                var rx = new System.Text.RegularExpressions.Regex(@"\b([A-Z]{1,3}(?:/[A-Z]{1,3})?-\d{1,4})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(name))
                {
                    var code = m.Groups[1].Value.ToUpperInvariant();
                    if (!tags.Contains(code)) tags.Add(code);
                }
                // Also split by common separators like '&' and '、' if they contain codes
                var parts = name.Split(new[] { '&', '、', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var s = p.Trim();
                    if (rx.IsMatch(s))
                    {
                        var code = rx.Match(s).Groups[1].Value.ToUpperInvariant();
                        if (!tags.Contains(code)) tags.Add(code);
                    }
                }
                // Chinese phrase '替换' followed by codes
                var idx = name.IndexOf("替换", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var tail = name.Substring(idx + "替换".Length);
                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(tail))
                    {
                        var code = m.Groups[1].Value.ToUpperInvariant();
                        if (!tags.Contains(code)) tags.Add(code);
                    }
                }
            }
            catch { }
            return tags;
        }

        private static System.Collections.Generic.List<string> ResolveTagsFromName(string name)
        {
            var result = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(name)) return result;
            var tokens = new System.Collections.Generic.List<string>();
            try
            {
                // collect codes first
                tokens.AddRange(ParsePreciseCodes(name));
                // then collect textual tokens (english/chinese words) split by common separators and punctuation
                var cleaned = name.Replace('[', ' ').Replace(']', ' ').Replace('（', ' ').Replace('）', ' ');
                var parts = cleaned.Split(new[] { '&', '、', ',', '/', '-', ' ', '>' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim())
                                   .Where(s => s.Length > 0)
                                   .ToList();
                foreach (var p in parts)
                {
                    // filter obvious non-tag words
                    if (string.Equals(p, "替换", StringComparison.OrdinalIgnoreCase)) continue;
                    tokens.Add(p);
                }
            }
            catch { }
            // Map tokens to catalog canonical item names
            try
            {
                var catalog = HD2ModManager.Services.TagCatalogService.Instance.GetAll();
                // normalized source for loose contains
                var src = NormalizeForLooseMatch(name);
                foreach (var item in catalog)
                {
                    bool hit = false;
                    if (!string.IsNullOrWhiteSpace(item.Code)) hit |= src.Contains(item.Code.ToLowerInvariant());
                    if (!string.IsNullOrWhiteSpace(item.EnglishName)) hit |= src.Contains(item.EnglishName.ToLowerInvariant());
                    if (!string.IsNullOrWhiteSpace(item.ChineseName)) hit |= src.Contains(NormalizeForLooseMatch(item.ChineseName));
                    if (!string.IsNullOrWhiteSpace(item.Name)) hit |= src.Contains(item.Name.ToLowerInvariant());
                    // also check tokens exact equals to cover fragmented cases
                    if (!hit)
                    {
                        foreach (var t in tokens)
                        {
                            var tt = t.ToLowerInvariant();
                            if (!string.IsNullOrWhiteSpace(item.Code) && string.Equals(item.Code.ToLowerInvariant(), tt)) { hit = true; break; }
                            if (!string.IsNullOrWhiteSpace(item.EnglishName) && string.Equals(item.EnglishName.ToLowerInvariant(), tt)) { hit = true; break; }
                            if (!string.IsNullOrWhiteSpace(item.ChineseName) && string.Equals(NormalizeForLooseMatch(item.ChineseName), NormalizeForLooseMatch(t))) { hit = true; break; }
                            if (!string.IsNullOrWhiteSpace(item.Name) && string.Equals(item.Name.ToLowerInvariant(), tt)) { hit = true; break; }
                        }
                    }
                    if (hit)
                    {
                        var canonical = item.Name;
                        if (!result.Contains(canonical)) result.Add(canonical);
                    }
                }
            }
            catch { }
            return result;
        }

        private static string NormalizeForLooseMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.ToLowerInvariant();
            // convert common full-width punctuations to half-width spaces
            s = s.Replace('（', ' ').Replace('）', ' ').Replace('【', ' ').Replace('】', ' ');
            s = s.Replace('[', ' ').Replace(']', ' ');
            return s;
        }

        private static string EnsureUniqueFolder(string baseDir)
        {
            var dir = baseDir;
            int i = 1;
            while (Directory.Exists(dir))
            {
                dir = baseDir + "_" + i;
                i++;
            }
            return dir;
        }

        private static void TryCopyDirectory(string src, string dest)
        {
            try
            {
                if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
                foreach (var dirPath in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, dirPath);
                    var target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(target);
                }
                foreach (var filePath in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, filePath);
                    var target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(filePath, target, overwrite: true);
                }
            }
            catch { }
        }

        private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "HD2ModManager_Temp");
        private const string MarkerName = ".mmtemp";
        private static void EnsureTempRoot()
        {
            try { if (!Directory.Exists(TempRoot)) Directory.CreateDirectory(TempRoot); }
            catch { }
        }
        private static string ShortRand()
        {
            var s = Path.GetRandomFileName().Replace(".", string.Empty);
            return s.Length <= 4 ? s : s[..4];
        }
        private static string NewTempDir(string prefix)
        {
            EnsureTempRoot();
            var dir = Path.Combine(TempRoot, prefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + ShortRand());
            try { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, MarkerName), DateTime.UtcNow.ToString("O")); }
            catch { }
            return dir;
        }
    }
}
