using System;
using System.Collections.Generic;

namespace HD2ModManager.Models
{
    public class ModEntity
    {
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Image { get; set; }
        public string? ThumbImage { get; set; }
        public string? IconPath { get; set; }
        public string? Url { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<FileGroup> FileGroups { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? SourcePath { get; set; }
    }
}
