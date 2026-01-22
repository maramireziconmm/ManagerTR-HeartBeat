using DenevaManagerTR.Application.LineasEstado;
using DenevaManagerTR.Application.Options;
using DenevaManagerTR.Core.Domain.Messages;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Core.Rabbit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

public sealed class InfoLineasEstadoConsumerHostedService : BackgroundService
{
    private readonly ILogger<InfoLineasEstadoConsumerHostedService> _logger;
    private readonly ILineasEstadosService _service;
    private readonly IDenevaConfigReader _config;
    private readonly ICryptoAdapter _crypto;
    private IConnection? _connection;
    private IModel? _channel;
    private string? _consumerTag;
    private readonly IInfoStationFileLogger _fileLog;
    private readonly IMessagePublisher _publisher;

    private readonly IOptionsMonitor<JobsOptions> _jobsOptions;
    private readonly IOptionsMonitor<DenevaConfigOptions> _denevaOptions;

    private const string SectionDeneva = "Deneva.My.MySettings";
    private const string KeyEmpresaDefecto = "EmpresaPorDefecto";

    public InfoLineasEstadoConsumerHostedService(
        ILogger<InfoLineasEstadoConsumerHostedService> logger,
        IOptionsMonitor<JobsOptions> jobsOptions,
        ILineasEstadosService service,
        IOptionsMonitor<DenevaConfigOptions> denevaOptions,
        IDenevaConfigReader config,
        ICryptoAdapter crypto,
        IInfoStationFileLogger fileLog,
        IMessagePublisher publisher)
    {
        _logger = logger;
        _service = service;
        _config = config;
        _crypto = crypto;
        _denevaOptions = denevaOptions;
        _fileLog = fileLog;
        _jobsOptions = jobsOptions;
        _publisher = publisher;
    }
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobCfg = _jobsOptions.CurrentValue.InfoLineasEstados;
        if (!jobCfg.Enabled)
        {
            _logger.LogInformation("InfoLineasEstados deshabilitado (Jobs:InfoLineasEstados:Enabled=false).");
            return Task.CompletedTask;
        }

        StartConsumer(jobCfg);

        // Mantener el hosted service vivo hasta cancelación
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }


    private void StartConsumer(InfoLineasEstados jobCfg)
    {
        var rabbitSection = _denevaOptions.CurrentValue.RabbitSection;

        // En Deneva.config el host se encuentra en RabbitMQ_ConnectionString
        var host = _config.Get(rabbitSection, RabbitConfigKeys.ConnectionString) ?? "localhost";
        var portStr = _config.Get(rabbitSection, RabbitConfigKeys.Port) ?? "5672";
        var vhost = _config.Get(rabbitSection, RabbitConfigKeys.VHost) ?? "/";
        var userEnc = _config.Get(rabbitSection, RabbitConfigKeys.User) ?? "guest";
        var passEnc = _config.Get(rabbitSection, RabbitConfigKeys.Pass) ?? "guest";

        var user = _crypto.Decrypt(userEnc);
        var pass = _crypto.Decrypt(passEnc);

        if (!int.TryParse(portStr, out var port)) port = 5672;

        var exchange = _config.Get(rabbitSection, RabbitConfigKeys.InfoEstacionExchange);
        if (string.IsNullOrWhiteSpace(exchange))
            throw new InvalidOperationException("RabbitMQ_InfoEstacionExchangeName viene vacío desde deneva.config.");

        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = user,
            Password = pass,
            VirtualHost = vhost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection($"{jobCfg.AppId}-InfoDenevaManagerTR");
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(
            exchange: exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

        _channel.QueueDeclare(
            queue: jobCfg.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var topics = (jobCfg.Topics != null && jobCfg.Topics.Count > 0)
            ? jobCfg.Topics
            : new List<string> { jobCfg.AppId };

        foreach (var t in topics)
            _channel.QueueBind(jobCfg.QueueName, exchange, t);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceivedAsync;

        _consumerTag = _channel.BasicConsume(
            queue: jobCfg.QueueName,
            autoAck: true,
            consumer: consumer);

        _logger.LogInformation("InfoStation consumer iniciado. Exchange={Exchange} Queue={Queue} Topics={Topics}",
            exchange, jobCfg.QueueName, string.Join(",", topics));
        _fileLog.Info($"InfoStation consumer iniciado. Exchange={exchange}, Queue={jobCfg.QueueName}, Topics={string.Join(",", topics)}");
    }

    //private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    //{
    //    string payload = string.Empty;

    //    try
    //    {
    //        payload = Encoding.UTF8.GetString(ea.Body.ToArray()).Trim();

    //        // Recorte a XML real
    //        var firstLt = payload.IndexOf('<');
    //        var lastGt = payload.LastIndexOf('>');
    //        if (firstLt >= 0 && lastGt > firstLt)
    //            payload = payload.Substring(firstLt, lastGt - firstLt + 1);

    //        XElement rootElement;
    //        try { rootElement = XElement.Parse(payload); }
    //        catch { return; }

    //        if (!rootElement.Name.LocalName.Equals("Message", StringComparison.OrdinalIgnoreCase))
    //            return;

    //        var msg = MessageMQ.Deserialize(payload);

    //        var contentType = msg.Header?.ContentType ?? "";
    //        if (!contentType.Equals("GETINFOLINEASESTADOS", StringComparison.OrdinalIgnoreCase))
    //            return;

    //        var inner = msg.MessageBody?.GenericXElement;
    //        if (inner is null) return;

    //        var objIdStr = (string?)inner.Attribute("objid");
    //        if (!int.TryParse(objIdStr, out var objID) || objID <= 0) return;

    //        // EmpresaPorDefecto desde deneva.config
    //        var empresaStr = _config.Get(SectionDeneva, KeyEmpresaDefecto) ?? "0";
    //        if (!int.TryParse(empresaStr, out var empresa) || empresa <= 0)
    //        {
    //            _logger.LogWarning("EmpresaPorDefecto inválida en deneva.config: {Empresa}", empresaStr);
    //            return;
    //        }

    //        var xmlCompleto = await _service.GetInfoLineasEstadosAsync(empresa, CancellationToken.None);
    //        if (string.IsNullOrWhiteSpace(xmlCompleto)) return;

    //        // Respuesta
    //        var response = new MessageMQ { Version = "1.0" };
    //        response.Header.OriginSystem = $"Servidor_{objID}_WSTransit";
    //        response.Header.ContentType = "SYNCINFOLINEASESTADOSTM_RETURN";
    //        response.Header.Timestamp = response.TimestampMiliseconds();
    //        response.MessageBody.GenericXElement = XElement.Parse(xmlCompleto);

    //        var outPayload = response.Serialize();
    //        var outBody = new UTF8Encoding(false).GetBytes(outPayload);

    //        // Publicación: misma exchange y routingKey que tu legacy
    //        var exchange = /* tu exchange */;
    //        var routingKey = $"PorObjectID.{objID}";

    //        var props = _channel!.CreateBasicProperties();
    //        props.DeliveryMode = 1;
    //        props.ContentType = "0";

    //        _channel.BasicPublish(exchange, routingKey, props, outBody);
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Error en InfoLinesEstadoConsumer. RoutingKey={RoutingKey}", ea.RoutingKey);
    //    }
    //}


    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        string payload = string.Empty;

        try
        {
            payload = Encoding.UTF8.GetString(ea.Body.ToArray());

            // 1) Sanitizar (recortar a XML real)
            payload = payload.Trim();
            var firstLt = payload.IndexOf('<');
            var lastGt = payload.LastIndexOf('>');
            if (firstLt >= 0 && lastGt > firstLt)
                payload = payload.Substring(firstLt, lastGt - firstLt + 1);

            // 2) Validar XML básico
            XElement rootElement;
            try
            {
                rootElement = XElement.Parse(payload);
            }
            catch (Exception pex)
            {
                _logger.LogWarning(pex,
                    "PAYLOAD NO XML. RoutingKey={RoutingKey}. Raw={Payload}",
                    ea.RoutingKey, payload);
                return;
            }

            var rootName = rootElement.Name.LocalName;
            if (!rootName.Equals("Message", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "FRAGMENT XML (sin envelope) IGNORADO: Root={Root} RoutingKey={RoutingKey}. Payload={Payload}",
                    rootName, ea.RoutingKey, payload);
                return;
            }

            // 3) Deserializar MessageMQ
            MessageMQ msg;
            try
            {
                msg = MessageMQ.Deserialize(payload);
            }
            catch (Exception dex)
            {
                _logger.LogError(dex,
                    "Error deserializando MessageMQ. RoutingKey={RoutingKey}. Payload={Payload}",
                    ea.RoutingKey, payload);
                // log raw también
                _fileLog.Xml(payload);
                return;
            }



            var origin = msg.Header?.OriginSystem ?? string.Empty;
            var contentType = msg.Header?.ContentType ?? string.Empty;
            CancellationToken ck = new CancellationToken();
            if (string.Equals(contentType, "GETINFOLINEASESTADOS", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetInfoLineasEstadosAsync(msg, ea.RoutingKey, ck);
                return;
            }

            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error en OnMessageReceivedAsync. RoutingKey={RoutingKey}. Payload={Payload}",
                ea.RoutingKey, payload);
        }
    }

    private async Task HandleGetInfoLineasEstadosAsync(MessageMQ msg, string routingKey, CancellationToken ct)
    {
        var opts = _jobsOptions.CurrentValue.InfoLineasEstados;

        // objid desde GenericXElement @objid
        var inner = msg.MessageBody?.GenericXElement;
        if (inner is null)
        {
            _logger.LogWarning("GETINFOLINEASESTADOS sin GenericXElement. routingKey={RK}", routingKey);
            return;
        }

        var objIdStr = (string?)inner.Attribute("objid");
        if (!int.TryParse(objIdStr, out var objID) || objID <= 0)
        {
            _logger.LogWarning("GETINFOLINEASESTADOS objid inválido. objid={ObjId} routingKey={RK}", objIdStr, routingKey);
            return;
        }

        var empresa = _config.Get("Deneva.My.MySettings", "EmpresaPorDefecto") ?? string.Empty;

        string xmlCompleto;
        try
        {
            xmlCompleto = await _service.GetLineasEstadosAsync(int.Parse(empresa), ct);
            if (string.IsNullOrWhiteSpace(xmlCompleto))
            {
                _logger.LogWarning("LineasEstados XML vacío. empresa={Empresa} objid={ObjId}", empresa, objID);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando XML LineasEstados. empresa={Empresa} objid={ObjId}", empresa, objID);
            return;
        }

        // PUBLICACIÓN: clave para evitar &lt; &gt;
        //  - GenericXElement debe ser XElement.Parse(xmlCompleto)
        //  - NO metas xmlCompleto como string dentro del XML
        var response = new MessageMQ { Version = "1.0" };
        response.Header.OriginSystem = $"Servidor_60_WSTransit"; //cuando sea bastión objectid de bastion
        response.Header.ContentType = "SYNCINFOLINEASESTADOSTM_RETURN";
        response.Header.Timestamp = response.TimestampMiliseconds();

        response.MessageBody.GenericXElement = System.Xml.Linq.XElement.Parse(xmlCompleto);

        var outPayload = response.Serialize();
        var outBody = Encoding.UTF8.GetBytes(outPayload);
        var rabbitSection = _denevaOptions.CurrentValue.RabbitSection;
        var exchange = _config.Get(rabbitSection, RabbitConfigKeys.InfoEstacionExchange);

        //// Misma regla que ya usas en GETINFOSTATION
        //var rkOut = string.IsNullOrWhiteSpace(msg.Header?.ReplyTo)
        //    ? $"PorObjectID.{objID}"
        //    : msg.Header!.ReplyTo!;
        var tmproutingKey = $"PorObjectID.{objID}";
        try
        {
            //_publisher.Publish(exchange, rkOut, outBody);


            var props = _channel.CreateBasicProperties();
            props.ReplyTo = string.Empty;
            props.DeliveryMode = 1; // transient
            props.ContentType = "0"; // como tu legacy (si lo necesitáis así)

            _channel.BasicPublish(
                exchange: exchange,
                routingKey: tmproutingKey,
                basicProperties: props,
                mandatory: true,
                body: outBody
            );
            _logger.LogInformation("Publicado SYNCINFOLINEASESTADOSTM_RETURN exchange={Exchange} rk={RK} bytes={Bytes}",
                exchange, tmproutingKey, outBody.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publicando SYNCINFOLINEASESTADOSTM_RETURN exchange={Exchange} rk={RK}", exchange, tmproutingKey);
        }
    }
    public override void Dispose()
    {
        try { _channel?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
