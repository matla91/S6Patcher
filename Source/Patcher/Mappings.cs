using S6Patcher.Source.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace S6Patcher.Source.Patcher
{
    public class Mappings(execID _ID, BinaryParser _Parser)
    {
        public static readonly Dictionary<string, string> UIFeatures = new()
        {
            {"cbHighTextures",      "HTS"},
            {"cbScalingPlacing",    "SCP"},
            {"cbEntityLimits",      "ENL"},
            {"cbMapBorder",         "BMB"},
            {"cbDevMode",           "DVM"},
            {"cbAllEntities",       "AET"},
            {"cbScriptBugFixes",    "SBF"},
            {"cbLimitedEdition",    "LME"},
        };

        public Dictionary<uint, byte[]> GetMapping(List<string> Features)
        {
            Dictionary<uint, byte[]> Mapping = [];
            foreach (string Feature in Features)
            {
                foreach (var Entry in _Parser.ParseBinaryWrapper(_ID, Feature))
                {
                    if (Mapping.TryGetValue(Entry.Key, out byte[] Existing) && !Existing.SequenceEqual(Entry.Value))
                        throw new InvalidOperationException($"Conflicting patches at 0x{Entry.Key:X}.");
                    Mapping[Entry.Key] = Entry.Value;
                }
            }

            return Mapping;
        }

        public Dictionary<uint, byte[]> GetTextureResolutionMapping(uint Resolution)
        {
            Dictionary<uint, byte[]> Mapping = _Parser.ParseBinaryWrapper(_ID, "GTS");

            if (Resolution < 4) throw new ArgumentOutOfRangeException(nameof(Resolution));
            var Markers = Mapping.ToArray();
            foreach (var Element in Markers)
            {
                if (Element.Value.Length != 1 || Element.Value[0] > 2)
                    throw new InvalidOperationException("Invalid texture-resolution marker.");
                Mapping[Element.Key] = BitConverter.GetBytes(Resolution / (1U << Element.Value[0]));
            }

            return Mapping;
        }

        public Dictionary<uint, byte[]> GetAutoSaveMapping(double Time)
        {
            if (!double.IsFinite(Time) || Time < 0 || !double.IsFinite(Time * 60000)) throw new ArgumentOutOfRangeException(nameof(Time));
            Dictionary<uint, byte[]> Mapping = _Parser.ParseBinaryWrapper(_ID, "ATS");
            UpdateMappingValueAtIndex(Mapping, 0x00, Time == 0.0 ? [0xEB] : [0x76]);
            UpdateMappingValueAtIndex(Mapping, 0x01, BitConverter.GetBytes(Time * 60000));

            return Mapping;
        }

        public Dictionary<uint, byte[]> GetZoomLevelMapping(double ZoomLevel, float ClutterFarDistance)
        {
            if (!double.IsFinite(ZoomLevel) || ZoomLevel <= 0 || !float.IsFinite(ClutterFarDistance)
                || ClutterFarDistance <= 0 || !float.IsFinite(ClutterFarDistance + 4800f)) throw new ArgumentOutOfRangeException(nameof(ZoomLevel));
            byte[] Level = _ID == execID.OV ? BitConverter.GetBytes(ZoomLevel) : BitConverter.GetBytes(ClutterFarDistance);
            Dictionary<uint, byte[]> Mapping = _Parser.ParseBinaryWrapper(_ID, "ZLM");
            UpdateMappingValueAtIndex(Mapping, 0x00, Level);
            UpdateMappingValueAtIndex(Mapping, 0x01, BitConverter.GetBytes(ClutterFarDistance + 4800f));

            return Mapping;
        }

        public Dictionary<uint, byte[]> GetDocumentsFolderMapping(string FolderPath)
        {
            Dictionary<uint, byte[]> Mapping = _Parser.ParseBinaryWrapper(_ID, "DCP");
            byte[] UnicodePath = Encoding.Unicode.GetBytes(FolderPath + "\0");
            if (string.IsNullOrWhiteSpace(FolderPath) || FolderPath.Contains('\0') || UnicodePath.Length > 0xB4)
                throw new ArgumentException("Documents path exceeds its 180-byte UTF-16 buffer, or is invalid.", nameof(FolderPath));
            UpdateMappingValueAtIndex(Mapping, 0xCC, UnicodePath);

            return Mapping;
        }

        public Dictionary<uint, byte[]> GetScriptFileMapping() => _Parser.ParseBinaryWrapper(_ID, "SFM");
        public Dictionary<uint, byte[]> GetEasyDebugMapping() => _Parser.ParseBinaryWrapper(_ID, "EDG");
        public Dictionary<uint, byte[]> GetModloaderMapping() => _Parser.ParseBinaryWrapper(_ID, "MDL");

        private void UpdateMappingValueAtIndex(Dictionary<uint, byte[]> Mapping, byte Value, byte[] Replacement)
        {
            var Matches = Mapping.Where(Element => Element.Value.Length == 1 && Element.Value[0] == Value).ToArray();
            if (Matches.Length != 1) throw new InvalidOperationException($"Expected one marker 0x{Value:X2}; found {Matches.Length}.");
            uint Key = Matches[0].Key;
            Mapping[Key] = Replacement;
        }
    }
}
