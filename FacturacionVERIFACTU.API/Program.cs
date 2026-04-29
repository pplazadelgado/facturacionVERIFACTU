using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.Middleware;
using FacturacionVERIFACTU.API.Data.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using FluentValidation;
using FacturacionVERIFACTU.API.Validators;
using FacturacionVERIFACTU.API.Services;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Polly.Bulkhead;


var builder = WebApplication.CreateBuilder(args);

// ===== DATABASE =====
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ===== AUTENTICACIÓN JWT =====
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret no configurado en appsettings.json");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    // Eventos para debugging (opcional)
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"❌ Auth failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("✅ Token validado correctamente");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddValidatorsFromAssemblyContaining<CrearClienteValidator>();


// ============================================
// CONFIGURAR SERILOG
// ============================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/verifactu-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
    .CreateLogger();

// ===== SERVICIOS =====
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IHashService, HashService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ISerieNumeracionService, SerieNumeracionService>();
builder.Services.AddScoped<IPresupuestoService, PresupuestoService>();
builder.Services.AddScoped<IAlbaranService, AlbaranService>();
builder.Services.AddScoped<IFacturaService, FacturaService>();
builder.Services.AddScoped<ICierreEjercicioService, CierreEjercicioService>();
builder.Services.AddScoped<VERIFACTUService>();
builder.Services.AddScoped<IPDFService, PDFService>();
builder.Services.AddScoped<ITenantInitializationService, TenantInitializationService> ();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<SuperAdminSeederService>();
builder.Services.AddScoped<IRecibosPdfService, RecibosPdfService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor",
        policy =>
        {
            policy.SetIsOriginAllowed(origin =>
            {
                var uri = new Uri(origin);
                return uri.Host == "localhost" ||
                       uri.Host == "127.0.0.1" ||
                       uri.Host.EndsWith(".railway.app");
            })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

// ===== CONTROLLERS & SWAGGER =====
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Configurar autenticación JWT en Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingresa tu token JWT: Bearer {token}"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ============================================
// BLOQUE 29: CACHÉ + APPLICATION INSIGHTS
// ============================================
// Después — fuerza la configuración sin SizeLimit
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = null; // sin límite, evita el error
    options.CompactionPercentage = 0.25;
});

builder.Services.AddScoped<ICacheService, CacheService>();

//Application Insights (vacio en dev, activo en Azure)
var aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}

// ============================================
// CONFIGURAR HTTPCLIENT CON POLLY
// ============================================
var aeatUrl = builder.Configuration.GetValue<string>("VERIFACTU:AEATUrl")
    ?? "https://prewww2.aeat.es/wlpl/TGVI-SJDT/VeriFactuServiceS";
var timeoutSegundos = builder.Configuration.GetValue<int>("VERIFACTU:TimeoutSegundos", 30);

builder.Services.AddHttpClient<AEATClient>(client =>
{
    client.BaseAddress = new Uri(aeatUrl);
    client.Timeout = TimeSpan.FromSeconds(timeoutSegundos);
    client.DefaultRequestHeaders.Add("Accept", "application/xml");
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(timeoutSegundos)));

builder.Services.AddTransient<IAEATClient>(sp => sp.GetRequiredService<AEATClient>());

// ===== CORS =====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});



var app = builder.Build();

// ===== MIDDLEWARE PIPELINE =====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("AllowAll");
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// ⚠️ ORDEN CRÍTICO ⚠️
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("AllowBlazor");
app.UseAuthentication();   // 1️⃣ Valida JWT
app.UseTenantMiddleware();  // 2️⃣ Extrae tenant_id
app.UseAuthorization();     // 3️⃣ Verifica permisos

app.MapControllers();

// Seeed SuperAdmin
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<SuperAdminSeederService>();
    await seeder.SeedAsync();
}

app.Run();


// ============================================
// POLÍTICAS DE POLLY
// ============================================
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Log.Warning(
                    "Reintento {RetryAttempt} después de {Delay}s debido a: {Error}",
                    retryAttempt,
                    timespan.TotalSeconds,
                    outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString() ?? "Unknown"
                );
            }
        );
}

public partial class Program { }

