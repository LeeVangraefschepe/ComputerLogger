namespace ComputerLogger.Service.Services.HardwareReading;

public sealed record PowerReadings(float? CpuWatts = null, float? GpuWatts = null)
{
    public float? TotalWatts => CpuWatts is null && GpuWatts is null ? null : (CpuWatts ?? 0f) + (GpuWatts ?? 0f);
}
