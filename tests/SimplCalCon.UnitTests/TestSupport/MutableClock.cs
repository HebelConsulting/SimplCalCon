using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.UnitTests.TestSupport;

internal sealed class MutableClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}
