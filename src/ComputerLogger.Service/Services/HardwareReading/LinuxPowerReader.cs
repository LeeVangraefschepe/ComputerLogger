using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ComputerLogger.Service.Services.HardwareReading;

// CPU package power from RAPL (powercap sysfs), GPU power from nvidia-smi if available
public sealed class LinuxPowerReader : IPowerReader
{
    private const string PowercapRoot = "/sys/class/powercap";

    private readonly ILogger<LinuxPowerReader> logger;
    private readonly List<RaplDomain> domains = [];
    private bool nvidiaSmiMissing;

    private sealed class RaplDomain(string energyPath, long maxRangeUj)
    {
        public string EnergyPath { get; } = energyPath;
        public long MaxRangeUj { get; } = maxRangeUj;
        public long? LastEnergyUj { get; set; }
        public long LastTimestamp { get; set; }
    }

    public LinuxPowerReader(ILogger<LinuxPowerReader> logger)
    {
        this.logger = logger;
        DiscoverRaplDomains();
    }

    private void DiscoverRaplDomains()
    {
        if (!Directory.Exists(PowercapRoot))
        {
            logger.LogWarning("{Path} not found; CPU power is unavailable (RAPL not supported by this host/kernel).", PowercapRoot);
            return;
        }
        
        foreach (var dir in Directory.GetDirectories(PowercapRoot, "intel-rapl:*").Order())
        {
            if (Path.GetFileName(dir).Count(c => c == ':') != 1) continue;
            var energyPath = Path.Combine(dir, "energy_uj");
            if (TryReadLong(energyPath) is null)
            {
                logger.LogWarning("Cannot read {Path} (permission denied?). Try running the container as root or privileged.", energyPath);
                continue;
            }
            var range = TryReadLong(Path.Combine(dir, "max_energy_range_uj")) ?? long.MaxValue;
            domains.Add(new RaplDomain(energyPath, range));
        }
    }

    public PowerReadings ReadPower()
    {
        var cpu = ReadCpuWatts();
        if (cpu is null) logger.LogWarning("No CPU power sensor was found (RAPL unavailable or not yet sampled).");
        return new PowerReadings(cpu, ReadGpuWatts());
    }

    // Watts = energy delta since the previous poll / elapsed time
    private float? ReadCpuWatts()
    {
        double total = 0;
        var any = false;
        var now = Stopwatch.GetTimestamp();

        foreach (var d in domains)
        {
            var energy = TryReadLong(d.EnergyPath);
            if (energy is null) continue;

            if (d.LastEnergyUj is { } last)
            {
                var delta = energy.Value - last;
                if (delta < 0) delta += d.MaxRangeUj; // counter wrapped
                var seconds = Stopwatch.GetElapsedTime(d.LastTimestamp, now).TotalSeconds;
                if (seconds > 0)
                {
                    total += delta / 1_000_000.0 / seconds;
                    any = true;
                }
            }
            d.LastEnergyUj = energy;
            d.LastTimestamp = now;
        }

        return any ? (float)total : null;
    }

    private float? ReadGpuWatts()
    {
        if (nvidiaSmiMissing) return null;
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=power.draw --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) { process.Kill(); return null; }
            if (process.ExitCode != 0) return null;

            double sum = 0;
            var any = false;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) continue;
                sum += w; any = true;
            }
            return any ? (float)sum : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            nvidiaSmiMissing = true;
            logger.LogDebug("nvidia-smi not available; GPU power will not be reported.");
            return null;
        }
    }

    private static long? TryReadLong(string path)
    {
        try { return long.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, out var v) ? v : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
