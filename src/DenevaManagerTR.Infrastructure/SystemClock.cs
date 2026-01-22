using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
