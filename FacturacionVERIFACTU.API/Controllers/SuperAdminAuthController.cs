using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FacturacionVERIFACTU.API.Controllers
{
    [ApiController]
    [Route("api/superadmin")]
    public class SuperAdminAuthController :ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHashService _hashService;
        private readonly IJwtService _jwtService;
        private readonly ILogger<SuperAdminAuthController> _logger;

        public SuperAdminAuthController(
            ApplicationDbContext context,
            IHashService hashService,
            IJwtService jwtService,
            ILogger<SuperAdminAuthController> logger)
        {
            _context = context;
            _hashService = hashService;
            _jwtService = jwtService;
            _logger = logger;
        }

        ///<summary>
        ///Login exclusivo de SuperAdmin
        /// </summary>
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            //Buscar usuario SuperAdmin por email
            var usuario = await _context.Usuarios
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u =>
                    u.Email == request.Email &&
                    u.Rol == "SuperAdmin" &&
                    u.Activo);

            if(usuario == null)
            {
                _logger.LogWarning("Intento de login SuperAdmin fallido para: {Email}", request.Email);
                return Unauthorized(new { message = "Credenciales invalidas" });
            }

            if(!_hashService.VerifyPassword(request.Password, usuario.PasswordHash))
            {
                _logger.LogWarning("Password incorrecto en login SuperAdmin: {Email}", request.Email);
                return Unauthorized(new { message = "Password incorrecto" });
            }

            //Actializar ultimo acceso
            usuario.UltimoAcceso = DateTime.UtcNow;

            //Generar tokens
            var accessToken = await _jwtService.GenerateAccessToken(
                usuario.Id,
                usuario.Email,
                usuario.TenantId,
                "SuperAdmin");

            var refreshToken = _jwtService.GenerateRefreshToken();

            //Revocar tokens anteriorres
            var oldTokens = await _context.RefreshTokens
                .Where(rt => rt.UsuarioId == usuario.Id && !rt.Revoked)
                .ToListAsync();

            foreach(var token in oldTokens ) 
                token.Revoked = true;

            _context.RefreshTokens.Add(new RefreshToken
            {
                Token = refreshToken,
                UsuarioId = usuario.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation("✅ Login SuperAdmin exitoso: {Email}", usuario.Email);

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                User = new UserInfo
                {
                    UserId = usuario.Id,
                    Email = usuario.Email,
                    NombreCompleto = usuario.NombreCompleto,
                    TenantId = usuario.TenantId,
                    NombreEmpresa = "SuperAdmin",
                    Role = "SuperAdmin"
                }
            });

        }
    }
}
