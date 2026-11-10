using ComputerLogger.Service;
using ComputerLogger.Service.Configuration;
using ComputerLogger.Service.Services.HardwareReading;
using ComputerLogger.Service.Services.Mqtt;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));

if (OperatingSystem.IsWindows()) builder.Services.AddSingleton<IPowerReader, HardwareMonitorService>();
else builder.Services.AddSingleton<IPowerReader, LinuxPowerReader>();
builder.Services.AddSingleton<MqttPublisherService>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ComputerLogger";
});
if (OperatingSystem.IsWindows()) builder.Services.Configure<EventLogSettings>(settings =>
{
    if (OperatingSystem.IsWindows()) settings.SourceName = "ComputerLogger";
});

var host = builder.Build();
host.Run();
