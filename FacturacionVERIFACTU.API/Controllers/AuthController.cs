using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacturacionVERIFACTU.API.Data.Interfaces;
using System.ComponentModel;


namespace FacturacionVERIFACTU.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController :ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHashService _hashService;
        private readonly IJwtService _jwtService;
        private readonly ITenantInitializationService _tenantInitService;
        private readonly ILogger<AuthController> _logger;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        public AuthController(
            ApplicationDbContext context,
            IHashService hasService,
            IJwtService jwtService,
            ITenantInitializationService tenantIntiService,
            ILogger<AuthController> logger,
            IEmailService emailService
,
            IConfiguration configuration)
        {
            _context = context;
            _hashService = hasService;
            _jwtService = jwtService;
            _tenantInitService = tenantIntiService;
            _logger = logger;
            _emailService = emailService;
            _configuration = configuration;
        }

        ///<summary>
        ///POST /api/auth/register - Registra nuevo usuario y empresa
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Verificar email único
                var emailExists = await _context.Usuarios.AnyAsync(u => u.Email == request.Email);
                if (emailExists)
                {
                    return BadRequest(new { mensaje = "El email ya está registrado" });
                }

                // 2. Verificar NIF único
                var nifExists = await _context.Tenants.AnyAsync(t => t.NIF == request.NIF);
                if (nifExists)
                {
                    return BadRequest(new { mensaje = "El NIF ya está registrado" });
                }

                // 3. Crear Tenant
                var tenant = new Tenant
                {
                    Nombre = request.NombreEmpresa,
                    NIF = request.NIF,
                    Activo = true,
                    FechaAlta = DateTime.UtcNow
                };

                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync();

                // 4. Crear Usuario Admin
                var usuario = new Usuario
                {
                    Email = request.Email,
                    PasswordHash = _hashService.Hash(request.Password),
                    Nombre = request.NombreCompleto,
                    Rol = "Admin",
                    Activo = true,
                    TenantId = tenant.Id,
                    FechaCreaccion = DateTime.UtcNow
                };

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();


                // 5. ✨ INICIALIZAR TENANT (NUEVO)
                await _tenantInitService.IncicializarTenantAsync(tenant.Id);

                // 6. Generar tokens
                var accessToken = await _jwtService.GenerateAccessToken(
                    usuario.Id, usuario.Email, tenant.Id, usuario.Rol);
                var refreshToken =  _jwtService.GenerateRefreshToken();

                var refreshTokenEntity = new RefreshToken
                {
                    Token = refreshToken,
                    UsuarioId = usuario.Id,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    CreatedAt = DateTime.UtcNow,
                    Revoked = false
                };

                _context.RefreshTokens.Add(refreshTokenEntity);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation("Empresa {Empresa} y usuario {Email} registrados correctamente",
                    tenant.Nombre, usuario.Email);

                return Ok(new AuthResponse
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                    User = new UserInfo
                    {
                        UserId = usuario.Id,
                        Email = usuario.Email,
                        NombreCompleto = usuario.Nombre,
                        TenantId = tenant.Id,
                        NombreEmpresa = tenant.Nombre,
                        Role = usuario.Rol
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error en registro de empresa");
                return StatusCode(500, new { mensaje = "Error interno del servidor" });
            }
        }
        /// <summary>
        /// POST /api/auth/login - Inicia sesión
        /// </summary>
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            // Buscar usuario con tenant
            var usuario = await _context.Usuarios
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == request.Email);

            if (usuario == null)
            {
                return Unauthorized(new { message = "Credenciales inválidas" });
            }

            // Verificar contraseña
            if (!_hashService.VerifyPassword(request.Password, usuario.PasswordHash))
            {
                return Unauthorized(new { message = "Credenciales inválidas" });
            }

            // Verificar que usuario y tenant estén activos
            if (!usuario.Activo || !usuario.Tenant.Activo)
            {
                return Unauthorized(new { message = "Usuario o empresa inactivos" });
            }

            // Generar tokens
            var accessToken = await _jwtService.GenerateAccessToken(
                usuario.Id,
                usuario.Email,
                usuario.TenantId,
                usuario.Role
            );

            var refreshToken = _jwtService.GenerateRefreshToken();

            // Revocar tokens anteriores del usuario
            var oldTokens = await _context.RefreshTokens
                .Where(rt => rt.UsuarioId == usuario.Id && !rt.Revoked)
                .ToListAsync();

            foreach (var token in oldTokens)
            {
                token.Revoked = true;
            }

            // Guardar nuevo refresh token
            var tokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UsuarioId = usuario.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            usuario.UltimoAcceso = DateTime.UtcNow;

            _context.RefreshTokens.Add(tokenEntity);
            await _context.SaveChangesAsync();

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
                    NombreEmpresa = usuario.Tenant.NombreEmpresa,
                    Role = usuario.Role
                }
            });
        }

        /// <summary>
        /// POST /api/auth/refresh-token - Refresca access token
        /// </summary>
        [HttpPost("refresh-token")]
        public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            // Buscar refresh token válido
            var tokenEntity = await _context.RefreshTokens
                .Include(rt => rt.Usuario)
                    .ThenInclude(u => u.Tenant)
                .FirstOrDefaultAsync(rt =>
                    rt.Token == request.RefreshToken &&
                    !rt.Revoked &&
                    rt.ExpiresAt > DateTime.UtcNow
                );

            if (tokenEntity == null)
            {
                return Unauthorized(new { message = "Refresh token inválido o expirado" });
            }

            var usuario = tokenEntity.Usuario;

            // Verificar que usuario y tenant estén activos
            if (!usuario.Activo || !usuario.Tenant.Activo)
            {
                return Unauthorized(new { message = "Usuario o empresa inactivos" });
            }

            // Generar nuevos tokens
            var newAccessToken = await _jwtService.GenerateAccessToken(
                usuario.Id,
                usuario.Email,
                usuario.TenantId,
                usuario.Role
            );

            var newRefreshToken =  _jwtService.GenerateRefreshToken();

            // Revocar token anterior
            tokenEntity.Revoked = true;

            // Guardar nuevo refresh token
            var newTokenEntity = new RefreshToken
            {
                Token = newRefreshToken,
                UsuarioId = usuario.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(newTokenEntity);
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                User = new UserInfo
                {
                    UserId = usuario.Id,
                    Email = usuario.Email,
                    NombreCompleto = usuario.NombreCompleto,
                    TenantId = usuario.TenantId,
                    NombreEmpresa = usuario.Tenant.NombreEmpresa,
                    Role = usuario.Role
                }
            });
        }

        /// <summary>
        /// POST /api/auth/forgot-password
        /// Solicita el reseteo de contraseña. Siempre devuelve 200 para no
        /// revelar si el email existe o no en el sistema.
        /// </summary>
        [HttpPost("forgot-password")]
        public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
            [FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var usuario = await _context.Usuarios
                    .FirstOrDefaultAsync(u => u.Email == request.Email && u.Activo);

                //Respuesta generica siempre
                var respuestaGenerica = new ForgotPasswordResponse
                {
                    Success = true,
                    Message = "Si el email esta registrado, recibiras un enlace praa restablecer tu contraseña"
                };

                if(usuario == null)
                {
                    _logger.LogInformation(
                        "Solicitudo de recuperar contraseña no registrada: {Email}", request.Email);
                    return Ok(respuestaGenerica);
                }

                //Invaliar token anteriores del usuario
                var tokenAnteriores = await _context.PasswordResetTokens
                    .Where(t => t.UsuarioId == usuario.Id && !t.Used && t.ExpiresAt > DateTime.UtcNow)
                    .ToListAsync();

                foreach (var t in tokenAnteriores)
                    t.Used = true;

                //Generar Token seguro
                var tokenBytes = new byte[48];
                using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                rng.GetBytes(tokenBytes);
                var token = Convert.ToBase64String(tokenBytes)
                     .Replace("+", "-").Replace("/", "_").Replace("=", "");

                var resetToken = new PasswordResetToken
                {
                    Token = token,
                    UsuarioId = usuario.Id,
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    CreatedAt = DateTime.UtcNow,
                    Used = false
                };

                _context.PasswordResetTokens.Add(resetToken);
                await _context.SaveChangesAsync();

                //Construir URL de reseteo
                var baseUrl = _configuration["App:BaseUrl"] ?? "http://localhost:5194";
                var resetUrl = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";

                //Enviar email
                await _emailService.SendPasswordResetEmailAsync(
                    usuario.Email,
                    usuario.NombreCompleto,
                    resetUrl);

                _logger.LogInformation(
                    "Token de reseteo generado para usuario {Id} {Email}", usuario.Id, usuario.Email);
                return Ok(respuestaGenerica);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error recuperando contraseña para {Email}", request.Email);
                return StatusCode(500, new { mensaje = "Erro al procesar la solicitud" });
            }
        }

        /// <summary>
        /// POST /api/auth/reset-password
        /// Valida el token y establece la nueva contraseña.
        /// </summary>
        [HttpPost("reset-password")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                var resetToken = await _context.PasswordResetTokens
                    .Include(t => t.Usuario)
                    .FirstOrDefaultAsync(t =>
                        t.Token == request.Token &&
                        !t.Used &&
                        t.ExpiresAt > DateTime.UtcNow);

                if (resetToken == null)
                {
                    return BadRequest(new { mensaje = "El enlace no es válido o ha expirado." });
                }

                var usuario = resetToken.Usuario;

                if (!usuario.Activo)
                {
                    return BadRequest(new { mensaje = "La cuenta está desactivada." });
                }

                // Actualizar contraseña
                usuario.PasswordHash = _hashService.Hash(request.NewPassword);

                // Marcar token como usado
                resetToken.Used = true;

                // Revocar todos los refresh tokens activos (sesiones abiertas)
                var refreshTokens = await _context.RefreshTokens
                    .Where(rt => rt.UsuarioId == usuario.Id && !rt.Revoked)
                    .ToListAsync();

                foreach (var rt in refreshTokens)
                    rt.Revoked = true;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Contraseña restablecida para usuario {Id} ({Email})", usuario.Id, usuario.Email);

                return Ok(new { mensaje = "Contraseña restablecida correctamente. Ya puedes iniciar sesión." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en reset-password");
                return StatusCode(500, new { mensaje = "Error al restablecer la contraseña" });
            }
        }

    }
}
  
