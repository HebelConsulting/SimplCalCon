namespace SimplCalCon.Infrastructure.Configuration;

/// <summary>Registration data for the Blazor WASM public client (ADR 0005, 0010).</summary>
public sealed class SpaClientOptions
{
    public const string SectionName = "SimplCalCon:SpaClient";

    public const string ClientId = "simplcalcon-spa";

    /// <summary>Origin of the web client, used to derive OIDC redirect URIs.</summary>
    public string BaseUrl { get; set; } = "https://localhost:5001";

    public string LoginCallbackPath { get; set; } = "/authentication/login-callback";

    public string LogoutCallbackPath { get; set; } = "/authentication/logout-callback";
}
