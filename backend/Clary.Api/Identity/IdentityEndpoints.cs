using Clary.Api.LocalIdentity;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Clary.Api.Identity;

public static class CompanyPolicies
{
    public const string Operator="Operator", Delete="CompanyDelete", Approve="CompanyApprove", Index="CompanyIndex", Repair="CompanyRepair", Settings="CompanySettings", AccountAdmin="CompanyAccountAdmin";
    public const string Admin="CompanyAdmin", Read="CompanyRead", Write="CompanyWrite", Knowledge="CompanyKnowledge", OwnInventory="CompanyOwnInventory";
    public static CompanyContext? Context(HttpContext http)=>http.Items[typeof(CompanyContext)] as CompanyContext;
}

public static class IdentityEndpoints
{
    public const string CookieScheme="ClarySession", OidcScheme="ClaryOidc";
    private const string CompanyClaim="clary_company";
    public static void AddCompanyIdentity(this WebApplicationBuilder builder)
    {
        var config=builder.Configuration;
        var local=LocalIdentityEndpoints.IsLocal(config);
        if(config["Identity:Mode"] is not (null or "Local" or "Oidc"))throw new InvalidOperationException("Invalid authentication configuration mode.");
        builder.Services.AddLocalIdentity();
        var development=config.GetValue<bool>("Identity:Development:Enabled");
        var validDevelopment=builder.Environment.IsDevelopment()&&development&&!string.IsNullOrWhiteSpace(config["Identity:Development:Subject"]);
        var authority=config["Identity:Oidc:Authority"];
        var clientId=config["Identity:Oidc:ClientId"];
        var secret=config["Identity:Oidc:ClientSecret"];
        var validOidc=Uri.TryCreate(authority,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!string.IsNullOrWhiteSpace(clientId)&&!string.IsNullOrWhiteSpace(secret);
        // --migrate is an explicit offline schema operation and does not start a listener.
        if(!local&&((development&&!validDevelopment)||(!validDevelopment&&!validOidc)))
            throw new InvalidOperationException("Missing or invalid authentication configuration. Configure OIDC, or explicitly enable a bootstrap identity in Development.");
        builder.Services.AddSingleton<OperatorAccess>();builder.Services.AddSingleton<CompanyAccess>();builder.Services.AddSingleton<CompanyRepository>();builder.Services.AddAccountServices();
        var auth=builder.Services.AddAuthentication(CookieScheme).AddCookie(CookieScheme,options=>
        {
            options.Cookie.Name="__Host-Clary.Session";options.Cookie.HttpOnly=true;options.Cookie.SecurePolicy=CookieSecurePolicy.Always;
            options.Cookie.SameSite=SameSiteMode.Lax;options.Cookie.Path="/";
            options.ExpireTimeSpan=TimeSpan.FromHours(8);options.SlidingExpiration=false;
            options.Events.OnSigningIn=async context=>
            {
                var identity=(ClaimsIdentity)context.Principal!.Identity!;
                var sessions=context.HttpContext.RequestServices.GetRequiredService<SessionPresence>();
                if(SessionPresence.Id(context.Principal) is not null)
                {if(local){var expiry=await sessions.ValidExpiryAsync(context.Principal,context.HttpContext.RequestAborted);if(expiry is null)throw new CompanyDenied();context.Properties.ExpiresUtc=expiry;context.Properties.IsPersistent=false;context.Properties.AllowRefresh=false;}else if(!await sessions.ValidAsync(context.Principal,context.HttpContext.RequestAborted))throw new CompanyDenied();return;}
                foreach(var claim in identity.FindAll(SessionPresence.ClaimType).ToArray())identity.RemoveClaim(claim);
                context.Properties.ExpiresUtc??=DateTimeOffset.UtcNow.AddHours(8);
                var id=await sessions.CreateAsync(context.Principal,context.Properties.ExpiresUtc.Value,context.HttpContext.RequestAborted);
                identity.AddClaim(new Claim(SessionPresence.ClaimType,id.ToString()));
            };
            options.Events.OnValidatePrincipal=async context=>
            {if(!await context.HttpContext.RequestServices.GetRequiredService<SessionPresence>().ValidAsync(context.Principal!,context.HttpContext.RequestAborted))context.RejectPrincipal();};
            options.Events.OnRedirectToLogin=context=>{context.Response.StatusCode=401;return Task.CompletedTask;};
            options.Events.OnRedirectToAccessDenied=context=>{context.Response.StatusCode=403;return Task.CompletedTask;};
        });
        if(!local&&validOidc)auth.AddOpenIdConnect(OidcScheme,options=>
        {
            options.SignInScheme=CookieScheme;options.Authority=authority;options.ClientId=clientId;options.ClientSecret=secret;
            options.ResponseType=OpenIdConnectResponseType.Code;options.UsePkce=true;options.RequireHttpsMetadata=true;
            options.MapInboundClaims=false;options.SaveTokens=false;
            options.Scope.Add("email");
            // OIDC's default claim actions delete iss after token validation; retain the validated issuer in the session.
            options.ClaimActions.Remove("iss");
            options.TokenValidationParameters.ValidateIssuer=true;options.TokenValidationParameters.ValidateAudience=true;
            options.TokenValidationParameters.ValidateIssuerSigningKey=true;
            options.TokenValidationParameters.ValidateLifetime=true;options.TokenValidationParameters.RequireSignedTokens=true;
            options.TokenValidationParameters.NameClaimType="name";
            options.Events.OnRedirectToIdentityProvider=context=>
            {
                var callback=TrustedOrigin(config["Identity:Oidc:CallbackOrigin"],false);
                if(callback is null){context.HandleResponse();context.Response.StatusCode=503;return Task.CompletedTask;}
                context.ProtocolMessage.RedirectUri=callback+"/signin-oidc";return Task.CompletedTask;
            };
            options.Events.OnTokenValidated=async context=>
            {
                var principal=context.Principal!;
                // Session IDs belong to this server, never to provider token claims.
                foreach(var identity in principal.Identities)
                    foreach(var claim in identity.FindAll(SessionPresence.ClaimType).ToArray())identity.RemoveClaim(claim);
                // These claims come only from the validated token; never map client-provided headers.
                if(string.IsNullOrWhiteSpace(principal.FindFirstValue("iss"))||string.IsNullOrWhiteSpace(principal.FindFirstValue("sub")))
                {context.Fail("Identity claims missing.");return;}
                var repo=context.HttpContext.RequestServices.GetRequiredService<CompanyRepository>();
                await repo.PersistIdentityAsync(principal,context.HttpContext.RequestAborted);
                if(await repo.UserAsync(principal,context.HttpContext.RequestAborted) is null){context.Fail("Identity unavailable.");return;}
                await SelectInitialCompany(principal,repo,context.HttpContext.RequestAborted);
            };
            options.Events.OnRemoteFailure=context=>{context.HandleResponse();context.Response.StatusCode=401;return Task.CompletedTask;};
        });
        builder.Services.AddAntiforgery(options=>
        {
            options.HeaderName="X-CSRF-TOKEN";options.Cookie.Name="__Host-Clary.Antiforgery";
            options.Cookie.HttpOnly=true;options.Cookie.SecurePolicy=CookieSecurePolicy.Always;options.Cookie.SameSite=SameSiteMode.Strict;options.Cookie.Path="/";
        });
        builder.Services.AddAuthorization(options=>
        {
            options.AddPolicy(CompanyPolicies.Operator,p=>p.RequireAuthenticatedUser().RequireAssertion(c=>c.Resource is HttpContext h&&h.Items[typeof(OperatorContext)] is OperatorContext));
            Policy(CompanyPolicies.Admin,CompanyCapability.Settings);
            Policy(CompanyPolicies.Read,CompanyCapability.Read);
            Policy(CompanyPolicies.OwnInventory,CompanyCapability.OwnInventory);
            Policy(CompanyPolicies.Write,CompanyCapability.Write);
            Policy(CompanyPolicies.Knowledge,CompanyCapability.Read);
            Policy(CompanyPolicies.Delete,CompanyCapability.Delete);
            Policy(CompanyPolicies.Approve,CompanyCapability.Approve);
            Policy(CompanyPolicies.Index,CompanyCapability.Index);
            Policy(CompanyPolicies.Repair,CompanyCapability.Repair);
            Policy(CompanyPolicies.Settings,CompanyCapability.Settings);
            Policy(CompanyPolicies.AccountAdmin,CompanyCapability.AccountAdmin);
            void Policy(string name,CompanyCapability capability)=>options.AddPolicy(name,p=>p.RequireAuthenticatedUser().RequireAssertion(c=>c.Resource is HttpContext h&&AccountPermissions.Allows(CompanyPolicies.Context(h),capability)));
        });
    }

    public static void UseCompanyIdentity(this WebApplication app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.Use(async(http,next)=>
        {
            var path=http.Request.Path;
            if(!path.StartsWithSegments("/api")||path=="/api/health"){await next(http);return;}
            if(path=="/api/session/login"&&!LocalIdentityEndpoints.IsLocal(app.Configuration)){await next(http);return;}
            if(LocalIdentityEndpoints.IsLocal(app.Configuration)&&LocalIdentityEndpoints.PublicPaths.Contains(path.Value!))
            {
                http.Response.Headers.CacheControl="no-store";
                if(HttpMethods.IsPost(http.Request.Method))
                {
                    try{await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);}
                    catch(AntiforgeryValidationException){http.Response.StatusCode=400;await http.Response.WriteAsJsonAsync(new{error="A valid antiforgery token is required."});return;}
                    if(!http.RequestServices.GetRequiredService<LocalRequestThrottle>().Allow(http.Connection.RemoteIpAddress)){http.Response.StatusCode=429;await http.Response.WriteAsJsonAsync(new{error="Too many requests. Try again later."});return;}
                }
                await next(http);return;
            }
            http.Response.Headers.CacheControl="no-store";
            if(http.User.Identity?.IsAuthenticated!=true){http.Response.StatusCode=401;return;}
            var repo=http.RequestServices.GetRequiredService<CompanyRepository>();
            if(await repo.UserAsync(http.User,http.RequestAborted) is null){http.Response.StatusCode=401;return;}
            var operatorContext=await http.RequestServices.GetRequiredService<OperatorAccess>().ResolveAsync(http.User,http.RequestAborted);
            if(operatorContext is not null)http.Items[typeof(OperatorContext)]=operatorContext=operatorContext with{SessionId=SessionPresence.Id(http.User)};
            var access=http.RequestServices.GetRequiredService<CompanyAccess>();
            if(Guid.TryParse(http.User.FindFirstValue(CompanyClaim),out var companyId))
            {
                var context=await access.ResolveAsync(http.User,companyId,http.RequestAborted);
                if(context is not null)http.Items[typeof(CompanyContext)]=context with{SessionId=SessionPresence.Id(http.User)};
            }
            if(path!="/api/session"&&path!="/api/session/antiforgery"&&path!="/api/session/company"&&path!="/api/session/logout"&&path!="/api/session/invitations/accept"&&path!="/api/session/heartbeat"&&path!="/api/auth/password")
            {
                if(http.GetEndpoint()?.Metadata.GetMetadata<OperatorPermission>() is not null)
                {if(operatorContext is null){http.Response.StatusCode=403;return;}}
                else
                {
                if(CompanyPolicies.Context(http) is null){http.Response.StatusCode=403;return;}
                var permission=http.GetEndpoint()?.Metadata.GetMetadata<CompanyPermission>();
                if(permission is null||!AccountPermissions.Allows(CompanyPolicies.Context(http),permission.Capability)){http.Response.StatusCode=403;return;}
                if(permission.Action)
                {
                    http.Request.EnableBuffering();
                    try
                    {
                        using var body=await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body,cancellationToken:http.RequestAborted);
                        var action=body.RootElement.Deserialize<PermissionAction>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                        if(action?.Operation=="delete"&&!AccountPermissions.Allows(CompanyPolicies.Context(http),CompanyCapability.Delete)){http.Response.StatusCode=403;return;}
                    }
                    catch(System.Text.Json.JsonException){http.Response.StatusCode=400;return;}
                    finally{http.Request.Body.Position=0;}
                }
                }
            }
            if(!HttpMethods.IsGet(http.Request.Method)&&!HttpMethods.IsHead(http.Request.Method)&&!HttpMethods.IsOptions(http.Request.Method))
            {
                try{await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);}
                catch(AntiforgeryValidationException){http.Response.StatusCode=400;await http.Response.WriteAsJsonAsync(new {error="A valid antiforgery token is required."});return;}
            }
            try{await next(http);}catch(CompanyDenied){http.Response.StatusCode=403;}
        });
        app.UseAuthorization();
    }

    public static void MapIdentityEndpoints(this WebApplication app)
    {
        app.MapLocalIdentity();
        app.MapGet("/api/session/login",async(HttpContext http,CompanyRepository repo,IConfiguration config,IWebHostEnvironment environment)=>
        {
            if(LocalIdentityEndpoints.IsLocal(config))return Results.NotFound();
            if(environment.IsDevelopment()&&config.GetValue<bool>("Identity:Development:Enabled"))
            {
                var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("iss","clary-development"),new Claim("sub",config["Identity:Development:Subject"]!),new Claim("name",config["Identity:Development:DisplayName"]??"Development user")},CookieScheme));
                await repo.PersistIdentityAsync(principal,http.RequestAborted);
                if(await repo.UserAsync(principal,http.RequestAborted) is null)return Results.Unauthorized();
                await SelectInitialCompany(principal,repo,http.RequestAborted);
                await http.SignInAsync(CookieScheme,principal);return Results.Redirect((TrustedOrigin(config["Identity:FrontendOrigin"],true)??"")+"/");
            }
            var frontend=TrustedOrigin(config["Identity:FrontendOrigin"],true);
            if(frontend is null||TrustedOrigin(config["Identity:Oidc:CallbackOrigin"],false) is null)return Results.StatusCode(503);
            return Results.Challenge(new AuthenticationProperties { RedirectUri=frontend+"/" },[OidcScheme]);
        });
        app.MapGet("/api/session/antiforgery",(HttpContext http,IAntiforgery antiforgery)=>Results.Ok(new {token=antiforgery.GetAndStoreTokens(http).RequestToken}));
        app.MapGet("/api/session",async(HttpContext http,CompanyRepository repo,OperatorAccess operators)=>
        {
            var user=await repo.UserAsync(http.User,http.RequestAborted);if(user is null)return Results.Unauthorized();
            var memberships=await repo.MembershipsAsync(user.Id,http.RequestAborted);
            var context=CompanyPolicies.Context(http);
            var selected=memberships.FirstOrDefault(m=>m.CompanyId==context?.CompanyId);
            var company=context is null?null:await repo.GetAsync(context,http.RequestAborted);
            var isOperator=await operators.ResolveAsync(http.User,http.RequestAborted) is not null;
            LocalProfile? localProfile=null;
            if(LocalIdentityEndpoints.IsLocal(app.Configuration)){await using var c=await http.RequestServices.GetRequiredService<Npgsql.NpgsqlDataSource>().OpenConnectionAsync(http.RequestAborted);await using var tx=await c.BeginTransactionAsync(http.RequestAborted);localProfile=await http.RequestServices.GetRequiredService<LocalCredentialRepository>().ProfileAsync(c,tx,user.Id,http.RequestAborted);}
            return Results.Ok(new {authenticationMode=LocalIdentityEndpoints.IsLocal(app.Configuration)?"Local":"Oidc",localProfile,user,activeCompany=selected is null||company is null?null:new CompanyDto(selected.CompanyId,selected.Name,company.Revision),memberships,role=company is null?null:context?.Role.ToString(),isOperator,capabilities=AccountPermissions.Capabilities(selected is null||company is null?null:context)});
        });
        app.MapPost("/api/session/company",async(SelectCompany input,HttpContext http,CompanyAccess access)=>
        {
            var context=await access.ResolveAsync(http.User,input.CompanyId,http.RequestAborted);if(context is null)return Results.Forbid();
            var principal=new ClaimsPrincipal(new ClaimsIdentity(http.User.Claims.Where(c=>c.Type!=CompanyClaim),CookieScheme));
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(CompanyClaim,input.CompanyId.ToString()));
            await http.SignInAsync(CookieScheme,principal,(await http.AuthenticateAsync(CookieScheme)).Properties);return Results.Ok(new {companyId=input.CompanyId,role=context.Role.ToString()});
        });
        app.MapPost("/api/session/heartbeat",async(HttpContext http,SessionPresence sessions)=>{var expiry=await sessions.HeartbeatAsync(http.User,CompanyPolicies.Context(http),http.Items[typeof(OperatorContext)] as OperatorContext,http.RequestAborted,LocalIdentityEndpoints.IsLocal(app.Configuration));if(expiry is not null){var ticket=await http.AuthenticateAsync(CookieScheme);if(!ticket.Succeeded)throw new CompanyDenied();ticket.Properties!.ExpiresUtc=expiry;ticket.Properties.IssuedUtc=DateTimeOffset.UtcNow;ticket.Properties.IsPersistent=false;ticket.Properties.AllowRefresh=false;await http.SignInAsync(CookieScheme,http.User,ticket.Properties);}return Results.Ok();}).RequireAuthorization();
        app.MapPost("/api/session/logout",async(HttpContext http,SessionPresence sessions)=>{await sessions.RevokeAsync(http.User,http.RequestAborted);await http.SignOutAsync(CookieScheme);return Results.Ok();});
        app.MapGet("/api/company",async(HttpContext http,CompanyRepository repo)=>await repo.GetAsync(CompanyPolicies.Context(http)!,http.RequestAborted) is {} company?Results.Ok(company):Results.NotFound()).WithMetadata(new CompanyPermission(CompanyCapability.Read));
        app.MapPut("/api/company",(CompanyUpdate input,HttpContext http,CompanyRepository repo)=>Run(()=>repo.UpdateAsync(CompanyPolicies.Context(http)!,input,http.RequestAborted))).WithMetadata(new CompanyPermission(CompanyCapability.Settings)).RequireAuthorization(CompanyPolicies.Admin);
        app.MapGet("/api/company/memberships",async(HttpContext http,CompanyRepository repo)=>Results.Ok(await repo.ListAsync(CompanyPolicies.Context(http)!,http.RequestAborted))).WithMetadata(new CompanyPermission(CompanyCapability.AccountAdmin)).RequireAuthorization(CompanyPolicies.Admin);
        app.MapPost("/api/company/memberships",(MembershipInput input,HttpContext http,CompanyRepository repo)=>Run(()=>repo.SaveAsync(CompanyPolicies.Context(http)!,input,false,http.RequestAborted))).WithMetadata(new CompanyPermission(CompanyCapability.AccountAdmin)).RequireAuthorization(CompanyPolicies.Admin);
        app.MapPut("/api/company/memberships",(MembershipInput input,HttpContext http,CompanyRepository repo)=>AccountEndpoints.Run(()=>repo.SaveAsync(CompanyPolicies.Context(http)!,input,true,http.RequestAborted))).WithMetadata(new CompanyPermission(CompanyCapability.AccountAdmin)).RequireAuthorization(CompanyPolicies.Admin);
    }
    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try{return Results.Ok(await action());}
        catch(ArgumentException e){return Results.BadRequest(new {error=e.Message});}
        catch(CompanyConflict){return Results.Conflict(new {error="Revision conflict or last active administrator."});}
        catch(CompanyMissing){return Results.NotFound();}
        catch(CompanyDenied){return Results.Forbid();}
    }
    private static async Task SelectInitialCompany(ClaimsPrincipal principal,CompanyRepository repo,CancellationToken ct)
    {
        var identity=(ClaimsIdentity)principal.Identity!;
        foreach(var claim in identity.FindAll(CompanyClaim).ToArray())identity.RemoveClaim(claim);
        var user=await repo.UserAsync(principal,ct);
        if(user is not null&&(await repo.MembershipsAsync(user.Id,ct)).FirstOrDefault() is {} membership)
            identity.AddClaim(new Claim(CompanyClaim,membership.CompanyId.ToString()));
    }
    // Configuration is the only redirect authority. Request Host, forwarded headers and query returns are ignored.
    private static string? TrustedOrigin(string? value,bool allowLoopbackHttp)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var origin)||!string.IsNullOrEmpty(origin.UserInfo)||origin.AbsolutePath!="/"||!string.IsNullOrEmpty(origin.Query)||!string.IsNullOrEmpty(origin.Fragment))return null;
        if(origin.Scheme!="https"&&!(allowLoopbackHttp&&origin.Scheme=="http"&&origin.IsLoopback))return null;
        return origin.GetLeftPart(UriPartial.Authority);
    }
    private record PermissionAction(string? Operation);
    public record SelectCompany(Guid CompanyId);
}

