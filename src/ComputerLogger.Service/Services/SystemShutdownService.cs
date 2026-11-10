using System.Diagnostics;

namespace ComputerLogger.Service.Services;

// Shuts down the machine the service runs on (Windows, Linux, or the Linux host when running in a privileged container)
public sealed class SystemShutdownService
{
    private readonly ILogger<SystemShutdownService> logger;

    public SystemShutdownService(ILogger<SystemShutdownService> logger) => this.logger = logger;

    public void Shutdown()
    {
        var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "shutdown";
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/t");
            psi.ArgumentList.Add("0");
        }
        else if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            // Enter the host's namespaces (needs `privileged: true` and `pid: host`) so we shut down the host, not the container
            psi.FileName = "nsenter";
            foreach (var arg in new[] { "--target", "1", "--mount", "--uts", "--ipc", "--net", "--pid", "--", "shutdown", "now" }) psi.ArgumentList.Add(arg);
        }
        else if (Environment.IsPrivilegedProcess)
        {
            psi.FileName = "shutdown";
            psi.ArgumentList.Add("now");
        }
        else
        {
            psi.FileName = "sudo";
            psi.ArgumentList.Add("shutdown");
            psi.ArgumentList.Add("now");
        }

        logger.LogWarning("Shutdown requested over MQTT, running: {Command} {Args}", psi.FileName, string.Join(' ', psi.ArgumentList));
        try { Process.Start(psi); }
        catch (Exception ex) { logger.LogError(ex, "Failed to start shutdown command."); }
    }
}
