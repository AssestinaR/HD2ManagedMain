using System.Collections.Generic;

namespace HD2ModManager.Models
{
    public class Profile
    {
        public List<ProfileEntry> Entries { get; set; } = new();
    }

    public class ProfileEntry
    {
        public string Guid { get; set; } = string.Empty;
        public int Marker { get; set; } = 0; // -1,0,1
        public List<string> After { get; set; } = new();
        public List<string> Before { get; set; } = new();
    }
}
