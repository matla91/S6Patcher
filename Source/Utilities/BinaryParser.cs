using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace S6Patcher.Source.Utilities
{
    public class FileDataEntry
    {
        public bool IsDataFile;
        public string BBArchiveName;
        public string FilePath;
        public UInt32 OriginalFileCRC;
        public UInt32 EntryCount;
        public Dictionary<UInt32, byte[]> Data;
    }

    public class BinaryParser : IDisposable
    {
        private readonly byte[] Magic = Encoding.UTF8.GetBytes("EMX");
        private readonly BinaryReader GlobalReader = null;
        private readonly uint BlockOffset = 0x0;

        public void Dispose() => GlobalReader?.Dispose();

        public BinaryParser(Stream BinaryStream)
        {
            if (BinaryStream == null || BinaryStream.CanRead == false)
            {
                throw new Exception("[ERROR] Invalid binary stream.");
            }

            BlockOffset = (uint)(Magic.Length + sizeof(byte));
            GlobalReader = new BinaryReader(GetDecompressedStream(BinaryStream));
            BinaryStream.Dispose();

            if (!IsValidBinaryFile() || GetFileVersion() != 1)
            {
                Dispose();
                throw new Exception("[ERROR] Invalid binary file.");
            }
        }

        public BinaryParser(string Definition)
        {
            Stream BinaryStream = Utility.GetEmbeddedResourceDefinition(Definition);
            if (BinaryStream == null || BinaryStream.CanRead == false)
            {
                throw new Exception("[ERROR] Invalid binary stream.");
            }

            BlockOffset = (uint)(Magic.Length + sizeof(byte));
            GlobalReader = new BinaryReader(GetDecompressedStream(BinaryStream));
            BinaryStream.Dispose();

            if (!IsValidBinaryFile() || GetFileVersion() != 1)
            {
                Dispose();
                throw new Exception("[ERROR] Invalid binary file.");
            }
        }

        private MemoryStream GetDecompressedStream(Stream BinaryStream)
        {
            BinaryStream.Seek(0, SeekOrigin.Begin);

            using GZipStream DecompressionStream = new(BinaryStream, CompressionMode.Decompress);
            MemoryStream DecompressedStream = new();

            const int Limit = 16 * 1024 * 1024;
            byte[] Buffer = new byte[8192];
            int Read;
            while ((Read = DecompressionStream.Read(Buffer)) != 0)
            {
                if (DecompressedStream.Length + Read > Limit)
                {
                    DecompressedStream.Dispose();
                    throw new InvalidDataException("Definition exceeds the 16 MiB decompression limit.");
                }
                DecompressedStream.Write(Buffer, 0, Read);
            }
            DecompressedStream.Seek(0, SeekOrigin.Begin);

            return DecompressedStream;
        }

        private bool IsValidBinaryFile()
        {
            byte[] Result = new byte[Magic.Length];
            GlobalReader.BaseStream.Seek(0, SeekOrigin.Begin);
            GlobalReader.Read(Result, 0, Result.Length);
            return GlobalReader.BaseStream.Length >= BlockOffset && Result.SequenceEqual(Magic);
        }

        public byte GetFileVersion()
        {
            GlobalReader.BaseStream.Position = Magic.Length;
            return GlobalReader.ReadByte();
        }

        public Dictionary<uint, byte[]> ParseBinaryWrapper(execID ID, string Identifier)
        {
            if (!ParseBinaryFileContent((byte)ID, out Dictionary<uint, byte[]> Mapping, Identifier))
            {
                throw new Exception("[ERROR] Could not parse binary file content.");
            }

            return Mapping;
        }

        private bool ParseBinaryFileContent(byte Identifier, out Dictionary<UInt32, byte[]> Result, string ID = "")
        {
            Result = [];
            byte[] IDBytes = (ID == "") ? [0x0, 0x0, 0x0] : Encoding.UTF8.GetBytes(ID);
            if (IDBytes.Length != 3) throw new InvalidDataException("Feature identifiers must contain exactly three bytes.");

            GlobalReader.BaseStream.Position = BlockOffset;
            long End = GlobalReader.BaseStream.Length;
            while (true)
            {
                if (End - GlobalReader.BaseStream.Position < 5) return false;
                byte BlockID = GlobalReader.ReadByte();
                uint Size = GlobalReader.ReadUInt32();
                if (Size > End - GlobalReader.BaseStream.Position) throw new InvalidDataException("Definition block is truncated.");
                if (BlockID == Identifier) { End = GlobalReader.BaseStream.Position + Size; break; }
                GlobalReader.BaseStream.Position += Size;
            }

            UInt32 Address;
            byte[] Data;
            byte[] EntryID;
            while (GlobalReader.BaseStream.Position < End)
            {
                if (End - GlobalReader.BaseStream.Position < 9) throw new InvalidDataException("Definition entry header is truncated.");
                EntryID = GlobalReader.ReadBytes(3);
                Address = GlobalReader.ReadUInt32();
                ushort Length = GlobalReader.ReadUInt16();
                if (Length == 0 || Length > End - GlobalReader.BaseStream.Position) throw new InvalidDataException("Definition entry data is truncated or empty.");
                Data = GlobalReader.ReadBytes(Length);
                if (EntryID.SequenceEqual(IDBytes) && !Result.TryAdd(Address, Data))
                    throw new InvalidDataException($"Duplicate patch offset 0x{Address:X}.");
            }

            return true;
        }

        public List<FileDataEntry> ParseFileData()
        {
            List<FileDataEntry> Result = [];
            GlobalReader.BaseStream.Position = BlockOffset;

            while (GlobalReader.BaseStream.Position < GlobalReader.BaseStream.Length)
            {
                string BBArchiveName = Encoding.UTF8.GetString(ReadSizedBytes());
                string FilePath = Encoding.UTF8.GetString(ReadSizedBytes());

                FileDataEntry Entry = new()
                {
                    IsDataFile = true,
                    BBArchiveName = BBArchiveName,
                    FilePath = FilePath,
                    OriginalFileCRC = GlobalReader.ReadUInt32(),
                    EntryCount = GlobalReader.ReadUInt32(),
                    Data = []
                };
                if (Entry.EntryCount > (GlobalReader.BaseStream.Length - GlobalReader.BaseStream.Position) / 8)
                    throw new InvalidDataException("FileData entry count exceeds the available data.");

                for (int i = 0; i < Entry.EntryCount; i++)
                {
                    UInt32 Address = GlobalReader.ReadUInt32();
                    byte[] Data = ReadSizedBytes();
                    if (Data.Length == 0 || !Entry.Data.TryAdd(Address, Data))
                        throw new InvalidDataException("Empty or duplicate FileData operation.");
                }

                Result.Add(Entry);
            }

            return Result;
        }

        private byte[] ReadSizedBytes()
        {
            uint Length = GlobalReader.ReadUInt32();
            if (Length > GlobalReader.BaseStream.Length - GlobalReader.BaseStream.Position)
                throw new InvalidDataException("FileData field is truncated.");
            return GlobalReader.ReadBytes(checked((int)Length));
        }
    }
}
