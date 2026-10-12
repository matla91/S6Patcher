using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using S6Packer.Source;
using S6Patcher.Source.Utilities;

namespace S6Patcher.Source.Patcher
{
    // Preflight the embedded S6Packer format before its unsafe decoder or any extraction writes.
    internal static class ArchiveGuard
    {
        public static List<BBADataEntry> ReadEntries(Stream Stream, string OutputDirectory)
        {
            using BinaryReader Reader = new(Stream, Encoding.UTF8, true);
            Stream.Position = 0;
            byte[] Prefix = Read(Reader, 4);
            long Start = 0;
            if (Prefix.AsSpan().SequenceEqual("RGMH"u8))
            {
                Stream.Position = 8;
                uint Offset = Reader.ReadUInt32();
                if (Offset >= Stream.Length) throw new InvalidDataException("Invalid map archive offset.");
                Stream.Position = Offset;
                Start = Reader.ReadByte() == 0 ? Offset + 1L : Offset;
            }
            Stream.Position = Start;
            byte[] HeaderBytes = Read(Reader, BBAHeader.Size);
            if (!HeaderBytes.AsSpan(0, 3).SequenceEqual("BAF"u8) || HeaderBytes[3] != 4)
                throw new InvalidDataException("Unsupported BBA header or version.");
            BBAHeaderDefinition Header = MemoryMarshal.Read<BBAHeaderDefinition>(HeaderBytes);
            if (Header.EncryptionID != Crypt.S6_HEAD_CRYPTID) throw new InvalidDataException("Unknown archive header encryption ID.");
            byte[] DirectoryBytes = Read(Reader, BBADirectoryHeader.Size);
            Crypt.Decrypt(DirectoryBytes, Header.EncryptionID);
            BBADirectoryHeaderDefinition Directory = MemoryMarshal.Read<BBADirectoryHeaderDefinition>(DirectoryBytes);
            if (Directory.Length < 8 || Directory.Length > 64 * 1024 * 1024 || Directory.Length % 4 != 0
                || Directory.Offset > Stream.Length - Directory.Length)
                throw new InvalidDataException("Invalid archive directory range.");
            if (Directory.EncryptionID != Crypt.S6_BBA_CRYPTID && Directory.EncryptionID != Crypt.S6_MAP_CRYPTID)
                throw new InvalidDataException("Unknown archive directory encryption ID.");
            Stream.Position = Directory.Offset;
            byte[] Encoded = Read(Reader, (int)Directory.Length);
            if (Crc32.HashToUInt32(Encoded) != Directory.CRC32) throw new InvalidDataException("Archive directory CRC mismatch.");
            Crypt.Decrypt(Encoded, Directory.EncryptionID);
            CompressedHeaderDefinition Compressed = MemoryMarshal.Read<CompressedHeaderDefinition>(Encoded);
            if (Compressed.DecompressedSize < BBADirectory.Size || Compressed.DecompressedSize > 64 * 1024 * 1024
                || Compressed.CompressedSize > Encoded.Length - BBACompression.Size)
                throw new InvalidDataException("Invalid compressed archive directory size.");
            byte[] Data = BBACompression.Decompress(Encoded.AsSpan(BBACompression.Size, (int)Compressed.CompressedSize).ToArray(), (int)Compressed.DecompressedSize);
            BBADirectoryDefinition Table = MemoryMarshal.Read<BBADirectoryDefinition>(Data);
            long Position = Table.OffsetFileEntries + 4L;
            if (Position < BBADirectory.Size || Position > Data.Length || Table.NumberOfFiles > (Data.Length - Position) / BBADataEntry.Size)
                throw new InvalidDataException("Invalid archive entry count.");
            long HashEnd = Table.OffsetFileHashtable;
            if (HashEnd < Position || HashEnd > Data.Length - 4) throw new InvalidDataException("Invalid archive hash table offset.");
            uint HashCount = BitConverter.ToUInt32(Data, (int)HashEnd);
            if (HashCount > (Data.Length - HashEnd - 4) / 8) throw new InvalidDataException("Truncated archive hash table.");
            List<BBADataEntry> Result = [];
            HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < Table.NumberOfFiles; i++)
            {
                if (Position > HashEnd - BBADataEntry.Size) throw new InvalidDataException("Truncated archive entry.");
                BBADataEntryDefinition Entry = MemoryMarshal.Read<BBADataEntryDefinition>(Data.AsSpan((int)Position));
                long Size = BBADataEntry.Size + Entry.FileNameLength + (4 - Entry.FileNameLength % 4);
                if (Size > HashEnd - Position) throw new InvalidDataException("Truncated archive filename.");
                BBADataEntry File = new(Data.AsSpan((int)Position, (int)Size));
                bool IsDirectory = Entry.FileType == 256;
                if (!IsDirectory && Entry.FileType != 0 && Entry.FileType != 1) throw new InvalidDataException("Unknown archive file type.");
                if (!(IsDirectory && (File.FileName == "" || File.FileName == ".")))
                    SafePath.InDirectory(OutputDirectory, File.FileName);
                if (!Names.Add(File.FileName)) throw new InvalidDataException("Duplicate archive path: " + File.FileName);
                if (!IsDirectory && (Entry.DecompressedFileSize > 512 * 1024 * 1024
                    || Entry.CompressedFileSize > Stream.Length || Entry.FileOffset > Stream.Length - Entry.CompressedFileSize))
                    throw new InvalidDataException("Invalid archive file data range.");
                Result.Add(File);
                Position += Size;
            }
            return Result;
        }

        private static byte[] Read(BinaryReader Reader, int Count)
        {
            if (Count < 0 || Count > Reader.BaseStream.Length - Reader.BaseStream.Position)
                throw new InvalidDataException("Truncated archive.");
            return Reader.ReadBytes(Count);
        }
    }
}
