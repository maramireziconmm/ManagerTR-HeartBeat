namespace DenevaManagerTR.Core.Rabbit;

public static class RabbitConfigKeys
{
    public const string ConnectionString = "RabbitMQ_ConnectionString";
    public const string Port = "RabbitMQ_Port";
    public const string User = "RabbitMQ_User";
    public const string Pass = "RabbitMQ_Pass";
    public const string Vhost = "RabbitMQ_Vhost";
    // Alias: algunos componentes usan 'VHost'
    public const string VHost = Vhost;
    public const string TimeoutMessage = "RabbitMQ_timeoutMessage";

    public const string TopicDominio = "TopicDominio";
    public const string TopicTipoDisp = "TopicTipoDisp";
    public const string TopicObjID = "TopicObjID";
    public const string TopicRol = "TopicRol";
    public const string TopicGeneral = "TopicGeneral";
    public const string TopicDefinidoPorUsuario = "TopicDefinidoPorUsuario";
    public const string TopicAplicacion = "TopicAplicacion";

    public const string HeartbeatExchange = "RabbitMQ_HeartBeatExchangeName";
    public const string InfoEstacionExchange = "RabbitMQ_InfoEstacionExchangeName";
    public const string NotificacionesExchange = "RabbitMQ_NotificacionesExchangeName";
}
