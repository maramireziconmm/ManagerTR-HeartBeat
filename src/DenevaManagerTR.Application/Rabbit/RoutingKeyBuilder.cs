namespace DenevaManagerTR.Application.Rabbit;

public sealed class RoutingKeyBuilder
{
    public string Build(params string?[] parts)
    {
        var clean = parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim().Trim('.'))
            .Where(p => p.Length > 0)
            .ToArray();

        return string.Join(".", clean);
    }
}
