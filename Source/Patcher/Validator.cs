using S6Patcher.Source.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace S6Patcher.Source.Patcher
{
    internal class Validator(FileStream Stream)
    {
        private const string ExpectedVersion = "1, 71, 4289, 0";
        private readonly Dictionary<uint, execID> Mapping = new()
        {
            {0x6ECADC, execID.OV},
            {0xF531A4, execID.HE_UBISOFT},
            {0xF545A4, execID.HE_STEAM},
            {0x6D06A8, execID.ED},
        };

        public execID Validate()
        {
            // The game executables are PE32/i386 images, even when the patcher itself is 64-bit.
            using BinaryReader Reader = new(Stream, Encoding.UTF8, leaveOpen: true);
            if (Stream.Length < 64 || Reader.ReadUInt16() != 0x5A4D)
                throw new InvalidDataException("Unsupported executable: DOS/PE header is missing.");
            Stream.Position = 0x3C;
            uint PE = Reader.ReadUInt32();
            if (PE > Stream.Length - 26) throw new InvalidDataException("Unsupported executable: truncated PE header.");
            Stream.Position = PE;
            if (Reader.ReadUInt32() != 0x4550 || Reader.ReadUInt16() != 0x14C)
                throw new InvalidDataException("Unsupported executable: expected a PE32/i386 game image.");
            ushort Sections = Reader.ReadUInt16();
            Stream.Position = PE + 20;
            ushort OptionalSize = Reader.ReadUInt16();
            Stream.Position = PE + 24;
            if (Sections == 0 || Sections > 96 || OptionalSize < 96 || Reader.ReadUInt16() != 0x10B
                || PE + 24L + OptionalSize + Sections * 40L > Stream.Length)
                throw new InvalidDataException("Unsupported executable: invalid PE32 section table.");
            execID ID = ValidateExecutableID();
            if (ID == execID.NONE)
            {
                throw new InvalidDataException("Unsupported executable. Expected game/editor version 1.71.4289 (original, History Ubisoft, or unpacked History Steam). No patch was applied.");
            }

            if ((ID == execID.OV || ID == execID.HE_STEAM) && !IsSteamExecutableValid(ID))
            {
                throw new Exception("Steam executable invalid, has to be unpacked with the tool \"Steamless\" before using the S6Patcher!\nAborting ...");
            }

            return ID;
        }

        private execID ValidateExecutableID()
        {
            Logger.Instance.Log("Called with Stream: " + Stream.Name);

            int Size = Encoding.Unicode.GetByteCount(ExpectedVersion);
            byte[] Result = new byte[Size];
            execID Match = execID.NONE;

            foreach (var Element in Mapping)
            {
                if (Element.Key > Stream.Length - Size)
                {
                    Logger.Instance.Log(Element.Value.ToString() + " Stream Length smaller than Mapping Index: " + Stream.Length.ToString());
                    continue;
                }

                Stream.Position = Element.Key;
                Stream.ReadExactly(Result);

                string Version = Encoding.Unicode.GetString(Result)[..ExpectedVersion.Length];
                Logger.Instance.Log(Element.Value.ToString() + " Read from File: " + Version);

                if (Version == ExpectedVersion)
                {
                    Logger.Instance.Log("Valid executable! execID: " + Element.Value.ToString());
                    if (Match != execID.NONE) throw new InvalidDataException("Ambiguous executable version signatures. No patch was applied.");
                    Match = Element.Value;
                };
            }

            if (Match == execID.NONE) Logger.Instance.Log("No valid executable was found!");
            return Match;
        }

        private bool IsSteamExecutableValid(execID? ID)
        {
            byte[] Identifier = [0x84, 0xC0];
            uint[] Addresses = [0x00C044, 0x1E0F08]; // 0 = OV, 1 = Steam HE

            byte[] Result = new byte[Identifier.Length];
            Stream.Position = Addresses[ID == execID.OV ? 0 : 1];
            Stream.ReadExactly(Result);

            return Identifier.SequenceEqual(Result);
        }
    }
}
