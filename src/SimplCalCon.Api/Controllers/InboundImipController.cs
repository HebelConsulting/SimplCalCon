using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Infrastructure.Email;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Inbound iMIP ingestion (ADR 0056): an MTA pipe or inbound-email webhook POSTs the raw RFC822
/// message here. Machine-to-machine — authenticated by the shared secret in the <c>X-Inbound-Key</c>
/// header (config <c>SimplCalCon:InboundEmail:ApiKey</c>); the endpoint is disabled (404) when unset.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/inbound-imip")]
public sealed class InboundImipController(
    IInboundItipProcessor processor, IOptions<InboundEmailOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ingest(CancellationToken cancellationToken)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return NotFound(); // feature not configured
        }

        if (!Request.Headers.TryGetValue("X-Inbound-Key", out var provided) || !FixedTimeEquals(provided!, key))
        {
            return Unauthorized();
        }

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        var result = await processor.ProcessAsync(raw, cancellationToken);

        return result.Outcome == InboundItipOutcome.NoCalendarPart
            ? BadRequest(new { error = "no text/calendar part" })
            : Accepted(new { outcome = result.Outcome.ToString(), detail = result.Detail });
    }

    private static bool FixedTimeEquals(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
