namespace DenevaManagerTR.Core.Options;

public sealed class DenevaConfigOptions
{
    public string Path { get; set; } = "deneva.config";
    public string RabbitSection { get; set; } = "RabbiMQ.My.MySettings";
}
