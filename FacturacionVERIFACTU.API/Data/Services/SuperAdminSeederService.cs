using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public class SuperAdminSeederService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHashService _hashService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SuperAdminSeederService> _logger;

        public SuperAdminSeederService(
            ApplicationDbContext context,
            IHashService hashService,
            IConfiguration configuration,
            ILogger<SuperAdminSeederService> logger)
        {
            _context = context;
            _hashService = hashService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            var email = _configuration["SuperAdmin:Email"];
            var password = _configuration["SuperAdmin:Password"];
            var nombre = _configuration["SuperAdmin:NombreCompleto"];

            if(string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("SuperAdmin no configurado en appsettins. Omitiendo seed");
                return;
            }

            // ¿Ya existe? Si existe, aseguramos consistencia mínima (password/activo/nombre)
            var superAdminExistente = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == email && u.Rol == "SuperAdmin");

            // Tenant interno reservado para SuperAmdin
            var tenantSuperAdmin = await _context.Tenants
                .FirstOrDefaultAsync(t => t.NIF == "SUPERADMIN");

            if(tenantSuperAdmin == null)
            {
                tenantSuperAdmin = new Tenant
                {
                    Nombre = "Sistema SuperAdmin",
                    NIF = "SUPERADMIN",
                    Schema = "superadmin",
                    Activo = true,
                    Plan = "Activo",
                    FechaInicioPlan = DateTime.UtcNow
                };
                _context.Tenants.Add(tenantSuperAdmin);
                await _context.SaveChangesAsync();
            }

            if (superAdminExistente != null)
            {
                var huboCambios = false;

                // Asegurar tenant correcto
                if (superAdminExistente.TenantId != tenantSuperAdmin.Id)
                {
                    superAdminExistente.TenantId = tenantSuperAdmin.Id;
                    huboCambios = true;
                }

                // Asegurar activo
                if (!superAdminExistente.Activo)
                {
                    superAdminExistente.Activo = true;
                    huboCambios = true;
                }

                // Sincronizar nombre (si se configuró)
                if (!string.IsNullOrWhiteSpace(nombre) && superAdminExistente.Nombre != nombre)
                {
                    superAdminExistente.Nombre = nombre;
                    huboCambios = true;
                }

                // Si la contraseña configurada no coincide, la actualizamos (solo SuperAdmin)
                if (!_hashService.VerifyPassword(password!, superAdminExistente.PasswordHash))
                {
                    superAdminExistente.PasswordHash = _hashService.HashPassword(password!);
                    huboCambios = true;
                    _logger.LogWarning("🔐 Password de SuperAdmin actualizado desde configuración: {Email}", email);
                }

                if (huboCambios)
                    await _context.SaveChangesAsync();

                _logger.LogInformation("SuperAdmin ya existe. Seed verificado.");
                return;
            }

            var superAdmin = new Usuario
            {
                Email = email,
                PasswordHash = _hashService.Hash(password),
                Nombre = nombre ?? "Super Administrador",
                Rol = "SuperAdmin",
                Activo = true,
                TenantId = tenantSuperAdmin.Id,
                FechaCreaccion = DateTime.UtcNow
            };


            _context.Usuarios.Add(superAdmin);
            await _context.SaveChangesAsync();

            _logger.LogInformation("✅ SuperAdmin creado: {Email}", email);
        }
    }
}
