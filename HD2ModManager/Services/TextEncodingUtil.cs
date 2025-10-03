using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HD2ModManager.Services
{
    public static class TextEncodingUtil
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Encoding Gb18030 = Encoding.GetEncoding(54936);

        public static string ReadAllTextDetect(string path)
        {
            try
            {
                using var sr = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return sr.ReadToEnd();
            }
            catch { }
            try
            {
                return File.ReadAllText(path, Utf8NoBom);
            }
            catch { }
            try
            {
                return File.ReadAllText(path, Gb18030);
            }
            catch { }
            return File.ReadAllText(path);
        }

        public static IEnumerable<string> ReadAllLinesDetect(string path)
        {
            var list = new List<string>();
            try
            {
                using var sr = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine() ?? string.Empty;
                    list.Add(line);
                }
                return list;
            }
            catch { }
            try
            {
                foreach (var line in File.ReadAllLines(path, Utf8NoBom)) list.Add(line);
                return list;
            }
            catch { }
            try
            {
                foreach (var line in File.ReadAllLines(path, Gb18030)) list.Add(line);
                return list;
            }
            catch { }
            foreach (var line in File.ReadAllLines(path)) list.Add(line);
            return list;
        }

        private static bool LooksCorrupted(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int rep = 0;
            foreach (var ch in s)
            {
                if (ch == '\uFFFD') rep++;
            }
            return rep > 0 && rep * 5 > s.Length;
        }
    }
}
