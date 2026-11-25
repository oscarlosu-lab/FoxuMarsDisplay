using System.Diagnostics;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;

namespace FoxuMarsDisplay.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        int benchmarkSec = GetIntArg(args, "--benchmark", 0);
        int fps = GetIntArg(args, "--fps", 0);
        int logLevel = GetIntArg(args, "--log-level", 0);

        var store = new FoxuDisplayConfigStore();
        var config = store.LoadOrImportSoeyi();
        if (fps > 0) config.TargetFps = fps;
        if (logLevel > 0) config.NativeLogLevel = logLevel;

        using var host = new DisplayHost(configStore: store, config: config);
        host.Start(dryRun: dryRun, enableIpc: false);

        if (benchmarkSec > 0)
        {
            var proc = Process.GetCurrentProcess();
            TimeSpan cpuStart = proc.TotalProcessorTime;
            var sw = Stopwatch.StartNew();

            await Task.Delay(TimeSpan.FromSeconds(benchmarkSec)).ConfigureAwait(false);

            sw.Stop();
            proc.Refresh();
            TimeSpan cpuDelta = proc.TotalProcessorTime - cpuStart;
            double cpuPct = (cpuDelta.TotalMilliseconds / (sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount)) * 100.0;
            double ramMb = proc.PrivateMemorySize64 / (1024.0 * 1024.0);

            var snap = host.CurrentSensors;
            Console.WriteLine($"[BENCHMARK {benchmarkSec}s] CPU={cpuPct:F2}% | PrivateRAM={ramMb:F1} MB | Threads={proc.Threads.Count}");
            Console.WriteLine($"[SENSORS] CPU={snap.CpuTemp:F1}°C ({snap.CpuUsage:F0}% | {snap.CpuPowerW:F1}W) | GPU={snap.GpuTemp:F1}°C ({snap.GpuUsage:F0}% | {snap.GpuPowerW:F1}W) | RAM={snap.MemoryUsagePct:F0}% | Net={snap.NetDownKbps:F0}/{snap.NetUpKbps:F0} KB/s");

            foreach (var s in host.GetStatuses())
            {
                Console.WriteLine($"  [{s.SerialNumber}] {s.Width}x{s.Height} | Theme={s.ThemeName} | Sent={s.FramesSent} | KeepAlive={s.KeepAlivesSent} | Skipped={s.FramesSkipped} | BusyDrops={s.BusyDrops} | Errors={s.SendErrors}");
            }

            host.Stop();
            return 0;
        }

        Console.WriteLine("FoxuMarsDisplay.Cli running. Press Ctrl+C to exit.");
        using var exitEvent = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exitEvent.Set();
        };
        exitEvent.Wait();
        host.Stop();
        return 0;
    }

    private static int GetIntArg(string[] args, string name, int fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int val))
                return val;
        }
        return fallback;
    }
}
