namespace ComputerLogger.Service.Configuration;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";
    
    public string Host { get; set; } = "192.168.50.101"; // Broker hostname or IP
    public int Port { get; set; } = 1883; // Broker port
    public string Username { get; set; } = ""; // Username credential for MQTT broker
    public string Password { get; set; } = ""; // Password credential for MQTT broker
    public string ClientId { get; set; } = "computerlogger";
    public string DiscoveryPrefix { get; set; } = "homeassistant";
    public string DevicePrefix { get; set; } = "pc"; // Unique PC id used in entity ids, e.g. sensor.pc_cpu_power
    public string DeviceName { get; set; } = "PC"; // Device name shown in Home Assistant
}

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int PollingIntervalSeconds { get; set; } = 5; // Seconds between sensor reads/publishes
    public bool IncludeTotalPower { get; set; } = true; // Also publish CPU + GPU total
}
