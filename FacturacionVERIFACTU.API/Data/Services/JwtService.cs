using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public interface IJwtService
    {
        Task<string> GenerateAccessToken(int userId, string email, int tenantId, string role); // ⬅️ Task<string>
        string GenerateRefreshToken();
        ClaimsPrincipal? ValidateToken(string token);

        Task<string> GenerateImpersonationToken(
            int userId, string email, int tenantId,
            string role, int impersonatedBy);
    }

    public class JwtService : IJwtService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public JwtService(IConfiguration configuration, ApplicationDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        /// <summary>
        /// Genera Access Token JWT con claims: user_id, email, tenant_id, role, TenantSchema
        /// </summary>
        public async Task<string> GenerateAccessToken(int userId, string email, int tenantId, string role)
        {
            // Obtener el tenant de la base de datos para obtener el Schema
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                throw new InvalidOperationException($"Tenant con ID {tenantId} no encontrado");
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]
                    ?? throw new InvalidOperationException("Jwt:Secret no configurado"))
            );

            var claims = new[]
            {
                new Claim("user_id", userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("TenantId", tenantId.ToString()), // ⬅️ Para ITenantContext.GetTenantId()
                new Claim("TenantSchema", tenant.Schema ?? ""), // ⬅️ Usar Schema del tenant
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Genera Refresh Token aleatorio seguro
        /// </summary>
        public string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        /// <summary>
        /// Valida token y retorna ClaimsPrincipal (null si inválido)
        /// </summary>
        public ClaimsPrincipal? ValidateToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"] ?? "");

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = _configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                return principal;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GenerateImpersonationToken(
            int userId, string email, int tenantId,
            string role, int impersonatedBy)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
                throw new InvalidOperationException($"Tenant {tenantId} no encontrado");

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Secrect"]!));

            var claims = new[]
            {
                new Claim("user_id", userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("TenantId", tenantId.ToString()),
                new Claim("TenantSchema", tenant.Schema ?? ""),
                new Claim(ClaimTypes.Role, role),
                new Claim("impersonated_by", impersonatedBy.ToString()), // auditoría
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken
            (
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2), // token corto
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}