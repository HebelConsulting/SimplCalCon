using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SimplCalCon.Client;
using SimplCalCon.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient for /api with the access token attached (OIDC, ADR 0005/0010).
builder.Services.AddHttpClient("SimplCalCon.Api", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("SimplCalCon.Api"));
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<LiveUpdates>();

builder.Services.AddOidcAuthentication(options =>
{
    // Must match the OpenIddict issuer exactly, including its trailing slash (BaseAddress ends
    // with '/'). Trimming it makes the SPA reject the callback's id_token / iss parameter.
    options.ProviderOptions.Authority = builder.HostEnvironment.BaseAddress;
    options.ProviderOptions.ClientId = "simplcalcon-spa";
    options.ProviderOptions.ResponseType = "code";
    options.ProviderOptions.DefaultScopes.Add("openid");
    options.ProviderOptions.DefaultScopes.Add("email");
    options.ProviderOptions.DefaultScopes.Add("profile");
    options.ProviderOptions.DefaultScopes.Add("simplcalcon.api");
});

await builder.Build().RunAsync();
