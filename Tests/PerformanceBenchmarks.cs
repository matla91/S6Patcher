using System.Diagnostics;
using S6Patcher.Source.Utilities;

internal static class PerformanceBenchmarks
{
    // Measurements are informational, not assertions or CI performance gates.
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "s6patcher-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Console.WriteLine("S6Patcher synthetic performance baseline (no game files)");
            Console.WriteLine($"Runtime={Environment.Version}; OS={Environment.OSVersion}; CPU={Environment.ProcessorCount}; mode={(Debugger.IsAttached ? "debugger" : "normal")}");
            Paths(root);
            Recovery(root);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Paths(string root)
    {
        string baseDir = Path.Combine(root, "path-fixture");
        Directory.CreateDirectory(baseDir);
        const int count = 400;
        string[] requested = new string[count];
        for (int i = 0; i < count; i++)
        {
            string directory = Path.Combine(baseDir, $"Group{i / 40:D2}");
            Directory.CreateDirectory(directory);
            string filename = $"Asset{i:D4}.xml";
            File.WriteAllText(Path.Combine(directory, filename), "fixture");
            requested[i] = Path.Combine(baseDir, $"group{i / 40:D2}", $"asset{i:D4}.XML");
        }
        Measure("ResolveCaseInsensitivePath / 400 mixed-case paths", 5, () =>
        {
            foreach (string path in requested)
            {
                string resolved = Utility.ResolveCaseInsensitivePath(path);
                if (!File.Exists(resolved)) throw new InvalidOperationException("Path resolution failed");
            }
        });
        Measure("File.Exists / 400 matching-case paths (context only)", 5, () =>
        {
            for (int i = 0; i < count; i++)
                if (!File.Exists(Path.Combine(baseDir, $"Group{i / 40:D2}", $"Asset{i:D4}.xml")))
                    throw new InvalidOperationException("Fixture missing");
        });
    }

    private static void Recovery(string root)
    {
        string dir = Path.Combine(root, "recovery-fixture");
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "Settlers6.exe");
        string options = Path.Combine(dir, "Options.ini");
        string mod = Path.Combine(dir, "shr");
        Directory.CreateDirectory(mod);
        byte[] executable = new byte[4 * 1024 * 1024];
        new Random(42).NextBytes(executable);
        File.WriteAllBytes(exe, executable);
        File.WriteAllText(options, "[S6Patcher]\\r\\n");
        for (int i = 0; i < 32; i++)
            File.WriteAllBytes(Path.Combine(mod, $"Asset{i:D2}.bin"), new byte[8192]);

        Measure("P10 Begin + Commit / 4 MiB EXE, 32 mod files", 5, () =>
        {
            using var session = PatchSessionRecovery.Begin(exe, [exe, options], [mod]);
            session.Commit();
        });
        Measure("P10 Begin + rollback / 4 MiB EXE, 32 mod files", 5, () =>
        {
            using var session = PatchSessionRecovery.Begin(exe, [exe, options], [mod]);
            session.RecoverFromJournal();
            if (!File.ReadAllBytes(exe).AsSpan().SequenceEqual(executable))
                throw new InvalidOperationException("Recovery altered baseline executable");
        });
    }

    private static void Measure(string label, int repetitions, Action operation)
    {
        operation(); // warm-up; excludes first-call and JIT cost
        var elapsed = new double[repetitions];
        var allocations = new long[repetitions];
        for (int i = 0; i < repetitions; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            operation();
            elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(elapsed);
        Array.Sort(allocations);
        Console.WriteLine($"{label}: median={elapsed[repetitions / 2]:F2} ms; range={elapsed[0]:F2}-{elapsed[^1]:F2} ms; thread_alloc_median={allocations[repetitions / 2] / 1024.0:F1} KiB");
    }
}
