using System.Net;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Npgsql;
namespace Clary.Api.LocalIdentity;
// One bounded bucket per network peer, never a forwarded/header identity. Saturation fails closed.
public sealed class LocalRequestThrottle
{
 private readonly object gate=new();private readonly Dictionary<string,(long Start,int Count)> buckets=[];
 public bool Allow(IPAddress? peer)
 {
  var now=Environment.TickCount64;var key=peer?.MapToIPv6().ToString()??"unknown";
  lock(gate){foreach(var expired in buckets.Where(x=>now-x.Value.Start>=60000).Select(x=>x.Key).ToArray())buckets.Remove(expired);if(!buckets.TryGetValue(key,out var bucket)){if(buckets.Count>=4096)return false;bucket=(now,0);}bucket.Count++;buckets[key]=bucket;return bucket.Count<=30;}
 }
}
public static class LocalIdentityEndpoints
{
 public static bool IsLocal(IConfiguration config)=>(config["Identity:Mode"]??"Local")=="Local";
 public static readonly HashSet<string> PublicPaths=new(StringComparer.Ordinal){"/api/auth/config","/api/auth/antiforgery","/api/auth/login","/api/auth/setup","/api/auth/setup/details","/api/auth/invitations/complete"};
 public static void AddLocalIdentity(this IServiceCollection services)
 {services.AddSingleton<LocalPasswordService>();services.AddSingleton<LocalCredentialRepository>();services.AddSingleton<LocalCredentialTokens>();services.AddSingleton<LocalAuthenticationService>();services.AddSingleton<LocalRequestThrottle>();services.AddSingleton<LocalProvisioningService>();}
 public static void MapLocalIdentity(this WebApplication app)
 {
  if(!IsLocal(app.Configuration))return;
  app.MapGet("/api/auth/config",()=>Results.Ok(new{mode="Local",allowsRegistration=false}));
  app.MapGet("/api/auth/antiforgery",(HttpContext h,IAntiforgery csrf)=>Results.Ok(new{token=csrf.GetAndStoreTokens(h).RequestToken}));
  app.MapPost("/api/auth/login",async(LoginInput input,HttpContext h,LocalAuthenticationService auth)=>
  {
   var result=await auth.LoginAsync(input,h.RequestAborted);if(result is null)return Results.Json(new{error="Unable to sign in."},statusCode:401);
   try{await h.SignInAsync(IdentityEndpoints.CookieScheme,result.Principal,new AuthenticationProperties{ExpiresUtc=result.ExpiresAt,IsPersistent=false,AllowRefresh=false});}catch(CompanyDenied){return Results.Json(new{error="Unable to sign in."},statusCode:401);}return Results.Ok(new{requiresSessionReload=true});
  });
  app.MapPost("/api/auth/setup",(CredentialSetupInput input,HttpContext h,LocalAuthenticationService auth)=>Anonymous(async()=>{await auth.SetupAsync(input,h.RequestAborted);return new{freshLoginRequired=true};}));
  app.MapPost("/api/auth/invitations/complete",(CredentialSetupInput input,HttpContext h,LocalAuthenticationService auth)=>Anonymous(async()=>{await auth.CompleteInvitationAsync(input,h.RequestAborted);return new{freshLoginRequired=true};}));
  app.MapPost("/api/auth/setup/details",(TokenInput input,HttpContext h,LocalAuthenticationService auth)=>Anonymous(()=>auth.DetailsAsync(input.Token,h.RequestAborted)));
  app.MapPost("/api/auth/password",(PasswordChangeInput input,HttpContext h,LocalAuthenticationService auth)=>AccountEndpoints.Run(async()=>{await auth.ChangeAsync(h.User,input,h.RequestAborted);await h.SignOutAsync(IdentityEndpoints.CookieScheme);return new{freshLoginRequired=true};})).RequireAuthorization();
 }
 internal static async Task<IResult> Anonymous<T>(Func<Task<T>> work)
 {try{return Results.Ok(await work());}catch(Exception e) when(e is ArgumentException or CompanyDenied or CompanyMissing or CompanyConflict || e is PostgresException p&&p.SqlState==PostgresErrorCodes.UniqueViolation){return Results.BadRequest(new{error="Invalid or expired link, or unavailable account details."});}}
 public record TokenInput(string Token);
}
