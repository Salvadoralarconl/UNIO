using System.Net;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;
using Xunit;
public class LoginRedirectTests
{
 [Fact] public async Task ChallengeUsesTrustedCallbackAndFixedFrontendReturnIgnoringRequestOrigins()
 {
  await using var app=Create("https://localhost:5081","http://localhost:5174");await app.StartAsync();using var client=app.GetTestClient();client.BaseAddress=new Uri("https://untrusted.example");client.DefaultRequestHeaders.Add("X-Forwarded-Host","attacker.example");client.DefaultRequestHeaders.Add("X-Forwarded-Proto","http");var result=await client.GetAsync("/api/session/login?returnUrl=https://attacker.example&token=never-forward");Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);var query=QueryHelpers.ParseQuery(result.Headers.Location!.Query);Assert.Equal("https://localhost:5081/signin-oidc",query["redirect_uri"].ToString());Assert.False(string.IsNullOrEmpty(query["code_challenge"]));var requestedScopes=query["scope"].ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries);Assert.Contains("openid",requestedScopes);Assert.Contains("profile",requestedScopes);Assert.Contains("email",requestedScopes);var options=app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(IdentityEndpoints.OidcScheme);var properties=options.StateDataFormat.Unprotect(query["state"]!)!;Assert.Equal("http://localhost:5174/",properties.RedirectUri);Assert.DoesNotContain("never-forward",result.Headers.Location.ToString());
 }
 [Theory][InlineData(null,"http://localhost:5174")][InlineData("http://localhost:5081","http://localhost:5174")][InlineData("https://localhost:5081/path","http://localhost:5174")][InlineData("https://localhost:5081","https://user:secret@example.com")][InlineData("https://localhost:5081","http://example.com")]
 public async Task InvalidTrustedOriginsFailClosed(string? callback,string? frontend){await using var app=Create(callback,frontend);await app.StartAsync();using var client=app.GetTestClient();var result=await client.GetAsync("/api/session/login");Assert.Equal(HttpStatusCode.ServiceUnavailable,result.StatusCode);Assert.Null(result.Headers.Location);}
 private static WebApplication Create(string? callback,string? frontend){var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});builder.WebHost.UseTestServer();builder.Logging.ClearProviders();builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Identity:Mode"]="Oidc",["Identity:Development:Enabled"]="false",["Identity:Oidc:Authority"]="https://accounts.google.com",["Identity:Oidc:ClientId"]="isolated-client",["Identity:Oidc:ClientSecret"]="isolated-secret",["Identity:Oidc:CallbackOrigin"]=callback,["Identity:FrontendOrigin"]=frontend});builder.Services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=unused;Username=unused"));builder.AddCompanyIdentity();builder.Services.Configure<OpenIdConnectOptions>(IdentityEndpoints.OidcScheme,o=>o.Configuration=new OpenIdConnectConfiguration{Issuer="https://accounts.google.com",AuthorizationEndpoint="https://accounts.google.com/o/oauth2/v2/auth",TokenEndpoint="https://oauth2.googleapis.com/token"});var app=builder.Build();app.UseAuthentication();app.MapIdentityEndpoints();return app;}
}
