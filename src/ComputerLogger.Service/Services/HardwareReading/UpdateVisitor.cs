using LibreHardwareMonitor.Hardware;

namespace ComputerLogger.Service.Services.HardwareReading;

// Walks the LibreHardwareMonitor hardware tree and refreshes every device's sensor values
internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
