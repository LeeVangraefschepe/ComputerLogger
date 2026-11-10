using System.Text.Json;
using System.Text.RegularExpressions;
using ComputerLogger.Service.Configuration;
using ComputerLogger.Service.Services;
using ComputerLogger.Service.Services.HardwareReading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace ComputerLogger.Service.Services.Mqtt;

// Publishes power readings to Home Assistant over MQTT
public sealed class MqttPublisherService : IAsyncDisposable
{
    private static readonly string[] SensorKeys = ["cpu_power", "gpu_power", "total_power"];

    private readonly IMqttClient client;
    private readonly MqttClientOptions clientOptions;
    private readonly MqttOptions options;
    private readonly ILogger<MqttPublisherService> logger;
    private readonly string devicePrefix;
    private readonly string stateTopic;
    private readonly string shutdownCommandTopic;
    private readonly string shutdownStateTopic;
    private readonly SystemShutdownService shutdownService;
    private readonly SemaphoreSlim connectLock = new(1, 1);
    private bool discoveryPublished;

    public MqttPublisherService(IOptions<MqttOptions> options, SystemShutdownService shutdownService, ILogger<MqttPublisherService> logger)
    {
        this.shutdownService = shutdownService;
        this.options = options.Value;
        this.logger = logger;
        devicePrefix = Slugify(this.options.DevicePrefix);
        stateTopic = $"{this.options.DiscoveryPrefix}/sensor/{devicePrefix}/state";

        shutdownCommandTopic = $"{this.options.DiscoveryPrefix}/switch/{devicePrefix}_shutdown/set";
        shutdownStateTopic = $"{this.options.DiscoveryPrefix}/switch/{devicePrefix}_shutdown/state";

        var factory = new MqttClientFactory();
        client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(this.options.Host, this.options.Port)
            .WithClientId(this.options.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            // If this app disappears without a clean disconnect, the broker publishes this on our behalf (sensor reset)
            .WithWillTopic(stateTopic)
            .WithWillPayload(BuildStatePayload(cpu: 0, gpu: 0, total: 0))
            .WithWillRetain(true);

        if (!string.IsNullOrWhiteSpace(this.options.Username)) optionsBuilder = optionsBuilder.WithCredentials(this.options.Username, this.options.Password);
        clientOptions = optionsBuilder.Build();
    }

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_');
        return string.IsNullOrEmpty(slug) ? "pc" : slug;
    }

    private static string BuildStatePayload(float cpu, float gpu, float total) => JsonSerializer.Serialize(new
    {
        cpu_power = cpu,
        gpu_power = gpu,
        total_power = total,
    });
    
    private async Task EnsureDiscoveryPublishedAsync(CancellationToken cancellationToken)
    {
        if (discoveryPublished) return;

        var device = new
        {
            identifiers = new[] { $"energylogger_{devicePrefix}" },
            name = options.DeviceName,
            manufacturer = "ComputerLogger",
            model = "LibreHardwareMonitor",
        };

        foreach (var key in SensorKeys)
        {
            var config = new
            {
                name = $"{options.DeviceName} {FriendlyNameSuffix(key)}",
                unique_id = $"energylogger_{devicePrefix}_{key}",
                state_topic = stateTopic,
                value_template = $"{{{{ value_json.{key} }}}}",
                unit_of_measurement = "W",
                device_class = "power",
                state_class = "measurement",
                device,
            };
            var payload = JsonSerializer.Serialize(config);
            await PublishAsync(DiscoveryConfigTopic(key), payload, retain: true, cancellationToken);
        }

        var shutdownConfig = new
        {
            name = $"{options.DeviceName} Shutdown",
            unique_id = $"energylogger_{devicePrefix}_shutdown",
            command_topic = shutdownCommandTopic,
            state_topic = shutdownStateTopic,
            payload_on = "ON",
            payload_off = "OFF",
            icon = "mdi:power",
            device,
        };
        await PublishAsync($"{options.DiscoveryPrefix}/switch/{devicePrefix}_shutdown/config", JsonSerializer.Serialize(shutdownConfig), retain: true, cancellationToken);
        await PublishAsync(shutdownStateTopic, "OFF", retain: true, cancellationToken);

        discoveryPublished = true;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        // Retained messages are ignored so a stale ON can never shut the machine down again after boot
        if (e.ApplicationMessage.Topic != shutdownCommandTopic || e.ApplicationMessage.Retain) return;
        if (!string.Equals(e.ApplicationMessage.ConvertPayloadToString()?.Trim(), "ON", StringComparison.OrdinalIgnoreCase)) return;

        // Reset the flag first so it is already false when the machine comes back up
        await PublishAsync(shutdownStateTopic, "OFF", retain: true, CancellationToken.None);
        shutdownService.Shutdown();
    }

    private static string FriendlyNameSuffix(string key) => key switch
    {
        "cpu_power" => "CPU Power",
        "gpu_power" => "GPU Power",
        "total_power" => "Total Power",
        _ => key,
    };

    private string DiscoveryConfigTopic(string key) => $"{options.DiscoveryPrefix}/sensor/{devicePrefix}_{key}/config";
    
    public async Task PublishReadingsAsync(PowerReadings readings, bool includeTotalPower, CancellationToken cancellationToken)
    {
        try { await EnsureConnectedAsync(cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return; }

        await EnsureDiscoveryPublishedAsync(cancellationToken);

        var values = new Dictionary<string, float>();
        if (readings.CpuWatts is { } cpu) values["cpu_power"] = cpu;
        if (readings.GpuWatts is { } gpu) values["gpu_power"] = gpu;
        if (includeTotalPower && readings.TotalWatts is { } total) values["total_power"] = total;

        if (values.Count == 0) return;

        await PublishAsync(stateTopic, JsonSerializer.Serialize(values), retain: true, cancellationToken);
    }

    private async Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken)
    {
        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .Build();

            await client.PublishAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Failed to publish MQTT topic {Topic}.", topic); }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (client.IsConnected) return;

        await connectLock.WaitAsync(cancellationToken);
        try
        {
            if (client.IsConnected) return;

            logger.LogInformation("Connecting to MQTT broker at {Host}:{Port}...", options.Host, options.Port);
            await client.ConnectAsync(clientOptions, cancellationToken);
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(shutdownCommandTopic).Build(), cancellationToken);
            discoveryPublished = false; // republish discovery configs after every (re)connect
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to MQTT broker at {Host}:{Port}.", options.Host, options.Port);
            throw;
        }
        finally { connectLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (client.IsConnected)
        {
            // When clean disposing set all sensors back to 0
            try { await PublishAsync(stateTopic, BuildStatePayload(cpu: 0, gpu: 0, total: 0), retain: true, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to publish zeroed state on shutdown."); }
            await client.DisconnectAsync();
        }
        client.Dispose();
        connectLock.Dispose();
    }
}
