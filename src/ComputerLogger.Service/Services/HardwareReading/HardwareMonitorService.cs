using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace ComputerLogger.Service.Services.HardwareReading;

// Wraps LibreHardwareMonitorLib to expose the CPU package and GPU power sensors
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer computer;
    private readonly UpdateVisitor updateVisitor = new();
    private readonly ILogger<HardwareMonitorService> logger;
    private bool disposed;

    public HardwareMonitorService(ILogger<HardwareMonitorService> logger)
    {
        this.logger = logger;
        computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
        };
        computer.Open();
        computer.Accept(updateVisitor);
    }

    public PowerReadings ReadPower()
    {
        float? cpuWatts = null;
        float? gpuWatts = null;

        foreach (var hardware in computer.Hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Update();
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Power || sensor.Value is not { } value) continue;
                
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu when IsPackagePowerSensor(sensor.Name):
                        cpuWatts = value;
                        break;
                    case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel
                        when IsPackagePowerSensor(sensor.Name):
                        gpuWatts = value;
                        break;
                }
            }
        }

        if (cpuWatts is null) logger.LogWarning("No CPU power sensor was found. Some CPUs/motherboards do not expose package power - check LibreHardwareMonitor's own GUI to see what's available.");
        if (gpuWatts is null) logger.LogDebug("No GPU power sensor was found.");
        return new PowerReadings(cpuWatts, gpuWatts);
    }

    public void Dispose()
    {
        if (disposed) return;
        computer.Close();
        disposed = true;
    }

    // Matches the common LibreHardwareMonitor sensor names across vendors
    private static bool IsPackagePowerSensor(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase)
        || name.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CPU Power", StringComparison.OrdinalIgnoreCase);
}
