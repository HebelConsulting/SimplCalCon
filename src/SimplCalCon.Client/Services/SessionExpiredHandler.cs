using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace SimplCalCon.Client.Services;

/// <summary>
/// Redirects to the interactive login when the session can no longer be renewed (ADR 0076).
///
/// The refresh-token grant keeps the session alive silently, but it eventually ends (the refresh token
/// expires after long idle, or the account is deactivated so the exchange returns invalid_grant). When
/// that happens the underlying auth handler throws <see cref="AccessTokenNotAvailableException"/>, and a
/// still-attached-but-expired token yields a <c>401</c>. Either way this handler bounces to login
/// (capturing the current page as the return URL) instead of leaving the SPA showing broken 401s until
/// the user manually reloads — the reported idle-expiry bug.
/// </summary>
public sealed class SessionExpiredHandler(NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                RedirectToLogin();
            }

            return response;
        }
        catch (AccessTokenNotAvailableException)
        {
            // Silent renewal failed outright (no usable access or refresh token).
            RedirectToLogin();
            throw;
        }
    }

    private void RedirectToLogin()
    {
        // Don't loop while already on the authentication pages, and coalesce concurrent 401s (the WASM
        // client is single-threaded, so the first navigation wins and later ones no-op here).
        if (navigation.Uri.Contains("authentication/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        navigation.NavigateToLogin("authentication/login");
    }
}
