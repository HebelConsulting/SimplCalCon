using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.Infrastructure.Time;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
