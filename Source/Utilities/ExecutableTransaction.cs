using System;
using System.IO;
using System.Security.Cryptography;

namespace S6Patcher.Source.Utilities
{
    internal sealed class ExecutableTransaction : IDisposable
    {
        public string TargetPath { get; }
        public string WorkingPath { get; }
        private FileStream Original;
        private readonly byte[] OriginalHash;
        private bool Committed;

        public ExecutableTransaction(string Path)
        {
            TargetPath = System.IO.Path.GetFullPath(Path);
            WorkingPath = AtomicFile.TemporaryPath(TargetPath);
            try
            {
                // Keep the real executable locked until commit or abort, including during downloads.
                Original = new FileStream(TargetPath, FileMode.Open, FileAccess.Read, FileShare.None);
                OriginalHash = SHA256.HashData(Original);
                Original.Position = 0;
                using FileStream Copy = new(WorkingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Original.CopyTo(Copy);
                Copy.Flush(true);
            }
            catch { Dispose(); throw; }
        }

        // Copy using the handle already held exclusively by this transaction.
        // Reopening the target via File.Copy fails under Windows FileShare.None.
        internal void CopyOriginalTo(string destination)
        {
            if (Original == null) throw new InvalidOperationException("Executable transaction is already closed.");
            long position = Original.Position;
            try
            {
                Original.Position = 0;
                using FileStream copy = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Original.CopyTo(copy);
                copy.Flush(true);
            }
            finally { Original.Position = position; }
        }

        public void Commit()
        {
            if (Committed || Original == null) throw new InvalidOperationException("Executable transaction is already closed.");
            string BackupPath = Backup.GetBackupPath(TargetPath, false);
            if (!File.Exists(BackupPath))
            {
                string TemporaryBackup = AtomicFile.TemporaryPath(BackupPath);
                try
                {
                    Original.Position = 0;
                    using (FileStream Copy = new(TemporaryBackup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        Original.CopyTo(Copy);
                        Copy.Flush(true);
                    }
                    // Never replace an earlier backup.
                    File.Move(TemporaryBackup, BackupPath, false);
                }
                finally { File.Delete(TemporaryBackup); }
            }

            Original.Dispose();
            Original = null;
            // Detect changes in the small interval between releasing the Windows lock and replacement.
            using (FileStream Current = new(TargetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!SHA256.HashData(Current).AsSpan().SequenceEqual(OriginalHash))
                    throw new IOException("The executable changed during patching. No patch was committed.");
            }
            File.Replace(WorkingPath, TargetPath, null);
            Committed = true;
            Logger.Instance.Log("Executable committed atomically: " + TargetPath);
        }

        public void Dispose()
        {
            Original?.Dispose();
            Original = null;
            if (!Committed && File.Exists(WorkingPath)) File.Delete(WorkingPath);
        }
    }
}
