using Clary.Api.Directories;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Clary.Api;
using Clary.Api.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

public class CompanySessionTests
{
    [Fact] public void ProductionRejectsDevelopmentIdentity()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Identity:Mode"]="Oidc",["Identity:Development:Enabled"]="true",["Identity:Development:Subject"]="sensitive-subject-example" });
        var error=Assert.Throws<InvalidOperationException>(()=>builder.AddCompanyIdentity());
        Assert.Contains("authentication configuration",error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-subject-example",error.Message);
    }
    [Fact] public async Task BodyCannotSelectUnauthorizedCompany() => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        var response=await f.Send(HttpMethod.Post,"/api/session/company",new { companyId=Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.Equal(IdentityFixture.Company,session.GetProperty("activeCompany").GetProperty("id").GetGuid());
    });
    [Fact] public async Task RevokedMembershipRejectsExistingSession() => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        await f.Sql("UPDATE memberships SET active=false");
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/company")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,"/api/company",new { name="Changed",revision=1 })).StatusCode);
        Assert.Equal("Legacy company",await f.Scalar("SELECT name FROM companies"));
    });
    [Fact] public async Task UnsafeRequestsRequireAntiforgery() => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PutAsJsonAsync("/api/company",new { name="Changed",revision=1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PostAsJsonAsync("/api/inventory/probe",new {})).StatusCode);
        Assert.Equal(0,f.Mutations);
        Assert.Equal("Legacy company",await f.Scalar("SELECT name FROM companies"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,"/api/company",new { name="Changed",revision=1 })).StatusCode);
        Assert.Equal("Changed",await f.Scalar("SELECT name FROM companies"));
    });
    [Theory] [InlineData("Viewer",true)] [InlineData("Administrator",false)]
    public async Task LastAdministratorCannotBeRemoved(string role,bool active) => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        var result=await f.Send(HttpMethod.Put,"/api/company/memberships",new { subject="bootstrap",issuer="clary-development",role,active,revision=1 });
        Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
        Assert.Equal("Administrator",await f.Scalar("SELECT role FROM memberships"));
        Assert.Equal(1,await f.Scalar("SELECT revision FROM memberships"));
    });
    [Fact] public async Task ConcurrentChangesCannotRemoveSoleAdministrator() => await IdentityFixture.Run(async f=>
    {
        var repo=new CompanyRepository(f.Database);
        var results=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
        {
            try{await repo.SaveAsync(new(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null),new("bootstrap","clary-development","Viewer",true,null,1),true,default);return true;}
            catch(CompanyDenied){return false;}
        }));
        Assert.All(results,value=>Assert.False(value));
        Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM memberships WHERE role='Administrator' AND active"));
    });
    [Fact] public async Task AnonymousHeadersCannotAuthenticateOrMutate() => await IdentityFixture.Run(async f=>
    {
        f.Client.DefaultRequestHeaders.Add("X-User-Id",IdentityFixture.User.ToString());
        f.Client.DefaultRequestHeaders.Add("X-Company-Id",IdentityFixture.Company.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.PostAsJsonAsync("/api/inventory/probe",new {})).StatusCode);
        Assert.Equal(0,f.Mutations);
    });
    [Theory] [InlineData("Viewer","AssignedOnly",403,403)] [InlineData("Viewer","Company",200,403)] [InlineData("Editor","Company",200,200)]
    public async Task BusinessPoliciesAreEnforced(string role,string scope,int read,int write) => await IdentityFixture.Run(async f=>
    {
        await f.Sql($"UPDATE memberships SET role='{role}',visibility_scope='{scope}'");
        await f.Login();
        Assert.Equal(read,(int)(await f.Client.GetAsync("/api/documents")).StatusCode);
        Assert.Equal(write,(int)(await f.Send(HttpMethod.Post,"/api/inventory/probe",new {})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/company/memberships")).StatusCode);
    });
    [Fact] public async Task MembershipProvisioningAndCompanyRevisions() => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        var input=new { subject="new-user",issuer="https://issuer.example",role="Editor",active=true,revision=(int?)null };
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/company/memberships",input)).StatusCode);
        Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM users"));
        var target=new CompanyContext(IdentityFixture.Company,Guid.NewGuid(),CompanyRole.Editor,null,null);await FixtureCompany.AddActorAsync(f.Database,target);
        await f.Sql($"UPDATE users SET issuer='https://issuer.example',identity_subject='new-user' WHERE id='{target.UserId}'");
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,"/api/company/memberships",new {input.subject,input.issuer,role="Viewer",active=false,revision=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Put,"/api/company/memberships",new {input.subject,input.issuer,role="Administrator",active=true,revision=1})).StatusCode);
        var membershipBefore=await f.Scalar($"SELECT row_to_json(m)::text FROM memberships m WHERE user_id='{target.UserId}'");var auditBefore=await f.Scalar("SELECT count(*) FROM inventory_audit");
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,"/api/company/memberships",new {input.subject,input.issuer,role="Editor",active=true,revision=1})).StatusCode);
        Assert.Equal("Viewer",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{target.UserId}'"));
        Assert.Equal(false,await f.Scalar($"SELECT active FROM memberships WHERE user_id='{target.UserId}'"));
        Assert.Equal(2,await f.Scalar($"SELECT revision FROM memberships WHERE user_id='{target.UserId}'"));
        Assert.Equal(membershipBefore,await f.Scalar($"SELECT row_to_json(m)::text FROM memberships m WHERE user_id='{target.UserId}'"));
        Assert.Equal(auditBefore,await f.Scalar("SELECT count(*) FROM inventory_audit"));
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Put,"/api/company",new {name="Renamed",revision=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,"/api/company",new {name="Stale",revision=1})).StatusCode);
        Assert.Equal("Renamed",await f.Scalar("SELECT name FROM companies"));
    });
    [Fact] public async Task LogoutInvalidatesCookie() => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/logout",new {})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
    });
    [Fact] public async Task CookieIsSecureAndHttpOnly() => await IdentityFixture.Run(async f=>
    {
        var response=await f.Client.GetAsync("/api/session/login");
        var cookie=Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);
    });
    [Fact] public async Task CompanySelectionUsesMembershipAndReflectsCurrentRole() => await IdentityFixture.Run(async f=>
    {
        await f.Sql($"INSERT INTO companies(id,name) VALUES('20000000-0000-0000-0000-000000000001','Second'); UPDATE memberships SET company_id='20000000-0000-0000-0000-000000000001',role='Viewer' WHERE user_id='{IdentityFixture.User}'");
        await f.Login();
        Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/company",new {companyId="20000000-0000-0000-0000-000000000001"})).StatusCode);
        await f.Sql("UPDATE memberships SET visibility_scope='AssignedOnly',role='Viewer' WHERE company_id='20000000-0000-0000-0000-000000000001'");
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/documents")).StatusCode);
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");Assert.Equal("Viewer",session.GetProperty("role").GetString());
    });
    [Fact] public async Task MalformedUpdatesAndMissingMembershipDoNotMutate() => await IdentityFixture.Run(async f=>
    {
        await f.Login();
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Put,"/api/company",new {name="Missing revision"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,"/api/company/memberships",new {subject="bad",issuer="issuer",role="99",active=true})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Put,"/api/company/memberships",new {subject="missing",issuer="issuer",role="Viewer",active=true,revision=1})).StatusCode);
        Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM users"));
    });
    [Theory] [InlineData(false,true)] [InlineData(false,false)] [InlineData(true,true)]
    public async Task OidcCallbackValidatesTokensAndNeverCreatesMembership(bool invalidSignature,bool emailVerified)
    {
        using var rsa=RSA.Create(2048);using var wrong=RSA.Create(2048);
        var key=new RsaSecurityKey(rsa){KeyId="test-signing-key"};
        string? nonce=null;
        await IdentityFixture.Run(async f=>
        {
            f.SignInAs("clary-development","bootstrap",IdentityFixture.Company);
            var invitation=await InvitationTests.Invite(f,"google-recipient@example.test");var invitationToken=invitation.GetProperty("token").GetString();
            Assert.Equal(HttpStatusCode.OK,(await f.Send(HttpMethod.Post,"/api/session/logout",new{})).StatusCode);
            var challenge=await f.Client.GetAsync("/api/session/login");f.Capture(challenge);
            Assert.Equal(HttpStatusCode.Redirect,challenge.StatusCode);
            var query=QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);nonce=query["nonce"];
            Assert.Equal("code",query["response_type"].ToString());Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
            var callback=await f.Client.GetAsync("/signin-oidc?code=test-code&state="+Uri.EscapeDataString(query["state"].ToString()));f.Capture(callback);
            if(invalidSignature)
            {
                Assert.Equal(HttpStatusCode.Unauthorized,callback.StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);
                Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM users"));
            }
            else
            {
                Assert.Equal(HttpStatusCode.Redirect,callback.StatusCode);
                Assert.Contains(callback.Headers.GetValues("Set-Cookie"),value=>value.StartsWith("__Host-Clary.Session="));
                Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM users"));
                var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");
                Assert.Equal("OIDC User",session.GetProperty("user").GetProperty("displayName").GetString());
                Assert.Empty(session.GetProperty("memberships").EnumerateArray());
                Assert.Equal(JsonValueKind.Null,session.GetProperty("activeCompany").ValueKind);
                Assert.Equal(2L,await f.Scalar("SELECT count(*) FROM users"));
                Assert.Equal(1L,await f.Scalar("SELECT count(*) FROM memberships"));
                Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/documents")).StatusCode);
                Assert.Equal("google-recipient@example.test",session.GetProperty("user").GetProperty("email").GetString());
                Assert.False(session.GetProperty("isOperator").GetBoolean());
                var accepted=await f.Send(HttpMethod.Post,"/api/session/invitations/accept",new{token=invitationToken});
                Assert.Equal(emailVerified?HttpStatusCode.OK:HttpStatusCode.Forbidden,accepted.StatusCode);
                Assert.Equal(emailVerified?2L:1L,await f.Scalar("SELECT count(*) FROM memberships"));
                Assert.Equal(emailVerified?0L:1L,await f.Scalar("SELECT count(*) FROM account_invitations WHERE state='Pending'"));
                if(emailVerified){var acceptance=await accepted.Content.ReadFromJsonAsync<JsonElement>();Assert.True(acceptance.GetProperty("freshLoginRequired").GetBoolean());Assert.Equal("Editor",acceptance.GetProperty("account").GetProperty("role").GetString());Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client.GetAsync("/api/session")).StatusCode);}
            }
        },builder=>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Identity:Development:Enabled"]="false",["Identity:Oidc:Authority"]="https://accounts.google.com",["Identity:Oidc:ClientId"]="clary-test",["Identity:Oidc:ClientSecret"]="test-only-secret",["Identity:Oidc:CallbackOrigin"]="https://localhost",["Identity:FrontendOrigin"]="http://localhost:5174"});
            builder.Services.Configure<OpenIdConnectOptions>(IdentityEndpoints.OidcScheme,options=>
            {
                options.Configuration=new OpenIdConnectConfiguration{Issuer="https://accounts.google.com",AuthorizationEndpoint="https://accounts.google.com/authorize",TokenEndpoint="https://accounts.google.com/token"};
                options.Configuration.SigningKeys.Add(key);
                options.Backchannel=new HttpClient(new TokenHandler(()=>
                {
                    var signing=invalidSignature?new RsaSecurityKey(wrong){KeyId=key.KeyId}:key;
                    var jwt=new JwtSecurityToken("https://accounts.google.com","clary-test",[new Claim("sub","oidc-new"),new Claim(SessionPresence.ClaimType,Guid.NewGuid().ToString()),new Claim("name","OIDC User"),new Claim("email","google-recipient@example.test"),new Claim("email_verified",emailVerified?"true":"false",ClaimValueTypes.Boolean),new Claim("nonce",nonce!),new Claim("iat",DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),ClaimValueTypes.Integer64)],DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(signing,SecurityAlgorithms.RsaSha256));
                    return new JwtSecurityTokenHandler().WriteToken(jwt);
                }));
            });
        });
    }
    private sealed class TokenHandler(Func<string> token):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {id_token=token(),access_token="test-access-token",token_type="Bearer",expires_in=300})});
    }
}

internal sealed class IdentityFixture : IAsyncDisposable
{
    public static Guid Company=Guid.Parse("00000000-0000-0000-0000-000000000001"), User=Guid.Parse("10000000-0000-0000-0000-000000000001");
    public NpgsqlDataSource Database=null!;
    public HttpClient Client=null!;
    public int Mutations;
    private WebApplication app=null!;
    private readonly Dictionary<string,string> cookies=[];
    public static async Task Run(Func<IdentityFixture,Task> test,Action<WebApplicationBuilder>? configure=null)
    {
        var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("CLARY_TEST_CONNECTION required");
        var schema="identity_test_"+Guid.NewGuid().ToString("N");
        await using var admin=NpgsqlDataSource.Create(connection);
        await using(var command=admin.CreateCommand($"CREATE SCHEMA {schema}"))await command.ExecuteNonQueryAsync();
        try
        {
            await using var f=new IdentityFixture();
            f.Database=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
            await f.Sql(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql")));
            await DatabaseMigrations.ApplyAsync(f.Database,CancellationToken.None);
            await f.Sql($"INSERT INTO users(id,issuer,identity_subject,display_name) VALUES('{User}','clary-development','bootstrap','Bootstrap'); INSERT INTO memberships(company_id,user_id,role) VALUES('{Company}','{User}','Administrator')");
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Development"});
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Identity:Mode"]="Oidc",["Identity:Development:Enabled"]="true",["Identity:Development:Subject"]="bootstrap" });
            builder.Services.AddSingleton(f.Database);builder.Services.AddSingleton<AssetConfigurationRepository>();builder.Services.AddSingleton<InventoryRepository>();builder.Services.AddSingleton<AuditRepository>();builder.Services.AddSingleton<InventoryDepartments>();builder.Services.AddSingleton<InventoryDocumentation>();builder.Services.AddSingleton<CompanyFiles>();
            builder.Services.AddSingleton<ConversationRepository>();
            builder.Services.AddSingleton<EquipmentRepository>();builder.Services.AddSingleton<TicketRepository>();
            builder.Services.AddSingleton<Clary.Api.Directories.DirectoryRepository>();
            builder.Services.AddSingleton(RuntimeConfiguration.Create("Demo",null,null));
            builder.Services.AddSingleton(sp=>new ConversationStore((company,q,c,ct)=>Task.FromResult(new Answer("insufficient",[],[],[],"Test",null)),"Test",sp.GetRequiredService<ConversationRepository>()));
            builder.Services.AddSingleton<LibraryRepository>();builder.Services.AddSingleton<LibraryOperations>();builder.Services.AddSingleton(sp=>new KnowledgeRepository(sp.GetRequiredService<NpgsqlDataSource>(),.55));builder.Services.AddSingleton<UploadService>();builder.Services.AddSingleton<SampleLoader>();
            builder.Services.AddSingleton<IEmbeddingProvider>(new KnowledgeIsolationFixture.TestEmbedding());
            builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<IKnowledgeRepository>(sp=>sp.GetRequiredService<KnowledgeRepository>().ForCompany(CompanyPolicies.Context(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!)!));
            configure?.Invoke(builder);
            builder.AddCompanyIdentity();
            f.app=builder.Build();
            f.app.UseCompanyIdentity();
            f.app.MapIdentityEndpoints();f.app.MapAccountEndpoints();f.app.MapCompanyContentEndpoints();
            f.app.MapGet("/api/operator/probe",()=>Microsoft.AspNetCore.Http.Results.Ok()).WithMetadata(new OperatorPermission()).RequireAuthorization(CompanyPolicies.Operator);
            f.app.MapPost("/api/inventory/probe",()=>{f.Mutations++;return Microsoft.AspNetCore.Http.Results.Ok();}).WithMetadata(new CompanyPermission(CompanyCapability.Write));

            await f.app.StartAsync();
            f.Client=f.app.GetTestClient();f.Client.BaseAddress=new Uri("https://localhost");
            await test(f);
        }
        finally {await using var command=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await command.ExecuteNonQueryAsync();}
    }
    public void SignInAs(string issuer,string subject,Guid? company=null,params Claim[] extra)
    {
        var claims=new List<Claim>{new("iss",issuer),new("sub",subject)};if(company.HasValue)claims.Add(new("clary_company",company.Value.ToString()));claims.AddRange(extra);
        var principal=new ClaimsPrincipal(new ClaimsIdentity(claims,IdentityEndpoints.CookieScheme));
        var session=app.Services.GetRequiredService<SessionPresence>().CreateAsync(principal,DateTimeOffset.UtcNow.AddHours(1),default).GetAwaiter().GetResult();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(SessionPresence.ClaimType,session.ToString()));
        var options=app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityEndpoints.CookieScheme);
        var ticket=new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal,new Microsoft.AspNetCore.Authentication.AuthenticationProperties{ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1)},IdentityEndpoints.CookieScheme);
        cookies["__Host-Clary.Session"]=options.TicketDataFormat.Protect(ticket);Client.DefaultRequestHeaders.Remove("Cookie");Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(p=>$"{p.Key}={p.Value}")));
    }
    public DateTimeOffset? CookieExpiry()
    {var options=app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityEndpoints.CookieScheme);return options.TicketDataFormat.Unprotect(cookies["__Host-Clary.Session"])!.Properties.ExpiresUtc;}
    public void RemovePersistedSessionClaim()
    {
        var options=app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityEndpoints.CookieScheme);
        var ticket=options.TicketDataFormat.Unprotect(cookies["__Host-Clary.Session"])!;var identity=(ClaimsIdentity)ticket.Principal.Identity!;
        foreach(var claim in identity.FindAll(SessionPresence.ClaimType).ToArray())identity.RemoveClaim(claim);
        cookies["__Host-Clary.Session"]=options.TicketDataFormat.Protect(ticket);Client.DefaultRequestHeaders.Remove("Cookie");Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(p=>$"{p.Key}={p.Value}")));
    }
    public async Task Login(){var response=await Client.GetAsync("/api/session/login");Capture(response);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Equal("/",response.Headers.Location!.OriginalString);}
    public void Capture(HttpResponseMessage response)
    {
        if(response.Headers.TryGetValues("Set-Cookie",out var values))foreach(var value in values){var pair=value.Split(';')[0].Split('=',2);if(pair[1]=="")cookies.Remove(pair[0]);else cookies[pair[0]]=pair[1];}
        Client.DefaultRequestHeaders.Remove("Cookie");if(cookies.Count>0)Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(p=>$"{p.Key}={p.Value}")));
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method,string path,object body)
    {
        var tokenResponse=await Client.GetAsync("/api/session/antiforgery");Capture(tokenResponse);
        var token=await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());
        var response=await Client.SendAsync(request);Capture(response);return response;
    }
    public async Task Sql(string sql){await using var q=Database.CreateCommand(sql);await q.ExecuteNonQueryAsync();}
    public async Task<object?> Scalar(string sql){await using var q=Database.CreateCommand(sql);return await q.ExecuteScalarAsync();}
    public async ValueTask DisposeAsync(){Client?.Dispose();if(app is not null)await app.DisposeAsync();if(Database is not null)await Database.DisposeAsync();}
}




