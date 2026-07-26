using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplCalCon.Infrastructure.Identity;

namespace SimplCalCon.UnitTests;

/// <summary>
/// Guards the OpenIddict token-prune background service (ADR 0079): it's ON by default but must fully
/// opt out when <c>TokenPruneDays = 0</c> — i.e. never even open a DI scope to reach the managers.
/// </summary>
public sealed class TokenPruneServiceTests
{
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int Created { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            throw new InvalidOperationException("A disabled prune service must not open a scope.");
        }
    }

    [Fact]
    public void Default_is_on_with_a_14_day_threshold()
    {
        var options = new TokenPruneOptions();
        Assert.Equal(14, options.TokenPruneDays);
        Assert.Equal(24, options.PruneHours);
    }

    [Fact]
    public async Task Disabled_when_TokenPruneDays_is_zero()
    {
        var factory = new ThrowingScopeFactory();
        var service = new TokenPruneService(
            factory, Options.Create(new TokenPruneOptions { TokenPruneDays = 0 }), NullLogger<TokenPruneService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.Created); // returned before touching the scope factory / managers
    }
}
