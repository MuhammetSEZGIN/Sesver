namespace Shared.Contracts;

public class RabbitMqOptions
{
    public string HostName { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string QueueName { get; set; } = string.Empty;
}
