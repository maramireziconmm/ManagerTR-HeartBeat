namespace DenevaManagerTR.Application.Options;

public sealed class JobsOptions
{
    public HeartbeatJobOptions Heartbeat { get; set; } = new();

    // v0.3.7: receptor de peticiones InfoStation (GETINFOSTATION)
    public InfoStationOptions InfoStation { get; set; } = new();

    public InfoLineasEstados InfoLineasEstados { get; set; } = new();
}

public sealed class HeartbeatJobOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public string OriginSystem { get; set; } = "DenevaManagerTR";
    public string AppId { get; set; } = "DenevaManagerTR";

    public string Msg_OriginSystem_HeartBeat { get; set; } = "Servidor_60";
    public string Msg_topic_HeartBeat { get; set; } = "PorObjectIDPadre.60";
    public string Msg_contentType_HeartBeat { get; set; } = "HeartBeat";

}

public sealed class InfoStationOptions
{
    public bool Enabled { get; set; } = true;

    // Nombre de la cola. En legacy se usa una cola fija en el subscriber.
    public string QueueName { get; set; } = "InfoDenevaManagerTR";

    // Lista de topics/bindings. Si está vacía, se usará AppId como único binding.
    public List<string> Topics { get; set; } = new();

    // Identificador de aplicación (se usa para el nombre de conexión y bindings por defecto)
    public string AppId { get; set; } = "DenevaManagerTR";

    // Header y publicación de respuesta
    public string ResponseOriginSystem { get; set; } = "ManagerTR";
    public string ResponseContentType { get; set; } = "SYNCINFOSTATION_RETURN";

    // Timeout/expiration de la respuesta (ms) - si vacío no se setea
    public string ResponseTimeoutMs { get; set; } = "10000";
}

public sealed class InfoLineasEstados
{
    public bool Enabled { get; set; } = true;

    // Nombre de la cola. En legacy se usa una cola fija en el subscriber.
    public string QueueName { get; set; } = "InfoLineasEstado";

    // Lista de topics/bindings. Si está vacía, se usará AppId como único binding.
    public List<string> Topics { get; set; } = new();

    // Identificador de aplicación (se usa para el nombre de conexión y bindings por defecto)
    public string AppId { get; set; } = "DenevaManagerTR";

    // Header y publicación de respuesta
    public string ResponseOriginSystem { get; set; } = "ManagerTR";
    public string ResponseContentType { get; set; } = "SYNCINFOLINEASESTADOSTM_RETURN";

    // Timeout/expiration de la respuesta (ms) - si vacío no se setea
    public string ResponseTimeoutMs { get; set; } = "10000";
}
