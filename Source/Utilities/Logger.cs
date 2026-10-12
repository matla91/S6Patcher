using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("S6Patcher.Tests")]

namespace S6Patcher.Source.Utilities
{
    /// <summary>Best-effort diagnostics: a logging failure must never abort patching.</summary>
    public sealed class Logger
    {
        private readonly Lock Gate = new();
        private readonly string PrimaryPath;
        private readonly string FallbackPath;
        private readonly Func<string, Stream> Open;
        private readonly Action<string> Report;
        private StreamWriter Writer;
        private bool UsingFallback;
        private bool Disposed;
        private int ConsecutiveFailures;

        public static Logger Instance { get; } = new();

        private Logger() : this(
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "S6Patcher"),
            path => new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            message => { try { Trace.WriteLine(message); Console.Error.WriteLine(message); } catch { } })
        { }

        // Injectable boundaries for deterministic fault tests, without changing callers.
        internal Logger(string applicationDirectory, string fallbackDirectory,
            Func<string, Stream> open, Action<string> report)
        {
            PrimaryPath = Path.Combine(applicationDirectory, "S6Patcher.log");
            FallbackPath = Path.Combine(fallbackDirectory, "S6Patcher.log");
            Open = open ?? throw new ArgumentNullException(nameof(open));
            Report = report ?? throw new ArgumentNullException(nameof(report));
        }

        /// <summary>Directory of the active log, or null if logging is unavailable.</summary>
        public string LogDirectory
        {
            get { lock (Gate) { return Writer == null ? null : Path.GetDirectoryName(UsingFallback ? FallbackPath : PrimaryPath); } }
        }

        public void Log(string Message, [CallerMemberName] string Caller = "")
        {
            lock (Gate)
            {
                if (Disposed) return;
                string line = DateTime.Now.ToString("HH:mm:ss") + " - " + Caller + "(): " + Message;
                if (Writer == null && !TryOpen()) return;
                try
                {
                    Writer.WriteLine(line);
                    Writer.Flush();
                    ConsecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    Fail("write", ex);
                    CloseWriter();
                    if (!UsingFallback)
                    {
                        UsingFallback = true;
                        if (TryOpen())
                        {
                            try { Writer.WriteLine(line); Writer.Flush(); ConsecutiveFailures = 0; }
                            catch (Exception retryError) { Fail("fallback write", retryError); CloseWriter(); }
                        }
                    }
                }
            }
        }

        private bool TryOpen()
        {
            if (TryOpenAt(UsingFallback ? FallbackPath : PrimaryPath)) return true;
            if (!UsingFallback)
            {
                UsingFallback = true;
                return TryOpenAt(FallbackPath);
            }
            return false;
        }

        private bool TryOpenAt(string path)
        {
            Stream stream = null;
            try
            {
                if (UsingFallback) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                stream = Open(path);
                Writer = new StreamWriter(stream, new UTF8Encoding(false));
                stream = null;
                return true;
            }
            catch (Exception ex)
            {
                Fail(UsingFallback ? "fallback open" : "application log open", ex);
                if (stream != null) { try { stream.Dispose(); } catch (Exception closeError) { Fail("failed-open stream close", closeError); } }
                Writer = null;
                return false;
            }
        }

        private void Fail(string operation, Exception error)
        {
            ConsecutiveFailures++;
            // Report the first error and periodically thereafter; do not recurse via Log().
            if (ConsecutiveFailures == 1 || ConsecutiveFailures % 32 == 0)
            {
                try { Report("S6Patcher logging unavailable (" + operation + ", " + error.GetType().Name +
                    "). Diagnostics are also sent to stderr/trace where available."); }
                catch { }
            }
        }

        private void CloseWriter()
        {
            StreamWriter writer = Writer;
            Writer = null;
            if (writer == null) return;
            try { writer.Dispose(); }
            catch (Exception ex) { Fail("close", ex); }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (Disposed) return;
                Log("Logger: Shutting down.");
                Disposed = true;
                CloseWriter();
            }
        }
    }
}
