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

builder.Services.AddSingleton<HardwareMonitorService>();
builder.Services.AddSingleton<MqttPublisherService>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ComputerLogger";
});
builder.Services.Configure<EventLogSettings>(settings =>
{
    settings.SourceName = "ComputerLogger";
});

var host = builder.Build();
host.Run();
