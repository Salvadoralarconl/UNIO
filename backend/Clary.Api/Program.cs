using Clary.Api;
using System.Net.Http.Headers;
using Npgsql;
using Clary.Api.Identity;
using Clary.Api.Directories;

var builder = WebApplication.CreateBuilder(args);
if(args.Contains("--recover-local-operator"))
{
    if(args.Length!=1)throw new InvalidOperationException("Local Operator recovery accepts no extra arguments. Configure the connection and private output file through the process environment.");
    await using var database=NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Knowledge")??throw new InvalidOperationException("Database connection is required."));
    await new Clary.Api.LocalIdentity.LocalOperatorBootstrap(database,builder.Configuration["Identity:FrontendOrigin"]??"http://localhost:5174").RecoverAsync(Environment.GetEnvironmentVariable("BRIE_OPERATOR_OUTPUT_FILE")??throw new InvalidOperationException("Private output file is required."));
    Console.WriteLine("Local Operator recovery issued. Open the private file to complete recovery.");
    return;
}
if(args.Contains("--initialize-local-operator"))
{
    if(args.Length!=1)throw new InvalidOperationException("Local Operator initialization accepts no extra arguments. Configure the connection and private output file through the process environment.");
    await using var database=NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Knowledge")??throw new InvalidOperationException("Database connection is required."));
    await new Clary.Api.LocalIdentity.LocalOperatorBootstrap(database,builder.Configuration["Identity:FrontendOrigin"]??"http://localhost:5174").InitializeAsync(Environment.GetEnvironmentVariable("BRIE_OPERATOR_OUTPUT_FILE")??throw new InvalidOperationException("Private output file is required."));
    Console.WriteLine("Local Operator initialized. Open the private setup file to complete account setup.");
    return;
}
if(args.Contains("--initialize"))throw new InvalidOperationException("Legacy --initialize is disabled. Use --fresh-bootstrap only for an empty database, or --migrate for existing data.");
if(args.Contains("--fresh-bootstrap") || args.Contains("--migrate"))
{
    if(args.Contains("--fresh-bootstrap") && args.Contains("--migrate"))throw new InvalidOperationException("Choose exactly one database operation.");
    await using var database=NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Knowledge")??throw new InvalidOperationException("Database connection is required."));
    if(args.Contains("--fresh-bootstrap"))await DatabaseStartup.FreshBootstrapAsync(database,CancellationToken.None);
    else await DatabaseMigrations.ApplyAsync(database,CancellationToken.None);
    return;
}
if(args.Contains("--initialize-operator"))
{
    var connection=builder.Configuration.GetConnectionString("Knowledge")??throw new InvalidOperationException("Database connection is required.");
    await using var database=NpgsqlDataSource.Create(connection);
    await new Clary.Api.Accounts.OperatorBootstrap(database).InitializeAsync(builder.Configuration["Operator:Issuer"]??"",builder.Configuration["Operator:Subject"]??"");
    Console.WriteLine("Operator binding initialized.");
    return;
}
builder.AddPersistentKeys();
if(!args.Contains("--migrate"))builder.AddCompanyIdentity();
if(builder.Environment.IsDevelopment())
{
    var urls=(builder.Configuration["urls"]??"https://localhost:7043").Split(';',StringSplitOptions.RemoveEmptyEntries);
    if(urls.Length==0||urls.Any(url=>!Uri.TryCreate(url,UriKind.Absolute,out var address)||!address.IsLoopback))
        throw new InvalidOperationException("Development listeners must bind to loopback.");
    if(builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any(endpoint=>
        !Uri.TryCreate(endpoint["Url"],UriKind.Absolute,out var address)||!address.IsLoopback))
        throw new InvalidOperationException("Development listeners must bind to loopback.");
    builder.WebHost.UseUrls(urls);
}
builder.Services.AddSingleton<InventoryRepository>();builder.Services.AddSingleton<AuditRepository>();
builder.Services.AddSingleton<EquipmentRepository>();builder.Services.AddSingleton<TicketRepository>();
builder.Services.AddSingleton<AssetConfigurationRepository>();
builder.Services.AddSingleton<InventoryDepartments>();
builder.Services.AddSingleton<DirectoryRepository>();
builder.Services.AddSingleton<InventoryDocumentation>();
var runtime = RuntimeConfiguration.Create(
    builder.Configuration["AI_PROVIDER"] ?? "OpenAI",
    builder.Configuration["OPENAI_API_KEY"],builder.Configuration["OLLAMA_BASE_URL"]);
builder.Services.AddSingleton(runtime);
var profile = runtime.Provider=="Ollama"
    ? new EmbeddingProfile("Ollama",builder.Configuration["OLLAMA_EMBEDDING_MODEL"]??"embeddinggemma",
        int.TryParse(builder.Configuration["OLLAMA_EMBEDDING_DIMENSIONS"],out var localDimensions)?localDimensions:768,"chars-1200-160-v1")
    : new EmbeddingProfile("OpenAI",builder.Configuration["EMBEDDING_MODEL"]??"text-embedding-3-small",
        int.TryParse(builder.Configuration["EMBEDDING_DIMENSIONS"],out var dimensions)?dimensions:1536,"chars-1200-160-v1");
builder.Services.AddSingleton(NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Knowledge")??"Host=127.0.0.1;Database=clary;Username=clary;Password=unset"));
builder.Services.AddHttpClient("OpenAI",client=> {
    client.BaseAddress=new Uri("https://api.openai.com/v1/");client.Timeout=TimeSpan.FromSeconds(60);
    var key=builder.Configuration["OPENAI_API_KEY"];
    if(!string.IsNullOrWhiteSpace(key))client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",key);
});
builder.Services.AddSingleton<DeterministicProvider>();
builder.Services.AddHttpClient("Ollama",client=> {
    var baseUrl=builder.Configuration["OLLAMA_BASE_URL"];
    client.BaseAddress=new Uri((string.IsNullOrWhiteSpace(baseUrl)?"http://127.0.0.1:11434":baseUrl).TrimEnd('/')+"/");
    client.Timeout=TimeSpan.FromMinutes(3);
});
builder.Services.AddSingleton<IEmbeddingProvider>(sp=>runtime.Provider switch {
    "Demo"=>sp.GetRequiredService<DeterministicProvider>(),
    "Ollama"=>new OllamaEmbeddingProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama"),profile),
    _=>new OpenAiEmbeddingProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI"),profile)
});
builder.Services.AddSingleton<IAnswerProvider>(sp=>runtime.Provider switch {
    "Demo"=>sp.GetRequiredService<DeterministicProvider>(),
    "Ollama"=>new OllamaAnswerProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama"),builder.Configuration["OLLAMA_GENERATION_MODEL"]??"qwen3:8b",sp.GetRequiredService<ILogger<OllamaAnswerProvider>>()),
    _=>new OpenAiAnswerProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI"),builder.Configuration["GENERATION_MODEL"]??"gpt-4.1-mini")
});
builder.Services.AddSingleton(sp=>new KnowledgeRepository(sp.GetRequiredService<NpgsqlDataSource>(),runtime.Provider=="Demo"?.55:
    double.TryParse(builder.Configuration["MIN_SIMILARITY"],System.Globalization.CultureInfo.InvariantCulture,out var threshold)?threshold:.45,
    builder.Configuration.GetValue<int?>("RETRIEVAL_MAX_DOCUMENTS")??3,
    builder.Configuration.GetValue<int?>("RETRIEVAL_PASSAGES_PER_DOCUMENT")??2,
    builder.Configuration.GetValue<double?>("RETRIEVAL_SCORE_WINDOW")??.2));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IKnowledgeRepository>(sp=>sp.GetRequiredService<KnowledgeRepository>().ForCompany(CompanyPolicies.Context(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!)??throw new InvalidOperationException("Company context required.")));
builder.Services.AddSingleton<SampleLoader>();
builder.Services.AddSingleton<UploadService>();
builder.Services.AddSingleton<CompanyFiles>();
builder.Services.AddSingleton<LibraryRepository>();
builder.Services.AddSingleton<LibraryOperations>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options=>options.MultipartBodyLengthLimit=DocumentExtractor.MaxBytes+64*1024);
builder.Services.AddScoped(sp=>new TroubleshootingService(sp.GetRequiredService<IEmbeddingProvider>(),sp.GetRequiredService<IAnswerProvider>(),sp.GetRequiredService<IKnowledgeRepository>(),runtime.Health().Origin,
    runtime.Provider=="Demo"?null:new ModelSupportVerifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient(runtime.Provider),runtime.Provider,
        runtime.Provider=="Ollama"?builder.Configuration["OLLAMA_GENERATION_MODEL"]??"qwen3:8b":builder.Configuration["GENERATION_MODEL"]??"gpt-4.1-mini"),
    runtime.Provider=="Demo"?null:sp.GetRequiredService<KnowledgeRepository>().ForCompany(CompanyPolicies.Context(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!)??throw new InvalidOperationException("Company context required."),true),documentationDriven:true));
var frontendOrigins=builder.Configuration.GetSection("Identity:FrontendOrigins").Get<string[]>()??[];
if(frontendOrigins.Any(origin=>!Uri.TryCreate(origin,UriKind.Absolute,out var address)||address.Scheme is not ("http" or "https")||address.GetLeftPart(UriPartial.Authority)!=origin))
    throw new InvalidOperationException("Configure explicit frontend origins without paths or wildcards.");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if(frontendOrigins.Length>0)policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddSingleton<ConversationRepository>();
builder.Services.AddSingleton(sp=>new ConversationStore((company,q,diagnostics,ct)=>new TroubleshootingService(
    sp.GetRequiredService<IEmbeddingProvider>(),sp.GetRequiredService<IAnswerProvider>(),sp.GetRequiredService<KnowledgeRepository>().ForCompany(company),runtime.Health().Origin,
    runtime.Provider=="Demo"?null:new ModelSupportVerifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient(runtime.Provider),runtime.Provider,
        runtime.Provider=="Ollama"?builder.Configuration["OLLAMA_GENERATION_MODEL"]??"qwen3:8b":builder.Configuration["GENERATION_MODEL"]??"gpt-4.1-mini"),
    runtime.Provider=="Demo"?null:sp.GetRequiredService<KnowledgeRepository>().ForCompany(company,true),documentationDriven:true).AskAsync(q,ct,diagnostics),runtime.Health().Origin,sp.GetRequiredService<ConversationRepository>(),documentationDriven:true));
builder.Services.AddSingleton<IBrieTicketParser>(sp=>new OllamaBrieTicketParser(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama"),builder.Configuration["OLLAMA_GENERATION_MODEL"]??"qwen3:8b"));
builder.Services.AddSingleton<IBrieIntentRouter>(sp=>new OllamaBrieIntentRouter(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama"),builder.Configuration["OLLAMA_GENERATION_MODEL"]??"qwen3:8b"));
builder.Services.AddSingleton<BrieTickets>();
var app = builder.Build();
app.Use(async(http,next)=>{try{await next(http);}catch(CompanyResourceMissing){http.Response.StatusCode=404;await http.Response.WriteAsJsonAsync(new{error="This resource no longer exists."});}});
app.UseCors();
if(!args.Contains("--migrate"))
{
    app.UseCompanyIdentity();
    app.MapIdentityEndpoints();
    app.MapAccountEndpoints();
}
app.MapGet("/api/health", (RuntimeConfiguration config) => config.Health());
app.MapHostedReadiness();
app.MapCompanyContentEndpoints();

app.Run();

public sealed record ChatProjectRequest(string Name);





