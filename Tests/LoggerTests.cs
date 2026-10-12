using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using S6Patcher.Source.Utilities;

internal static class LoggerTests
{
    private sealed class FaultStream : MemoryStream
    {
        public bool FailWrite;
        public bool FailClose;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (FailWrite) throw new IOException("Injected write failure");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailWrite) throw new IOException("Injected write failure");
            base.Write(buffer);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && FailClose) throw new IOException("Injected close failure");
        }
    }

    internal static void Run(Action<string, Action> test, string root)
    {
        string NewDir() { string p = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }

        test("logger appends and does not truncate previous sessions", () =>
        {
            string app = NewDir(), fallback = NewDir();
            string file = Path.Combine(app, "S6Patcher.log");
            File.WriteAllText(file, "previous session\n");
            usingLogger(new Logger(app, fallback, p => new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read), _ => { }),
                l => { l.Log("new session"); });
            string text = File.ReadAllText(file);
            if (!text.StartsWith("previous session\n") || !text.Contains("new session")) throw new Exception("log replaced");
        });

        test("logger falls back when application file cannot open", () =>
        {
            string app = NewDir(), fallback = Path.Combine(NewDir(), "nested");
            var diagnostics = new ConcurrentQueue<string>();
            usingLogger(new Logger(app, fallback, p => p.StartsWith(app, StringComparison.Ordinal)
                ? throw new UnauthorizedAccessException("denied")
                : new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read),
                diagnostics.Enqueue),
                l => { l.Log("retained event"); if (l.LogDirectory != fallback) throw new Exception("wrong location"); });
            if (!File.ReadAllText(Path.Combine(fallback, "S6Patcher.log")).Contains("retained event") || diagnostics.IsEmpty)
                throw new Exception("fallback or notification missing");
        });

        test("logger survives write failure and retries using fallback", () =>
        {
            string app = NewDir(), fallback = NewDir();
            var broken = new FaultStream { FailWrite = true };
            usingLogger(new Logger(app, fallback, p => p.StartsWith(app, StringComparison.Ordinal) ? broken :
                new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read), _ => { }),
                l => l.Log("event survived"));
            if (!File.ReadAllText(Path.Combine(fallback, "S6Patcher.log")).Contains("event survived"))
                throw new Exception("event lost");
        });

        test("logger survives close failure and post-dispose calls", () =>
        {
            string app = NewDir();
            usingLogger(new Logger(app, NewDir(), _ => new FaultStream { FailClose = true }, _ => throw new IOException("diagnostic sink failed")),
                l => { l.Log("event"); l.Dispose(); l.Log("after disposal"); l.Dispose(); });
        });

        test("logger recovers after temporary fallback failure", () =>
        {
            string app = NewDir(), fallback = NewDir();
            int attempts = 0;
            usingLogger(new Logger(app, fallback, p =>
            {
                if (p.StartsWith(app, StringComparison.Ordinal) || ++attempts == 1)
                    throw new IOException("temporarily unavailable");
                return new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read);
            }, _ => { }), l => { l.Log("first lost"); l.Log("second retained"); });
            if (!File.ReadAllText(Path.Combine(fallback, "S6Patcher.log")).Contains("second retained"))
                throw new Exception("recovery failed");
        });

        test("logger concurrent writers retain complete lines", () =>
        {
            string app = NewDir();
            usingLogger(new Logger(app, NewDir(), p => new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read), _ => { }),
                l => Parallel.For(0, 128, n => l.Log("unique-" + n + "-end")));
            string[] lines = File.ReadAllLines(Path.Combine(app, "S6Patcher.log"));
            for (int i = 0; i < 128; i++)
                if (lines.Count(x => x.EndsWith("unique-" + i + "-end", StringComparison.Ordinal)) != 1)
                    throw new Exception("missing or interleaved line " + i);
        });
    }

    private static void usingLogger(Logger logger, Action<Logger> body)
    {
        try { body(logger); }
        finally { logger.Dispose(); }
    }
}
