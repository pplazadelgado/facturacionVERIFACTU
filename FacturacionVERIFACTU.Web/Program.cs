using FacturacionVERIFACTU.Web.Components;
using FacturacionVERIFACTU.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using CurrieTechnologies.Razor.SweetAlert2;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// 1. SERVICIOS DE INTERFAZ (BLAZOR SERVER)
// ========================================
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ========================================
// 2. AUTENTICACIÓN Y ESTADO DE USUARIO
// ========================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    });

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// Servicios esenciales de Blazor para sesión y auth
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddScoped<ConfirmDialogService>();

builder.Services.AddScoped<IWebCacheService, WebCacheService>();


// ========================================
// 3. GESTIÓN DE TOKEN Y HTTP (SOLUCIÓN 401)
// ========================================

// Registramos el estado del token como Scoped (se comparte en toda la sesión del usuario)
builder.Services.AddScoped<TokenState>();

// El Handler debe ser Transient para que el Factory lo cree por cada cliente
builder.Services.AddScoped<TokenHandler>();

// A) Configuración del Cliente "VerifactuApi"
builder.Services.AddHttpClient("VerifactuApi", client =>
{
    // Asegúrate de que este puerto coincide con tu Backend
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5121/";
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<TokenHandler>() // Inyecta el token en cada petición
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Bypass de SSL para desarrollo
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

// B) Cliente para AuthService (LOGIN)
// IMPORTANTE: Este NO lleva TokenHandler porque el login no necesita token
builder.Services.AddHttpClient<IAuthService, AuthService>(client =>
{
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5121/";
    client.BaseAddress = new Uri(baseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

// C) REGISTRO CLAVE: Hace que @inject HttpClient use la configuración de "VerifactuApi"
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("VerifactuApi");
});

// Otros servicios de negocio
builder.Services.AddScoped<IApiService, ApiService>();

// ========================================
// 4. PIPELINE DE LA APLICACIÓN (MIDDLEWARE)
// ========================================

builder.Services.AddSweetAlert2();

var cultureInfo = new System.Globalization.CultureInfo("es-ES");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("es-ES");
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("es-ES");

// ============================================
// BLOQUE 29: COMPRESIÓN + CACHÉ WEB + AI
// ============================================
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/octet-stream",
        "text/event-stream",
        "application/json"
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.Fastest);

builder.Services.AddMemoryCache();

// Application Insights 8actio en Azure)
var aiConnStr = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(aiConnStr))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = aiConnStr;
    });
}


var app = builder.Build();

app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("es-ES")
    .AddSupportedCultures("es-ES")
    .AddSupportedUICultures("es-ES"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();