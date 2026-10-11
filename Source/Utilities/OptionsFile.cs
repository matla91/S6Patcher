using System;
using System.Collections.Generic;

namespace S6Patcher.Source.Utilities
{
    internal static class OptionsFile
    {
        public static bool HasKey(List<string> Lines, string Section, string Key)
        {
            bool InSection = false;
            foreach (string Line in Lines)
            {
                string Text = Line.Trim();
                if (Text.StartsWith('[')) { InSection = string.Equals(Text, Section, StringComparison.OrdinalIgnoreCase); continue; }
                int Index = Text.IndexOf('=');
                if (InSection && Index >= 0 && string.Equals(Text[..Index].Trim(), Key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static void Set(List<string> Lines, string Section, string Key, string Value)
        {
            int Start = Lines.FindIndex(Line => string.Equals(Line.Trim(), Section, StringComparison.OrdinalIgnoreCase));
            if (Start < 0) { Lines.Add(Section); Start = Lines.Count - 1; }
            int End = Start + 1;
            while (End < Lines.Count && !Lines[End].TrimStart().StartsWith('[')) End++;
            for (int i = End - 1; i > Start; i--)
            {
                string Line = Lines[i].Trim();
                int EqualsIndex = Line.IndexOf('=');
                if (EqualsIndex >= 0 && string.Equals(Line[..EqualsIndex].Trim(), Key, StringComparison.OrdinalIgnoreCase))
                    Lines.RemoveAt(i);
            }
            Lines.Insert(Start + 1, Key + "=" + Value);
        }
    }
}
