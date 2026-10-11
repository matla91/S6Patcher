using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace S6Patcher.Source.Utilities
{
    // The temporary file is on the same volume as the destination. A failed write never truncates it.
    internal static class AtomicFile
    {
        public static string TemporaryPath(string Destination) => Destination + ".s6patcher-" + Guid.NewGuid().ToString("N") + ".tmp";

        public static void WriteAllBytes(string Destination, byte[] Data)
        {
            string Temporary = TemporaryPath(Destination);
            try
            {
                using (FileStream Stream = new(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Stream.Write(Data);
                    Stream.Flush(true);
                }
                File.Move(Temporary, Destination, true);
            }
            finally { File.Delete(Temporary); }
        }

        public static async Task WriteAllBytesAsync(string Destination, byte[] Data, CancellationToken Token = default)
        {
            string Temporary = TemporaryPath(Destination);
            try
            {
                await using (FileStream Stream = new(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await Stream.WriteAsync(Data, Token);
                    Stream.Flush(true);
                }
                Token.ThrowIfCancellationRequested();
                File.Move(Temporary, Destination, true);
            }
            finally { File.Delete(Temporary); }
        }

        public static void WriteAllText(string Destination, string Text) => WriteAllBytes(Destination, new UTF8Encoding(false).GetBytes(Text));
    }
}
