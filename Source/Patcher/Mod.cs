using S6Patcher.Properties;
using S6Patcher.Source.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Hashing;
using Utility = S6Patcher.Source.Utilities.Utility;
using S6Packer.Source;

namespace S6Patcher.Source.Patcher
{
    internal class Mod(execID GlobalID, string GlobalStreamName)
    {
        private class ModLoaderFile
        {
            public bool IsDataFile;
            public bool IsExtra1File;
            public string Name;
            public byte[] Data;
            public FileDataEntry Entry;
        }

        private readonly List<FileDataEntry> GlobalFileDataMappings = [];
        private readonly string GlobalDestinationDirectoryPath = IOFileHandler.Instance.GetModLoaderDirectory(GlobalID, GlobalStreamName);
        private readonly string GlobalBaseGameDataDirectoryPath = IOFileHandler.Instance.GetGameDataDirectory(GlobalID, GlobalStreamName, false);
        private readonly string GlobalExtra1GameDataDirectoryPath = IOFileHandler.Instance.GetGameDataDirectory(GlobalID, GlobalStreamName, true);
        private readonly string ArchiveFilePath = Path.Combine(IOFileHandler.Instance.GetModLoaderDirectory(GlobalID, GlobalStreamName), "shr");
        private readonly string ArchiveFilePathModloader = Path.Combine(IOFileHandler.Instance.GetModLoaderDirectory(GlobalID, GlobalStreamName), "shr");
        private readonly string ArchiveFilePathBase = Path.Combine(IOFileHandler.Instance.GetModLoaderDirectory(GlobalID, GlobalStreamName), "base");
        private readonly string ArchiveFilePathExtra1 = Path.Combine(IOFileHandler.Instance.GetModLoaderDirectory(GlobalID, GlobalStreamName), "extra1");
        private const string ArchiveFileName = "mod.bba";
        private string WorkingBugfixDirectory;
        public event Action<string> ShowMessage;
        private static async Task<List<MemoryStream>> DownloadDefinitionFile(string FilePath) => await WebHandler.Instance.DownloadFilesAsync([new Uri(FilePath)]);

        public async Task Create(bool InstallMod, bool DownloadDefinition, bool UseGameplayModification)
        {
            // Restore the previous mod if the process died after moving it aside.
            // If both directories exist, stop rather than silently delete evidence.
            RecoverInterruptedInstall(ArchiveFilePath);
            try
            {
                await WriteModLoaderFiles();
            }
            catch (Exception ex)
            {
                ErrorTracking.Increment();
                Logger.Instance.Log(ex.ToString());
                ShowMessage?.Invoke(ex.Message);
                return;
            }

            if (!InstallMod)
            {
                return;
            }

            BinaryParser FileDataParser = null;
            if (DownloadDefinition)
            {
                List<MemoryStream> Definition = await DownloadDefinitionFile(Resources.RepoDefinitionBasePath + "FileData.bin");
                if (Definition.Count == 1)
                {
                    try
                    {
                        FileDataParser = new BinaryParser(Definition[0]);
                    }
                    catch
                    {
                        Definition.ForEach(Element => Element.Dispose());
                        FileDataParser = null;

                        Logger.Instance.Log("Downloaded Definition file invalid, falling back to embedded resource ...");
                    }
                }
            }

            FileDataParser ??= new BinaryParser("S6Patcher.Definitions.FileData.bin");
            try
            {
                GlobalFileDataMappings.AddRange(FileDataParser.ParseFileData());
                if (UseGameplayModification) GlobalFileDataMappings.AddRange(GameplayModification.ModifiableFileData);
            }
            finally { FileDataParser.Dispose(); }
            
            foreach (var Entry in GlobalFileDataMappings)
            {
                SafePath.InDirectory(GlobalDestinationDirectoryPath, Entry.FilePath);
                SafePath.InDirectory(GlobalBaseGameDataDirectoryPath, Entry.BBArchiveName);
            }
            if (GlobalFileDataMappings.GroupBy(Entry => Utility.SanitizeFilePath(Entry.FilePath), StringComparer.OrdinalIgnoreCase).Any(Group => Group.Count() > 1))
                throw new InvalidDataException("Conflicting mod definitions target the same file. No bugfix files were installed.");

            // Never delete the active bugfix folder before its replacement has been fully generated.
            WorkingBugfixDirectory = AtomicFile.TemporaryPath(ArchiveFilePath) + ".dir";
            uint PreviousErrors = ErrorTracking.Count;
            try
            {
                Directory.CreateDirectory(WorkingBugfixDirectory);
                if (GlobalID == execID.OV) await GetModLoaderFilesFromArchives();
                else await UpdateModLoaderFilesByFileDataMapping();
                if (ErrorTracking.Count != PreviousErrors)
                    throw new IOException("Mod generation failed. Previous bugfix files were retained; see S6Patcher.log.");

                InstallStagedDirectory(WorkingBugfixDirectory, ArchiveFilePath);

            }
            finally
            {
                if (Directory.Exists(WorkingBugfixDirectory)) Directory.Delete(WorkingBugfixDirectory, true);
                WorkingBugfixDirectory = null;
            }
        }

        // A deterministic recovery name survives a crash between the two directory renames.
        // Refuse ambiguous state rather than overwrite a previous installation.
        internal static void RecoverInterruptedInstall(string active)
        {
            string previous = active + ".s6patcher-recovery";
            if (!Directory.Exists(previous)) return;
            if (Directory.Exists(active))
                throw new IOException("A previous mod installation recovery directory remains. Back it up and inspect it before patching again.");
            Directory.Move(previous, active);
        }

        internal static void InstallStagedDirectory(string staged, string active, Action afterBackup = null)
        {
            string previous = active + ".s6patcher-recovery";
            if (Directory.Exists(previous))
                throw new IOException("A pending mod recovery directory exists. Refusing to overwrite it.");
            bool hadPrevious = Directory.Exists(active);
            if (hadPrevious) Directory.Move(active, previous);
            try
            {
                afterBackup?.Invoke(); // fault-injection seam, never used in production
                Directory.Move(staged, active);
            }
            catch
            {
                if (hadPrevious && !Directory.Exists(active))
                    Directory.Move(previous, active);
                throw;
            }
            if (hadPrevious)
            {
                try { Directory.Delete(previous, true); }
                catch (Exception ex) { Logger.Instance.Log("Old mod directory retained for inspection: " + ex.GetType().Name); }
            }
        }

        private async Task WriteModLoaderFiles()
        {
            Directory.CreateDirectory(GlobalDestinationDirectoryPath);

            if (GlobalID == execID.HE_UBISOFT || GlobalID == execID.HE_STEAM)
            {
                // The active shr directory is installed only after successful staging.
                // Creating it here would leave an empty mod after a failed first install.
                Directory.CreateDirectory(Path.Combine(ArchiveFilePathBase, "shr"));
                Directory.CreateDirectory(Path.Combine(ArchiveFilePathExtra1, "shr"));
                Logger.Instance.Log("Directories created.");
            }
            else
            {
                Directory.CreateDirectory(ArchiveFilePathBase);
                Directory.CreateDirectory(ArchiveFilePathExtra1);
                Directory.CreateDirectory(ArchiveFilePathModloader);

                await WriteModLoaderArchiveFileAsync(ArchiveFilePathBase);
                await WriteModLoaderArchiveFileAsync(ArchiveFilePathExtra1);
                await WriteModLoaderArchiveFileAsync(ArchiveFilePathModloader);
            }
        }

        private static async Task WriteModLoaderArchiveFileAsync(string BasePath)
        {
            string CurrentPath = Path.Combine(BasePath, ArchiveFileName);
            if (!File.Exists(CurrentPath))
            {
                await AtomicFile.WriteAllBytesAsync(CurrentPath, Resources.mod);
                Logger.Instance.Log("Written " + ArchiveFileName + " to " + CurrentPath);
            }
            else
            {
                Logger.Instance.Log("Archive file already existing at " + CurrentPath);
            }
        }

        private async Task GetModLoaderFilesFromArchives()
        {
            var ArchiveFilesToParse = GetArchiveFilesToParse();
            await LoadDataFromArchiveFiles(ArchiveFilesToParse);
            await WriteAllModFilesToCorrespondingFolder(ArchiveFilesToParse);
            await CreateArchiveFileInPath(WorkingBugfixDirectory);
        }

        private static byte[] UpdateFileContent(FileDataEntry Entry, byte[] FileContent)
        {
            return FileDataPatches.Apply(Entry, FileContent);
        }

        private FileStream OpenArchiveFileStream(string ArchiveFile, out bool IsExtra1ArchiveFile)
        {
            IsExtra1ArchiveFile = false;

            string ArchiveFilePath = SafePath.InDirectory(GlobalBaseGameDataDirectoryPath, ArchiveFile);
            if (!File.Exists(ArchiveFilePath))
            {
                // Check if is extra1
                ArchiveFilePath = SafePath.InDirectory(GlobalExtra1GameDataDirectoryPath, ArchiveFile);

                if (!File.Exists(ArchiveFilePath))
                {
                    Logger.Instance.Log($"Could NOT find archive file {ArchiveFile}. Skipping ...");
                    return null;
                }

                IsExtra1ArchiveFile = true;
            }

            return Archives.OpenArchiveFileStream(ArchiveFilePath);
        }

        private async Task LoadDataFromArchiveFiles(Dictionary<string, List<ModLoaderFile>> ArchiveFilesToParse)
        {
            foreach (var Element in ArchiveFilesToParse)
            {
                FileStream ArchiveFileStream = OpenArchiveFileStream(Element.Key, out bool IsExtra1ArchiveFile);
                if (ArchiveFileStream == null)
                {
                    ErrorTracking.Increment();
                    continue;
                }

                foreach (ModLoaderFile MLF in Element.Value)
                {
                    MLF.IsExtra1File = IsExtra1ArchiveFile;
                }

                BBAArchiveFile ArchiveFile;
                try
                {
                    ArchiveGuard.ReadEntries(ArchiveFileStream, WorkingBugfixDirectory);
                    ArchiveFile = new(ArchiveFileStream, true);
                }
                catch
                {
                    IOFileHandler.Instance.CloseStream(ArchiveFileStream);
                    ErrorTracking.Increment();
                    Logger.Instance.Log($"Could NOT parse archive file {Element.Key}. Skipping ...");
                    continue;
                }
                
                try
                {
                    foreach (ModLoaderFile MLF in Element.Value)
                    {
                        MLF.Data = ArchiveFile.GetFileDataByDataEntryName(MLF.Name);
                        if (MLF.Data == null)
                        {
                            ErrorTracking.Increment();
                            Logger.Instance.Log($"Could NOT parse data from archive file {Element.Key}.");
                        }
                    }
                }
                finally { IOFileHandler.Instance.CloseStream(ArchiveFileStream); }
            }
        }

        private Dictionary<string, List<ModLoaderFile>> GetArchiveFilesToParse()
        {
            Dictionary<string, List<ModLoaderFile>> ArchiveFilesToParse = [];
            foreach (var Element in GlobalFileDataMappings)
            {
                ModLoaderFile CurrentFile = new()
                {
                    Name = Element.FilePath,
                    Entry = Element,
                    Data = null,
                    IsExtra1File = false,
                    IsDataFile = Element.IsDataFile
                };

                if (ArchiveFilesToParse.TryGetValue(Element.BBArchiveName, out List<ModLoaderFile> Value))
                {
                    Value.Add(CurrentFile);
                }
                else
                {
                    List<ModLoaderFile> Files = [CurrentFile];
                    ArchiveFilesToParse.Add(Element.BBArchiveName, Files);
                }
            }

            return ArchiveFilesToParse;
        }

        private async Task WriteAllModFilesToCorrespondingFolder(Dictionary<string, List<ModLoaderFile>> ArchiveFilesToParse)
        {
            try
            {
                Directory.CreateDirectory(WorkingBugfixDirectory);
            }
            catch (Exception ex)
            {
                ErrorTracking.Increment();
                Logger.Instance.Log(ex.ToString());
                return;
            }

            foreach (var Element in ArchiveFilesToParse)
            {
                foreach (var CurrentFile in Element.Value)
                {
                    if (CurrentFile.Data == null)
                    {
                        Logger.Instance.Log($"Skipping file {CurrentFile.Name} cause Data field is null.");
                        continue;
                    }

                    string SanitizedFilePath = Utility.SanitizeFilePath(CurrentFile.Entry.FilePath);
                    bool Result = await WriteModifiedFileToFolder(CurrentFile.Data, CurrentFile.Entry, CurrentFile.Name, SanitizedFilePath, 
                        true, System.Threading.CancellationToken.None);

                    if (!Result)
                    {
                        Logger.Instance.Log($"Could NOT write modified file {CurrentFile.Name}. Skipping ...");
                    }
                    else
                    {
                        Logger.Instance.Log($"Updated file: {CurrentFile.Name}");
                    }
                }
            }
        }

        private async Task CreateArchiveFileInPath(string RootDirectoryFilePath)
        {
            string TemporaryFile = AtomicFile.TemporaryPath(Path.Combine(GlobalDestinationDirectoryPath, ArchiveFileName));
            try
            {
                await using (FileStream WrittenArchive = new(TemporaryFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    new BBAArchiveFile(WrittenArchive, RootDirectoryFilePath, ".bba");
                    WrittenArchive.Flush(true);
                    ArchiveGuard.ReadEntries(WrittenArchive, RootDirectoryFilePath);
                }
                // RootDirectoryFilePath is the isolated staging folder, never the active mod directory.
                Directory.Delete(RootDirectoryFilePath, true);
                Directory.CreateDirectory(RootDirectoryFilePath);
                File.Move(TemporaryFile, Path.Combine(RootDirectoryFilePath, ArchiveFileName));
                Logger.Instance.Log("Generated staged bugfix archive: " + RootDirectoryFilePath);
            }
            finally { File.Delete(TemporaryFile); }
        }

        private async Task UpdateModLoaderFilesByFileDataMapping()
        {
            await Parallel.ForEachAsync(GlobalFileDataMappings, async (Entry, CT) =>
            {
                string SanitizedFilePath = Utility.SanitizeFilePath(Entry.FilePath);
                string CurrentFile = Utility.ResolveCaseInsensitivePath(SafePath.InDirectory(GlobalExtra1GameDataDirectoryPath, SanitizedFilePath));

                if (!File.Exists(CurrentFile))
                {
                    CurrentFile = Utility.ResolveCaseInsensitivePath(SafePath.InDirectory(GlobalBaseGameDataDirectoryPath, SanitizedFilePath));
                    if (!File.Exists(CurrentFile))
                    {
                        ErrorTracking.Increment();
                        Logger.Instance.Log($"Could NOT find file {CurrentFile}. Skipping ...");
                        return;
                    }
                }

                byte[] FileContent;
                try
                {
                    FileContent = await File.ReadAllBytesAsync(CurrentFile, CT);
                }
                catch (Exception ex)
                {
                    ErrorTracking.Increment();
                    Logger.Instance.Log(ex.ToString());
                    return;
                }

                bool Result = await WriteModifiedFileToFolder(FileContent, Entry, Path.GetFileName(CurrentFile), SanitizedFilePath, false, CT);
                if (!Result)
                {
                    Logger.Instance.Log($"Could NOT write modified file {Path.GetFileName(CurrentFile)}. Skipping ...");
                }
                else
                {
                    Logger.Instance.Log($"Updated file: {Path.GetFileName(CurrentFile)}");
                }
            });
        }

        private async Task<bool> WriteModifiedFileToFolder(byte[] FileContent, FileDataEntry Entry, string CurrentFile, string SanitizedFilePath, 
            bool UseModLoaderPath,System.Threading.CancellationToken CT)
        {
            if (Entry.IsDataFile)
            {
                Crc32 CRC = new();
                CRC.Append(FileContent);
                
                if (CRC.GetCurrentHashAsUInt32() != Entry.OriginalFileCRC)
                {
                    ErrorTracking.Increment();
                    Logger.Instance.Log($"File Hash for file {CurrentFile} not equivalent! Skipping ...");
                    return false;
                }

                FileContent = UpdateFileContent(Entry, FileContent);
            }
            else
            {
                FileContent = GameplayModification.UpdateFileContent(Entry, FileContent);
            }

            if (FileContent == null)
            {
                ErrorTracking.Increment();
                Logger.Instance.Log($"File {CurrentFile} could not be updated! Skipping ...");
                return false;
            }

            string DestinationPath = WorkingBugfixDirectory ?? (UseModLoaderPath ? ArchiveFilePathModloader : ArchiveFilePath);
            string Destination = SafePath.InDirectory(DestinationPath, SanitizedFilePath);
            string DirectoryPath = Path.GetDirectoryName(Destination);

            try
            {
                Directory.CreateDirectory(DirectoryPath);
                await AtomicFile.WriteAllBytesAsync(Destination, FileContent, CT);
            }
            catch (Exception ex)
            { 
                ErrorTracking.Increment();
                Logger.Instance.Log(ex.ToString());
                return false;
            }

            return true;
        }
    }
}
