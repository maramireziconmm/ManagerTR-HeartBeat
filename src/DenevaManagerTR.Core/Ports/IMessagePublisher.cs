namespace DenevaManagerTR.Core.Ports;

public interface IMessagePublisher
{
    Task PublishAsync(PublishedMessage message, CancellationToken cancellationToken = default);
}

public sealed record PublishedMessage(
    string Exchange,
    string RoutingKey,
    string Payload,
    IReadOnlyDictionary<string, object>? Headers = null
);
