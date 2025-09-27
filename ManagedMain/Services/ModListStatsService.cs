using System;
using System.IO;
using System.Text.Json;

namespace ManagedMain.Services
{
    public class ModListStatsService
    {
        public (int modCount, int patchGroupCount, int enabledPatchGroupCount) ComputeStats(string profileRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profileRoot)) return (0, 0, 0);
                var modlistPath = Path.Combine(profileRoot, "modlist.json");
                if (!File.Exists(modlistPath)) return (0, 0, 0);
                using var doc = JsonDocument.Parse(File.ReadAllText(modlistPath));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return (0, 0, 0);
                int modCount = 0;
                int totalGroups = 0;
                int enabledGroups = 0;
                foreach (var main in root.EnumerateArray())
                {
                    modCount++;
                    CountForItem(main, isEnabled: IsEnabled(main), ref totalGroups, ref enabledGroups);
                    if (main.TryGetProperty("Options", out var options) && options.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var opt in options.EnumerateArray())
                        {
                            CountForItem(opt, isEnabled: IsEnabled(opt), ref totalGroups, ref enabledGroups);
                            if (opt.TryGetProperty("SubOptions", out var subs) && subs.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var sub in subs.EnumerateArray())
                                {
                                    CountForItem(sub, isEnabled: IsEnabled(sub), ref totalGroups, ref enabledGroups);
                                }
                            }
                        }
                    }
                }
                return (modCount, totalGroups, enabledGroups);
            }
            catch { return (0, 0, 0); }
        }

        private static bool IsEnabled(JsonElement elem)
        {
            if (elem.TryGetProperty("Enabled", out var en))
            {
                // 1  ”Œ™∆Ù”√
                if (en.ValueKind == JsonValueKind.Number && en.TryGetInt32(out var v)) return v == 1;
                if (en.ValueKind == JsonValueKind.True) return true;
            }
            return false;
        }

        private static void CountForItem(JsonElement item, bool isEnabled, ref int total, ref int enabled)
        {
            if (item.TryGetProperty("FileGroups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                int c = 0;
                foreach (var g in groups.EnumerateArray()) c++;
                total += c;
                if (isEnabled) enabled += c;
            }
        }
    }
}
