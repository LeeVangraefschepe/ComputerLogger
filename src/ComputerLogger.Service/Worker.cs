using ComputerLogger.Service.Configuration;
using ComputerLogger.Service.Services.HardwareReading;
using ComputerLogger.Service.Services.Mqtt;
using Microsoft.Extensions.Options;

namespace ComputerLogger.Service;

// Periodically reads CPU/GPU power sensors and publishes them over MQTT
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly HardwareMonitorService _hardwareMonitor;
    private readonly MqttPublisherService _mqttPublisher;
    private readonly MonitoringOptions _monitoringOptions;

    public Worker(
        ILogger<Worker> logger,
        HardwareMonitorService hardwareMonitor,
        MqttPublisherService mqttPublisher,
        IOptions<MonitoringOptions> monitoringOptions)
    {
        _logger = logger;
        _hardwareMonitor = hardwareMonitor;
        _mqttPublisher = mqttPublisher;
        _monitoringOptions = monitoringOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _monitoringOptions.PollingIntervalSeconds));
        _logger.LogInformation("ComputerLogger starting, polling every {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var readings = _hardwareMonitor.ReadPower();
                await _mqttPublisher.PublishReadingsAsync(readings, _monitoringOptions.IncludeTotalPower, stoppingToken);
                _logger.LogDebug("Published CPU={Cpu}W GPU={Gpu}W Total={Total}W", readings.CpuWatts, readings.GpuWatts, readings.TotalWatts);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error while polling sensors / publishing over MQTT."); }
 
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
