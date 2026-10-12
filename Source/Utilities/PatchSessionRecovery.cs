using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace S6Patcher.Source.Utilities
{
    // A crash-recoverable rollback journal for HE Steam auxiliary files.
    // The journal lives beside the executable so it is discoverable on next startup.
    internal sealed class PatchSessionRecovery : IDisposable
    {
        private sealed class Snapshot
        {
            public string Path { get; set; }
            public bool Existed { get; set; }
            public bool Directory { get; set; }
            public string Backup { get; set; }
        }
        private sealed class Manifest
        {
            public string State { get; set; } = "pending";
            public List<Snapshot> Items { get; set; } = [];
        }

        private const string JournalSuffix = ".s6patcher-session";
        private readonly string Journal;
        private bool Finished;
        private PatchSessionRecovery(string journal) { Journal = journal; }

        internal static string JournalPath(string executable) => Path.GetFullPath(executable) + JournalSuffix;

        internal static PatchSessionRecovery Begin(string executable, IEnumerable<string> files, IEnumerable<string> directories)
        {
            string journal = JournalPath(executable);
            if (Directory.Exists(journal)) throw new IOException("A previous patch session needs recovery before starting.");
            Directory.CreateDirectory(journal);
            var session = new PatchSessionRecovery(journal);
            try
            {
                var manifest = new Manifest();
                int index = 0;
                foreach (string path in files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string backup = index++.ToString("D4") + ".file";
                    bool exists = File.Exists(path);
                    if (exists) File.Copy(path, Path.Combine(journal, backup));
                    manifest.Items.Add(new Snapshot { Path = path, Existed = exists, Directory = false, Backup = backup });
                }
                foreach (string path in directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string backup = index++.ToString("D4") + ".dir";
                    bool exists = Directory.Exists(path);
                    if (exists) CopyDirectory(path, Path.Combine(journal, backup));
                    manifest.Items.Add(new Snapshot { Path = path, Existed = exists, Directory = true, Backup = backup });
                }
                WriteManifest(journal, manifest);
                return session;
            }
            catch
            {
                // No tracked target has been mutated at this point.
                Directory.Delete(journal, true);
                throw;
            }
        }

        private static void WriteManifest(string journal, Manifest manifest)
        {
            AtomicFile.WriteAllBytes(Path.Combine(journal, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
        }

        private static Manifest ReadManifest(string journal)
        {
            string file = Path.Combine(journal, "manifest.json");
            if (!File.Exists(file)) throw new InvalidDataException("Incomplete patch recovery journal. Manual inspection required.");
            return JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(file))
                ?? throw new InvalidDataException("Invalid patch recovery journal.");
        }

        internal static bool Recover(string executable)
        {
            string journal = JournalPath(executable);
            if (!Directory.Exists(journal)) return false;
            Manifest manifest = ReadManifest(journal);
            if (manifest.State == "committed")
            {
                Directory.Delete(journal, true);
                return false;
            }
            if (manifest.State != "pending") throw new InvalidDataException("Unknown patch recovery state.");
            foreach (Snapshot item in manifest.Items.AsEnumerable().Reverse())
            {
                string backup = Path.Combine(journal, item.Backup);
                if (item.Directory)
                {
                    if (item.Existed && !Directory.Exists(backup))
                        throw new IOException("Missing mod recovery snapshot; no files were overwritten.");
                    if (Directory.Exists(item.Path)) Directory.Delete(item.Path, true);
                    if (item.Existed) CopyDirectory(backup, item.Path);
                }
                else
                {
                    if (item.Existed && !File.Exists(backup))
                        throw new IOException("Missing file recovery snapshot; no files were overwritten.");
                    if (item.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
                        AtomicFile.WriteAllBytes(item.Path, File.ReadAllBytes(backup));
                    }
                    else if (File.Exists(item.Path)) File.Delete(item.Path);
                }
            }
            Directory.Delete(journal, true);
            return true;
        }

        internal void Commit()
        {
            WriteManifest(Journal, new Manifest { State = "committed" });
            Finished = true;
            try { Directory.Delete(Journal, true); }
            catch (IOException) { /* committed marker permits cleanup on next launch */ }
        }

        public void Dispose()
        {
            if (!Finished) RecoverFromJournal();
        }

        internal void RecoverFromJournal()
        {
            Recover(Journal[..^JournalSuffix.Length]);
            Finished = true;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
    }
}
