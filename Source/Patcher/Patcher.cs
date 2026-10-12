using S6Patcher.Source.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace S6Patcher.Source.Patcher
{
    internal class Patcher
    {
        private readonly BinaryParser GlobalBinaryFileParser;
        private readonly FileStream GlobalStream;
        private readonly ExecutableTransaction Transaction;
        private bool Disposed;
        private readonly Mappings GlobalMappings;
        public readonly Mod GlobalMod;

        internal string ExecutablePath => Transaction.TargetPath;
        internal void SnapshotOriginalExecutable(string destination) => Transaction.CopyOriginalTo(destination);

        public readonly execID GlobalID;
        private readonly Lock WriteLock = new();

        public Patcher(string FilePath)
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                throw new ArgumentException("No file chosen or file does not exist!\nAborting ...");
            }

            try
            {
                Transaction = new ExecutableTransaction(FilePath);
                GlobalStream = IOFileHandler.Instance.OpenFileStream(Transaction.WorkingPath, true, FileMode.Open);
            }
            catch (Exception)
            {
                Transaction?.Dispose();
                throw;
            }

            Validator Validator = new(GlobalStream);
            try
            {
                GlobalID = Validator.Validate();
            }
            catch (Exception)
            {
                IOFileHandler.Instance.CloseStream(GlobalStream);
                Transaction.Dispose();
                throw;
            }

            try
            {
                GlobalBinaryFileParser = new BinaryParser("S6Patcher.Definitions.Definitions.bin");
            }
            catch (Exception)
            {
                IOFileHandler.Instance.CloseStream(GlobalStream);
                Transaction.Dispose();
                throw;
            }

            try
            {
                GlobalMappings = new Mappings(GlobalID, GlobalBinaryFileParser);
                GlobalMod = new Mod(GlobalID, Transaction.TargetPath);
            }
            catch { Dispose(); throw; }

            Logger.Instance.Log("ID: " + GlobalID.ToString() + ", Stream: " + GlobalStream.Name);
        }

        public void PatchByControlFeatures(List<string> Features) => WriteMappingToFile(GlobalMappings.GetMapping(Features));

        public void SetTextureResolution(string ResolutionText)
        {
            Logger.Instance.Log("Called with " + ResolutionText);

            if (!uint.TryParse(ResolutionText, out uint Resolution) || Resolution < 4)
            {
                HandleError("Invalid texture resolution value: " + ResolutionText);
                return;
            }

            WriteMappingToFile(GlobalMappings.GetTextureResolutionMapping(Resolution));
            IOFileHandler.Instance.UpdateEntryInOptionsFile("[Display]", "TextureResolution", "3");
            IOFileHandler.Instance.UpdateEntryInOptionsFile("[Display]", "Terrain", "2");
        }

        public void SetAutosaveTimer(string AutosaveText)
        {
            Logger.Instance.Log("Called with " + AutosaveText);

            if (!double.TryParse(AutosaveText, out double Timer) || !double.IsFinite(Timer) || Timer < 0 || !double.IsFinite(Timer * 60000))
            {
                HandleError("Invalid autosave timer value: " + AutosaveText);
                return;
            }

            WriteMappingToFile(GlobalMappings.GetAutoSaveMapping(Timer));
        }

        public void SetZoomLevel(string ZoomText)
        {
            Logger.Instance.Log("Called with " + ZoomText);

            if (!double.TryParse(ZoomText, out double Level) || !float.TryParse(ZoomText, out float Distance)
                || !double.IsFinite(Level) || !float.IsFinite(Distance) || Level <= 0 || !float.IsFinite(Distance + 4800f))
            {
                HandleError("Invalid zoom level value: " + ZoomText);
                return;
            }

            WriteMappingToFile(GlobalMappings.GetZoomLevelMapping(Level, Distance));
        }

        public void SetDocumentsFolderPath(string FolderPath)
        {
            Logger.Instance.Log("Called with " + FolderPath);

            uint MaxSize = 0xB4;
            if (string.IsNullOrWhiteSpace(FolderPath) || FolderPath.Contains('\0'))
                throw new ArgumentException("Invalid documents folder path.");
            int Size = Encoding.Unicode.GetByteCount(FolderPath + "\0");

            if (Size > MaxSize)
            {
                HandleError($"The documents folder path is too long! Maximum length is {MaxSize / 2 - 1} UTF-16 code units.");
                return;
            }
            else if (Size < 3)
            {
                HandleError("The documents folder path is too short!");
                return;
            }

            WriteMappingToFile(GlobalMappings.GetDocumentsFolderMapping(FolderPath));
        }

        public async Task SetModLoader(bool UseBugfixMod, bool UseDownload, bool UseGameplayModification)
        {
            Logger.Instance.Log("Called with " + UseBugfixMod.ToString() + " and " + UseDownload.ToString());
            WriteMappingToFile(GlobalMappings.GetModloaderMapping());
            SetEntryInOptionsFile("SpecialKnightsAvailable", UseBugfixMod);
            await GlobalMod.Create(UseBugfixMod, UseDownload, UseGameplayModification);
        }

        public void SetLargeAddressAwareFlag() => UpdatePEHeaderValues(0x20, 0x12, true);
        private void UpdatePEHeaderValues(short Mask, Int32 Offset, bool Enable)
        {
            Logger.Instance.Log("Called.");

            lock (WriteLock)
            {
                BinaryReader Reader = Utility.GetPEHeaderReader(GlobalStream);
                if (Reader == null)
                {
                    return;
                }

                Reader.BaseStream.Position += Offset;
                long Saved = Reader.BaseStream.Position;

                short Flag = Reader.ReadInt16();
                short Updated = Enable ? (short)(Flag | Mask) : (short)(Flag & ~Mask);
                if (Updated != Flag)
                {
                    WriteBytes(Saved, BitConverter.GetBytes(Updated));
                }

                Logger.Instance.Log($"Finished! Characteristics = Previous: 0x{Flag:X4} Updated: 0x{Updated:X4}");
            }
        }

        public void SetEntryInOptionsFile(string Entry, bool Checked) => 
            IOFileHandler.Instance.UpdateEntryInOptionsFile("[S6Patcher]", Entry, (Checked ? 1U : 0U).ToString());
        public void SetEasyDebug() => WriteMappingToFile(GlobalMappings.GetEasyDebugMapping());

        public async Task WriteScriptFilesToFolder(bool UseDownload)
        {
            WriteMappingToFile(GlobalMappings.GetScriptFileMapping());
            foreach (var Element in Utility.ScriptFeatures)
            {
                IOFileHandler.Instance.UpdateEntryInOptionsFile("[S6Patcher]", Element.Key, Element.Value ? "1" : "0", OnlyIfMissing: true);
            }

            IOFileHandler.Instance.UpdateEntryInOptionsFile("[S6Patcher]", "Version", " - S6Patcher v" + Utility.GetApplicationVersion()[..3]);
            await UserScriptHandler.Instance.CreateUserScriptFiles(UseDownload);
        }

        private void WriteMappingToFile(Dictionary<UInt32, byte[]> Mapping)
        {
            foreach (var Entry in Mapping)
                ValidateWrite(Entry.Key, Entry.Value);
            foreach (var Entry in Mapping)
            {
                WriteBytes(Entry.Key, Entry.Value);
                Logger.Instance.Log("Patching Element: 0x" + $"{Entry.Key:X}");
            }
        }

        private void WriteBytes(long Position, byte[] Bytes)
        {
            lock (WriteLock)
            {
                ValidateWrite(Position, Bytes);
                GlobalStream.Position = Position;
                try
                {
                    GlobalStream.Write(Bytes, 0, Bytes.Length);
                }
                catch (Exception ex)
                {
                    HandleError("Error while writing to file: " + ex.Message);
                }
            }
        }

        private void ValidateWrite(long Position, byte[] Bytes)
        {
            if (Bytes == null || Bytes.Length == 0 || Position < 0 || Position > GlobalStream.Length - Bytes.Length)
                throw new InvalidDataException($"Patch at 0x{Position:X} is outside the executable. No changes will be committed.");
        }

        private void HandleError(string Message)
        {
            ErrorTracking.Increment();
            Logger.Instance.Log(Message);
            throw new InvalidDataException(Message);
        }

        public void Complete(bool FinishWithPEHeader)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(Patcher));
            if (ErrorTracking.Count != 0) throw new IOException("Patching reported errors. The executable was not modified.");
            string BackupPath = Backup.GetBackupPath(Transaction.TargetPath, false);
            if (File.Exists(BackupPath))
            {
                using FileStream Saved = new(BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (new Validator(Saved).Validate() != GlobalID)
                    throw new InvalidDataException("Existing backup belongs to another edition. No patch was committed.");
            }
            string Name = GlobalStream.Name;
            long Size = GlobalStream.Length;
            GlobalStream.Flush(true);
            lock (WriteLock)
            {
                IOFileHandler.Instance.CloseStream(GlobalStream);
            }

            if (FinishWithPEHeader)
            {
                new CheckSumCalculator().WritePEHeaderFileCheckSum(Name, Size);
            }
            IOFileHandler.Instance.WriteBackToOptionsFiles();
            if (ErrorTracking.Count != 0) throw new IOException("Could not finish patching. The executable was not modified; check the log for auxiliary file errors.");
            Transaction.Commit();
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try { IOFileHandler.Instance.CloseStream(GlobalStream); }
            finally
            {
                GlobalBinaryFileParser?.Dispose();
                Transaction?.Dispose();
                IOFileHandler.Instance.DiscardOptionChanges();
            }
        }
    }
}
