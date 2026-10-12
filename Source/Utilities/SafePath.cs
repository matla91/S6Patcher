using System;
using System.IO;
using System.Linq;

namespace S6Patcher.Source.Utilities
{
    internal static class SafePath
    {
        public static string InDirectory(string Root, string Relative)
        {
            // Interpret Windows separators even when tests/builds run on Linux.
            if (string.IsNullOrWhiteSpace(Relative) || Relative.StartsWith('/') || Relative.StartsWith('\\')
                || Relative.IndexOfAny([':', '\0', '<', '>', '"', '|', '?', '*']) >= 0)
                throw new InvalidDataException("Invalid relative mod path: " + Relative);
            string[] Parts = Relative.Split(['/', '\\']);
            if (Parts.Any(Part => Part.Length == 0 || Part == "." || Part == ".." || Part.EndsWith('.') || Part.EndsWith(' ')))
                throw new InvalidDataException("Mod path escapes or aliases its directory: " + Relative);
            string Current = Path.GetFullPath(Root);
            foreach (string Part in Parts)
            {
                Current = Path.Combine(Current, Part);
                if ((File.Exists(Current) || Directory.Exists(Current)) && (File.GetAttributes(Current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Mod path traverses a symbolic link: " + Relative);
            }
            return Current;
        }
    }
}
