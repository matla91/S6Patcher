using S6Patcher.Source.Utilities;
using System;
using System.IO;
using System.IO.Hashing;
using System.Threading.Tasks;
using S6Packer.Source;

namespace S6Patcher.Source.Patcher
{
    internal static class Archives
    {
        public static async Task<bool> PackFolderFilesIntoArchiveFileAsync(string BaseFolderPath, string ArchiveFilePath)
        {
            string Temporary = AtomicFile.TemporaryPath(ArchiveFilePath);
            try
            {
                if (!Directory.Exists(BaseFolderPath)) throw new DirectoryNotFoundException("Archive source folder does not exist.");
                await using (FileStream Archive = new(Temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    new BBAArchiveFile(Archive, BaseFolderPath, Path.GetExtension(ArchiveFilePath));
                    Archive.Flush(true);
                    ArchiveGuard.ReadEntries(Archive, BaseFolderPath);
                }
                File.Move(Temporary, ArchiveFilePath, true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex.ToString());
                return false;
            }
            finally { File.Delete(Temporary); }
        }

        public static async Task<bool> ExtractArchiveFileToFolderAsync(string FilePath, string OutputDirectoryPath, bool IsMap)
        {
            FileStream ArchiveFileStream = OpenArchiveFileStream(FilePath);
            if (ArchiveFileStream == null) return false;
            try
            {
                // Validate every name before extracting the first file. Do not use the unchecked upstream extractor.
                var Entries = ArchiveGuard.ReadEntries(ArchiveFileStream, OutputDirectoryPath);
                BBAArchiveFile Archive = new(ArchiveFileStream, !IsMap);
                foreach (var Entry in Entries)
                {
                    ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();
                    if (Definition.FileType == 256 && (Entry.FileName == "" || Entry.FileName == ".")) continue;
                    string Destination = SafePath.InDirectory(OutputDirectoryPath, Entry.FileName);
                    if (Definition.FileType == 256) { Directory.CreateDirectory(Destination); continue; }
                    byte[] Data = Archive.GetFileDataByDataEntryName(Entry.FileName);
                    if (Data == null || Data.Length != Definition.DecompressedFileSize || Crc32.HashToUInt32(Data) != Definition.DecompressedCRC32)
                        throw new InvalidDataException("Archive file data/CRC mismatch: " + Entry.FileName);
                    // Do not retain trailing bytes when an existing file was larger.
                    Directory.CreateDirectory(Path.GetDirectoryName(Destination));
                    await AtomicFile.WriteAllBytesAsync(Destination, Data);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex.ToString());
                return false;
            }
            finally { IOFileHandler.Instance.CloseStream(ArchiveFileStream); }
        }

        public static FileStream OpenArchiveFileStream(string ArchiveFilePath) => IOFileHandler.Instance.OpenFileStream(ArchiveFilePath, false, FileMode.Open, FileAccess.Read);
    }
}
