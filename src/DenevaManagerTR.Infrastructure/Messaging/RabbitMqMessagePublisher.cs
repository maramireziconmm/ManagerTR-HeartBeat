using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Core.Rabbit;

namespace DenevaManagerTR.Infrastructure.Messaging;

public sealed class RabbitMqMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly ILogger<RabbitMqMessagePublisher> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqMessagePublisher(
        ILogger<RabbitMqMessagePublisher> logger,
        IDenevaConfigReader config,
        ICryptoAdapter crypto,
        IOptionsMonitor<DenevaConfigOptions> denevaOptions)
    {
        _logger = logger;

        var section = denevaOptions.CurrentValue.RabbitSection;

        var host = config.Get(section, RabbitConfigKeys.ConnectionString) ?? "localhost";
        var portStr = config.Get(section, RabbitConfigKeys.Port);
        var vhost = config.Get(section, RabbitConfigKeys.Vhost) ?? "/";
        var userEnc = config.Get(section, RabbitConfigKeys.User) ?? "guest";
        var passEnc = config.Get(section, RabbitConfigKeys.Pass) ?? "guest";

        var user = crypto.Decrypt(userEnc);
        var pass = crypto.Decrypt(passEnc);

        var port = 5672;
        if (!string.IsNullOrWhiteSpace(portStr) && int.TryParse(portStr, out var p))
            port = p;

        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            VirtualHost = vhost,
            UserName = user,
            Password = pass,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        _connection = factory.CreateConnection("DenevaManagerTR.Publisher");
        _channel = _connection.CreateModel();

        _logger.LogInformation("RabbitMQ conectado. Host={Host} Port={Port} VHost={VHost}", host, port, vhost);
    }

        public Task PublishAsync(PublishedMessage message, CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(message.Payload);

        var props = _channel.CreateBasicProperties();

        // Legacy defaults
        props.ContentType = "0";
        props.DeliveryMode = 1;
        props.ReplyTo = string.Empty;

        // Opcionales vía headers
        if (message.Headers is not null)
        {
            if (message.Headers.TryGetValue("amqp_content_type", out var ctObj) && ctObj is string ct && !string.IsNullOrWhiteSpace(ct))
                props.ContentType = ct;

            if (message.Headers.TryGetValue("amqp_app_id", out var appObj) && appObj is string appId && !string.IsNullOrWhiteSpace(appId))
                props.AppId = appId;

            if (message.Headers.TryGetValue("amqp_expiration", out var expObj) && expObj is string exp && !string.IsNullOrWhiteSpace(exp))
                props.Expiration = exp;

            if (message.Headers.TryGetValue("amqp_delivery_mode", out var dmObj))
            {
                if (dmObj is byte dmB) props.DeliveryMode = dmB;
                else if (dmObj is int dmI) props.DeliveryMode = (byte)dmI;
            }
        }

        _channel.BasicPublish(
            exchange: message.Exchange,
            routingKey: message.RoutingKey,
            mandatory: true,
            basicProperties: props,
            body: body);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _channel?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
    }
}
