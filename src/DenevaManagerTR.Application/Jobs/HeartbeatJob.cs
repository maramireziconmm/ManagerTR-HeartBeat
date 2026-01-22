using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DenevaManagerTR.Application.Builders;
using DenevaManagerTR.Application.Options;
using DenevaManagerTR.Core.Domain.Messages;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Core.Rabbit;

namespace DenevaManagerTR.Application.Jobs;

public sealed class HeartbeatJob : IJob
{
    private readonly ILogger<HeartbeatJob> _logger;
    private readonly IOptionsMonitor<JobsOptions> _jobsOptions;
    private readonly IOptionsMonitor<DenevaConfigOptions> _denevaOptions;
    private readonly IDenevaConfigReader _config;
    private readonly IMessagePublisher _publisher;
    private readonly ITransitRepository _transit;
    private readonly IHeartbeatFileLogger _fileLog;

    public string Name => "HeartBeat";

    public HeartbeatJob(
        ILogger<HeartbeatJob> logger,
        IOptionsMonitor<JobsOptions> jobsOptions,
        IOptionsMonitor<DenevaConfigOptions> denevaOptions,
        IDenevaConfigReader config,
        IMessagePublisher publisher,
        ITransitRepository transit,
        IHeartbeatFileLogger fileLog)
    {
        _logger = logger;
        _jobsOptions = jobsOptions;
        _denevaOptions = denevaOptions;
        _config = config;
        _publisher = publisher;
        _transit = transit;
        _fileLog = fileLog;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var jobCfg = _jobsOptions.CurrentValue.Heartbeat;
        var routingKey = jobCfg.Msg_topic_HeartBeat;

        var rabbitSection = _denevaOptions.CurrentValue.RabbitSection;
        var exchange = _config.Get(rabbitSection, RabbitConfigKeys.HeartbeatExchange) ?? "DenevaHeartBeatExchange";
        var timeout = _config.Get(rabbitSection, RabbitConfigKeys.TimeoutMessage);

        // LastUpdate: transact_tables.Tabla = 'servidores_infostation'
        var lastUpdateMs = await _transit.GetLastUpdateMsAsync("servidores_infostation", cancellationToken);

        // SE (si aplica):
        // - si la vista devuelve filas, filtrar por IdSistema_externo
        // - si la vista no devuelve, coger los Enabled=1
        var seEntries = await _transit.GetHeartbeatSeEntriesAsync(cancellationToken);

        // 1) XML HeartBeat (exacto)
        var heartbeatXml = HeartbeatXmlBuilder.Build(
            originSystemValue: "ManagerTR",
            modoSeguimiento: 2,
            modoTrabajo: 2,
            lastUpdateMs: lastUpdateMs,
            infoHorario: "",
            seEntries: seEntries
        );

        // 2) Envolver en MessageMQ (formato legacy)
        var msg = new MessageMQ { Version = "1.0" };
        msg.Header.OriginSystem = jobCfg.Msg_OriginSystem_HeartBeat;
        msg.Header.ContentType = jobCfg.Msg_contentType_HeartBeat;
        msg.Header.Timestamp = msg.TimestampMiliseconds();
        msg.MessageBody.GenericXElement = XElement.Parse(heartbeatXml);

        var payload = msg.Serialize();

        // 3) Publicar con properties legacy
        var headers = new Dictionary<string, object?>
        {
            ["ContentType"] = "0",
            ["AppId"] = "ManagerTR",
            ["ReplyTo"] = string.Empty,
            ["DeliveryMode = 1; // transient\n"] = 1,
        };

        if (!string.IsNullOrWhiteSpace(timeout))
            headers["Expiration"] = timeout;

        _fileLog.Info($"Publicando HeartBeat. Origin={jobCfg.Msg_OriginSystem_HeartBeat}, Exchange={exchange}, RoutingKey={routingKey}, ContentType={jobCfg.Msg_contentType_HeartBeat}");
        _fileLog.Xml(payload);

        await _publisher.PublishAsync(
            new PublishedMessage(exchange, routingKey, payload, headers),
            cancellationToken
        );

        _fileLog.Info("HeartBeat publicado correctamente.");
        _logger.LogInformation("HeartBeat publicado. Exchange={Exchange} RoutingKey={RoutingKey}", exchange, routingKey);
    }
}
