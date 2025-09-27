using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace LiberTeaManager
{
    public static class ManifestGenerator
    {
        private static string MergePath(string? oldVal, string newVal)
        {
            if (string.IsNullOrWhiteSpace(oldVal)) return newVal;
            if (!oldVal.Contains('/') && newVal.Contains('/')) return newVal;
            return oldVal;
        }

        /// <summary>
        /// 总是新建manifest.json，优先填充旧数据，无则自动生成，并补充文件组信息
        /// </summary>
        public static MainModItem EnsureManifestWithFileGroups(string modName, string tempDir)
        {
            string manifestPath = Path.Combine(tempDir, "manifest.json");
            MainModItem oldMod = null;

            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                json = System.Text.RegularExpressions.Regex.Replace(json, @",(\s*[}\]])", "$1");
                oldMod = JsonSerializer.Deserialize<MainModItem>(json);
            }

            var newMod = GenerateManifest(modName, tempDir);

            if (oldMod != null)
            {
                newMod.Name = oldMod.Name;
                newMod.Description = MergePath(oldMod.Description, newMod.Description);
                newMod.Guid = oldMod.Guid;
                newMod.IconPath = MergePath(oldMod.IconPath, newMod.IconPath);
                newMod.Image = MergePath(oldMod.Image, newMod.Image);
                newMod.Url = string.IsNullOrWhiteSpace(oldMod.Url) ? newMod.Url : oldMod.Url; // 保留旧的 Url
                newMod.RootModName = newMod.Name; // 统一

                if (newMod.Options != null && oldMod.Options != null)
                {
                    foreach (var newOpt in newMod.Options)
                    {
                        var oldOpt = oldMod.Options.FirstOrDefault(o => o.Name == newOpt.Name);
                        if (oldOpt == null && newOpt.Include != null)
                        {
                            oldOpt = oldMod.Options.FirstOrDefault(o => o.Include != null && o.Include.Intersect(newOpt.Include).Any());
                        }
                        if (oldOpt != null)
                        {
                            newOpt.Description = MergePath(oldOpt.Description, newOpt.Description);
                            newOpt.Image = MergePath(oldOpt.Image, newOpt.Image);
                            newOpt.IconPath = MergePath(oldOpt.IconPath, newOpt.IconPath);
                            newOpt.Include = oldOpt.Include ?? newOpt.Include;
                            newOpt.Url = string.IsNullOrWhiteSpace(oldOpt.Url) ? newOpt.Url : oldOpt.Url; // 保留 Url
                            newOpt.RootModName = newMod.Name;

                            if (newOpt.SubOptions != null)
                            {
                                if (oldOpt.SubOptions != null)
                                {
                                    foreach (var newSub in newOpt.SubOptions)
                                    {
                                        var oldSub = oldOpt.SubOptions.FirstOrDefault(s => s.Name == newSub.Name);
                                        if (oldSub != null)
                                        {
                                            newSub.Description = MergePath(oldSub.Description, newSub.Description);
                                            newSub.Image = MergePath(oldSub.Image, newSub.Image);
                                            newSub.IconPath = MergePath(oldSub.IconPath, newSub.IconPath);
                                            newSub.Include = oldSub.Include ?? newSub.Include;
                                            newSub.Url = string.IsNullOrWhiteSpace(oldSub.Url) ? newSub.Url : oldSub.Url; // 保留 Url
                                            newSub.RootModName = newMod.Name;
                                        }
                                    }
                                    foreach (var oldSub in oldOpt.SubOptions)
                                    {
                                        if (!newOpt.SubOptions.Any(s => s.Name == oldSub.Name))
                                        {
                                            newOpt.SubOptions.Add(new SubOptionItem
                                            {
                                                Name = oldSub.Name,
                                                Description = oldSub.Description,
                                                Image = oldSub.Image,
                                                IconPath = oldSub.IconPath,
                                                Include = oldSub.Include?.ToList() ?? new List<string>(),
                                                RootModName = newMod.Name,
                                                FileGroups = new List<ModFileGroup>(),
                                                IsSelected = false,
                                                Url = oldSub.Url
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var outJson = JsonSerializer.Serialize(newMod, options);
            File.WriteAllText(manifestPath, outJson);

            return newMod;
        }

        /// <summary>
        /// 自动生成manifest.json并返回主Mod对象
        /// </summary>
        public static MainModItem GenerateManifest(string modName, string targetDir)
        {
            var mainMod = new MainModItem
            {
                Name = modName,
                Description = "",
                Guid = Guid.NewGuid(),
                IconPath = FindImageFile(targetDir),
                IsSelected = false,
                Options = new ObservableCollection<OptionItem>(),
                FileGroups = GetModFileGroups(targetDir, targetDir),
                RootModName = modName,
                Url = string.Empty
            };

            foreach (var firstDir in Directory.GetDirectories(targetDir))
            {
                var optionName = Path.GetFileName(firstDir);
                var optionImageFile = FindImageFile(firstDir);
                var optionImageRel = string.IsNullOrEmpty(optionImageFile) ? "" : $"{optionName}/{optionImageFile}";

                var option = new OptionItem
                {
                    Name = optionName,
                    Description = "",
                    Image = optionImageRel,
                    IconPath = optionImageRel,
                    IsSelected = false,
                    SubOptions = new ObservableCollection<SubOptionItem>(),
                    Include = new List<string> { optionName },
                    FileGroups = GetModFileGroups(targetDir, firstDir),
                    RootModName = modName,
                    Url = string.Empty
                };

                foreach (var secondDir in Directory.GetDirectories(firstDir))
                {
                    var subName = Path.GetFileName(secondDir);
                    var subImageFile = FindImageFile(secondDir);
                    var subImageRel = string.IsNullOrEmpty(subImageFile) ? "" : $"{optionName}/{subName}/{subImageFile}";

                    var subOption = new SubOptionItem
                    {
                        Name = subName,
                        Description = "",
                        Image = subImageRel,
                        IconPath = subImageRel,
                        IsSelected = false,
                        Include = new List<string> { optionName + "/" + subName },
                        FileGroups = GetModFileGroups(targetDir, secondDir),
                        RootModName = modName,
                        Url = string.Empty
                    };
                    option.SubOptions.Add(subOption);
                }
                mainMod.Options.Add(option);
            }
            var options2 = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(mainMod, options2);
            File.WriteAllText(Path.Combine(targetDir, "manifest.json"), json);

            return mainMod;
        }

        /// <summary>
        /// 选择图片：优先匹配 icon/cover/logo/preview/thumbnail/thumb 命名；否则取首个。返回相对目录的文件名（仅文件名）。
        /// </summary>
        internal static string? FindImageFile(string dir)
        {
            try
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();
                if (files.Count == 0) return "";
                if (files.Count == 1) return files[0]!;
                string[] prefs = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                var picked = files.FirstOrDefault(f => prefs.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase)));
                return picked ?? files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
            catch { return ""; }
        }

        /// <summary>
        /// 生成文件组（只保存相对路径）
        /// </summary>
        public static List<ModFileGroup> GetModFileGroups(string rootFolder, string currentFolder)
        {
            var groups = new Dictionary<string, ModFileGroup>();
            var files = Directory.GetFiles(currentFolder, "*", SearchOption.TopDirectoryOnly);
            var regex = new Regex(
                @"([a-fA-F0-9]{16})\.patch_(\d+)(\.stream|\.gpu_resources)?$",
                RegexOptions.Compiled);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var match = regex.Match(name);
                if (match.Success)
                {
                    var hex = match.Groups[1].Value;
                    var patchN = int.Parse(match.Groups[2].Value);
                    var relDir = Path.GetRelativePath(rootFolder, currentFolder).Replace('\\', '/');
                    var relPath = string.IsNullOrEmpty(relDir) ? hex : $"{relDir}/{hex}";
                    var key = $"{relPath}.{patchN}";
                    if (!groups.ContainsKey(key))
                    {
                        groups[key] = new ModFileGroup
                        {
                            RelativePath = relPath,
                            HexPrefix = hex,
                            PatchN = patchN
                        };
                    }
                    var relFile = Path.GetRelativePath(rootFolder, file).Replace('\\', '/');
                    groups[key].Files.Add(relFile);
                }
            }
            return new List<ModFileGroup>(groups.Values);
        }
    }

    public static partial class ManifestGeneratorExtensions { }
}
