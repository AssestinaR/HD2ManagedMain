using System.Collections.Generic;

namespace HD2ModManager.Models
{
    public class FileGroup
    {
        public string HexPrefix { get; set; } = string.Empty;
        public int PatchN { get; set; }
        public List<string> Files { get; set; } = new();
        public string RelativePath { get; set; } = string.Empty;
    }
}
