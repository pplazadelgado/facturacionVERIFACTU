using FacturacionVERIFACTU.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FacturacionVERIFACTU.API.Tests.Api
{
    public class ApiTestsFactory : WebApplicationFactory<Program>
    {
        public const string JwtSecret   = "TEST_SECRET_MUY_LARGO_PARA_QUE_FUNCIONE_32CHARS!!";
        public const string JwtIssuer   = "FacturacionVERIFACTU";
        public const string JwtAudience = "FacturacionVERIFACTU";

        private readonly string _dbName = $"ApiTests_{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Reemplazar DbContext de Npgsql por base de datos en memoria
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName)
                           .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });

            builder.UseSetting("Jwt:Secret",        JwtSecret);
            builder.UseSetting("Jwt:Issuer",        JwtIssuer);
            builder.UseSetting("Jwt:Audience",      JwtAudience);
            builder.UseSetting("VERIFACTU:UsarMock","true");
        }
    }
}
