using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace FacturacionVERIFACTU.API.Tests.Api
{
    /// <summary>
    /// Tests de API HTTP — levantan la app entera en memoria
    /// y hacen peticiones HTTP reales contra los endpoints.
    /// </summary>
    public class ApiTests : IDisposable
    {
        // ── Constantes de test ────────────────────────────────────────────
        private const string JwtSecret   = ApiTestsFactory.JwtSecret;
        private const string JwtIssuer   = ApiTestsFactory.JwtIssuer;
        private const string JwtAudience = ApiTestsFactory.JwtAudience;
        private const int TenantId   = 1;
        private const int ClienteId  = 1;
        private const int SerieId    = 1;
        private const int ImpuestoId = 1;

        private readonly ApiTestsFactory _factory;
        private readonly HttpClient _client;

        public ApiTests()
        {
            _factory = new ApiTestsFactory();
            _client  = _factory.CreateClient();

            // Borrar cualquier dato generado en el arranque (p.ej. SuperAdmin)
            // y sembrar datos de test limpios.
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();
            context.Database.EnsureDeleted();
            SembrarDatos(context);
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE AUTENTICACIÓN
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GET_Facturas_SinToken_Devuelve401()
        {
            // Sin cabecera Authorization el servidor debe rechazar la petición
            var response = await _client.GetAsync("/api/facturas");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                because: "todos los endpoints requieren autenticación JWT");
        }

        [Fact]
        public async Task GET_Presupuestos_SinToken_Devuelve401()
        {
            var response = await _client.GetAsync("/api/presupuestos");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task GET_Clientes_SinToken_Devuelve401()
        {
            var response = await _client.GetAsync("/api/clientes");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task GET_Facturas_ConToken_Devuelve200()
        {
            // Con un token válido debe responder correctamente
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var response = await _client.GetAsync("/api/facturas");

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: "un token JWT válido debe dar acceso al endpoint");
        }

        [Fact]
        public async Task GET_Facturas_ConTokenInvalido_Devuelve401()
        {
            // Un token manipulado o firmado con otro secreto debe ser rechazado
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "token.falso.manipulado");

            var response = await _client.GetAsync("/api/facturas");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                because: "un token inválido no debe dar acceso");
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE ENDPOINTS — FACTURAS
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GET_Facturas_Autenticado_DevuelveListaVacia()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var response = await _client.GetAsync("/api/facturas");
            var facturas = await response.Content
                .ReadFromJsonAsync<List<FacturaResponseDto>>();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            facturas.Should().NotBeNull();
            facturas.Should().BeEmpty(
                because: "no hemos creado ninguna factura todavía");
        }

        [Fact]
        public async Task POST_Facturas_DatosValidos_Devuelve201()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var dto = new FacturaCreateDto
            {
                ClienteId = ClienteId,
                SerieId = SerieId,
                FechaEmision = DateTime.UtcNow,
                Lineas = new List<LineaFacturaDto>
        {
            new()
            {
                Descripcion    = "Servicio de consultoría",
                Cantidad       = 1,
                PrecioUnitario = 500m,
                TipoImpuestoId = ImpuestoId
            }
        }
            };

            var response = await _client.PostAsJsonAsync("/api/facturas", dto);

            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Created,
                because: body);
        }

        [Fact]
        public async Task POST_Facturas_ClienteInexistente_Devuelve400()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var dto = new FacturaCreateDto
            {
                ClienteId = 9999, // no existe
                SerieId = SerieId,
                FechaEmision = DateTime.UtcNow,
                Lineas = new List<LineaFacturaDto>
                {
                    new()
                    {
                        Descripcion    = "Servicio",
                        Cantidad       = 1,
                        PrecioUnitario = 100m,
                        TipoImpuestoId = ImpuestoId
                    }
                }
            };

            var response = await _client.PostAsJsonAsync("/api/facturas", dto);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                because: "un cliente inexistente debe devolver 400");
        }

        [Fact]
        public async Task GET_Factura_PorId_Inexistente_Devuelve404()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var response = await _client.GetAsync("/api/facturas/99999");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE ENDPOINTS — PRESUPUESTOS
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GET_Presupuestos_Autenticado_DevuelveOK()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var response = await _client.GetAsync("/api/presupuestos");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GET_Clientes_Autenticado_DevuelvePaginado()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarToken());

            var response = await _client.GetAsync("/api/clientes");
            var resultado = await response.Content
                .ReadFromJsonAsync<PaginatedResponseDto<ClienteResponseDto>>();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            resultado.Should().NotBeNull();
            resultado!.Items.Should().HaveCount(1,
                because: "sembramos 1 cliente en la BD de test");
        }

        // ══════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Genera un JWT válido firmado con el secreto de test.
        /// Incluye el claim tenant_id que usa el TenantMiddleware.
        /// </summary>
        private string GenerarToken(int? tenantId = null)
        {
            var claims = new[]
            {
                new Claim("tenant_id", (tenantId ?? TenantId).ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, "usuario_test"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("UserId", "1")
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: JwtIssuer,
                audience: JwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Siembra los datos mínimos necesarios para los tests de API.
        /// </summary>
        private static void SembrarDatos(ApplicationDbContext context)
        {
            context.Tenants.Add(new Tenant
            {
                Id = TenantId,
                Nombre = "Empresa Test SL",
                NIF = "B12345678",
                Schema = "test"
            });

            context.Clientes.Add(new Cliente
            {
                Id = ClienteId,
                TenantId = TenantId,
                Nombre = "Cliente Test SA",
                NIF = "A87654321",
                TipoCliente = "B2B",
                Activo = true,
                RegimenRecargoEquivalencia = false
            });

            context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SerieId,
                TenantId = TenantId,
                Codigo = "F",
                Descripcion = "Facturas",
                TipoDocumento = DocumentTypes.FACTURA,
                ProximoNumero = 1,
                Ejercicio = DateTime.UtcNow.Year,
                Activo = true,
                Bloqueada = false
            });

            context.TiposImpuesto.Add(new TipoImpuesto
            {
                Id = ImpuestoId,
                TenantId = TenantId,
                Nombre = "IVA 21%",
                PorcentajeIva = 21m,
                PorcentajeRecargo = 0m,
                Activo = true
            });

            context.SaveChanges();
        }
    }
}
