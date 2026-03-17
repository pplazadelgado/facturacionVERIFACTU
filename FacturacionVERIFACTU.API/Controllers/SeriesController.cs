using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;


namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SeriesController :ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<SeriesController> _logger;

        public SeriesController (
            ApplicationDbContext context,
            ITenantContext tenantContext,
            ILogger<SeriesController> logger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResponseDto<SerieDto>>> GetSeries(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            [FromQuery] string? tipoDocumento = null,
            [FromQuery] int? ejercicio = null,
            [FromQuery] bool soloActivas = false)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null || tenantId == 0)
                return Unauthorized(new { message = "Tenant no identificado" });

            if (page < 1)
                page = 1;

            if (pageSize < 1)
                pageSize = 100;

            if (pageSize > 100)
                pageSize = 100;

            var query = _context.SeriesNumeracion
                .Where(s => s.TenantId == tenantId.Value)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(tipoDocumento))
            {
                var tipoNormalizado = DocumentTypes.Normalize(tipoDocumento);
                if(!DocumentTypes.IsValid(tipoNormalizado))
                    return BadRequest(new { message = "TipoDocumento invalido" });

                query = query.Where(s => s.TipoDocumento == tipoNormalizado);
            }

            if (ejercicio.HasValue)
            {
                query = query.Where(s => s.Ejercicio == ejercicio.Value);
            }

            if (soloActivas)
            {
                query = query.Where(s => s.Activo && !s.Bloqueada);
            }

            var totalItems = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.TipoDocumento)
                .ThenByDescending(s => s.Ejercicio)
                .ThenBy(s => s.Codigo)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SerieDto
                {
                    Id = s.Id,
                    Codigo = s.Codigo,
                    Descripcion = s.Descripcion,
                    TipoDocumento = s.TipoDocumento,
                    Ejercicio = s.Ejercicio,
                    ProximoNumero = s.ProximoNumero,
                    Activo = s.Activo,
                    Bloqueada = s.Bloqueada,
                    Formato = s.Formato
                })
                .ToListAsync();

            var response = new PaginatedResponseDto<SerieDto>
            {
                Items = items,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };

            return Ok(response);            
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SerieDto>> GetSerie(int id)
        {
            var tenantId = _tenantContext.GetTenantId();
            if(tenantId == null || tenantId == 0)
            {
                return Unauthorized(new { message = "Tenant no identificado" });
            }

            var serie = await _context.SeriesNumeracion
                .Where(s => s.TenantId == tenantId.Value && s.Id == id)
                .Select(s => new SerieDto
                {
                    Id = s.Id,
                    Codigo = s.Codigo,
                    Descripcion = s.Descripcion,
                    TipoDocumento = s.TipoDocumento,
                    Ejercicio = s.Ejercicio,
                    ProximoNumero = s.ProximoNumero,
                    Activo = s.Activo,
                    Bloqueada = s.Bloqueada,
                    Formato = s.Formato
                })
                .FirstOrDefaultAsync();

            if (serie == null)
            {
                return NotFound(new { message = "Serie no encontrado" });
            }

            return Ok(serie);
        }

        [HttpPost]
        public async Task<ActionResult<SerieDto>> CrearSerie([FromBody] SerieCreateDto dto)
        {
            var tenantId = _tenantContext.GetTenantId();
            if(tenantId == null || tenantId == 0)
            {
                return Unauthorized(new { message = "Tenant no identificado" });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(dto.Codigo))
            {
                return BadRequest(new { message = "El codigo es obligatorio" });
            }

            if (string.IsNullOrWhiteSpace(dto.Descripcion))
            {
                return BadRequest(new { message = "La descripcion es obligatoria" });
            }

            if (dto.Ejercicio < 2000)
            {
                return BadRequest(new { message = "El ejercicio debe ser igual o mayor a 2000" });
            }

            var tipoNormalizado = DocumentTypes.Normalize(dto.TipoDocumento);
            if (!DocumentTypes.IsValid(tipoNormalizado))
            {
                return BadRequest(new { message = "TipoDocumento invalido" });
            }

            var codigoNormalizado = dto.Codigo.Trim();
            var descripcionNormalizada = dto.Descripcion.Trim();

            var existeSerie = await _context.SeriesNumeracion.AnyAsync(s =>
                s.TenantId == tenantId.Value &&
                s.TipoDocumento == tipoNormalizado &&
                s.Ejercicio == dto.Ejercicio &&
                s.Codigo == codigoNormalizado);

            if (existeSerie)
            {
                return BadRequest(new { message = "Ya existe una serie con el mismo código, tipo y ejercicio." });
            }
            var serie = new SerieNumeracion
            {
                TenantId = tenantId.Value,
                Codigo = codigoNormalizado,
                Descripcion = descripcionNormalizada,
                TipoDocumento = tipoNormalizado,
                Ejercicio = dto.Ejercicio,
                ProximoNumero = 1,
                Formato = string.IsNullOrWhiteSpace(dto.Formato)
                ? new SerieNumeracion().Formato
                : dto.Formato.Trim(),
                Activo = dto.Activo ?? true,
                Bloqueada = false
            };

            _context.SeriesNumeracion.Add(serie);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Creada serie {SerieId} para tenant {TenantId}", serie.Id, tenantId);

            return CreatedAtAction(nameof(GetSerie), new { id = serie.Id }, MapToDto(serie));
       }

        [HttpPut("{id}")]
        public async Task<ActionResult<SerieDto>> ActualizarSerie(int id, [FromBody] SerieUpdateDto dto)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null || tenantId == 0)
            {
                return Unauthorized(new { message = "Tenant no identificado" });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(dto.Descripcion))
            {
                return BadRequest(new { message = "La descripción es obligatoria." });
            }

            if (string.IsNullOrWhiteSpace(dto.Formato))
            {
                return BadRequest(new { message = "El formato es obligatorio." });
            }

            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.Id == id);

            if (serie == null)
            {
                return NotFound(new { message = "Serie no encontrada" });
            }

            if (!string.IsNullOrWhiteSpace(dto.Codigo))
            {
                var codigoNormalizado = dto.Codigo.Trim();
                if (codigoNormalizado.Length > 10)
                {
                    return BadRequest(new { message = "El código no puede superar los 10 caracteres." });
                }

                if (!string.Equals(serie.Codigo, codigoNormalizado, StringComparison.Ordinal))
                {
                    var existeSerie = await _context.SeriesNumeracion.AnyAsync(s =>
                        s.Id != serie.Id &&
                        s.TenantId == tenantId.Value &&
                        s.TipoDocumento == serie.TipoDocumento &&
                        s.Ejercicio == serie.Ejercicio &&
                        s.Codigo == codigoNormalizado);

                    if (existeSerie)
                    {
                        return BadRequest(new { message = "Ya existe una serie con el mismo código, tipo y ejercicio." });
                    }

                    serie.Codigo = codigoNormalizado;
                }
            }

            serie.Descripcion = dto.Descripcion.Trim();
            serie.Formato = dto.Formato.Trim();
            serie.Activo = dto.Activo;
            serie.Bloqueada = dto.Bloqueada;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Actualizada serie {SerieId} para tenant {TenantId}", serie.Id, tenantId);

            return Ok(MapToDto(serie));
        }

        [HttpPatch("{id}/bloquear")]
        public async Task<ActionResult<SerieDto>> BloquearSerie(int id)
        {
            return await ActualizarEstadoAsync(id, serie => serie.Bloqueada = true);
        }

        [HttpPatch("{id}/desbloquear")]
        public async Task<ActionResult<SerieDto>> DesbloquearSerie(int id)
        {
            return await ActualizarEstadoAsync(id, serie => serie.Bloqueada = false);
        }

        [HttpPatch("{id}/activar")]
        public async Task<ActionResult<SerieDto>> ActivarSerie(int id)
        {
            return await ActualizarEstadoAsync(id, serie => serie.Activo = true);
        }

        [HttpPatch("{id}/desactivar")]
        public async Task<ActionResult<SerieDto>> DesactivarSerie(int id)
        {
            return await ActualizarEstadoAsync(id, serie => serie.Activo = false);
        }

        /// <summary>
        ///  Inciliza manuelmente el proximo numero de una serie.
        ///  Util cuando una empresa migra desde otro sistema a mitad de ejercicio
        /// </summary>
        [HttpPatch("{id}/proximo-numero")]
        public async Task<ActionResult<SerieDto>> ActualizarProximoNumero(int id, [FromBody] ActualizarProximoNumeroDto dto)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null || tenantId == 0)
            {
                return Unauthorized(new { message = "Tenant no identificado" });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var serie = await _context.SeriesNumeracion.FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.Id == id);

            if (serie == null)
            {
                return NotFound(new { message = "Serie no encontrada" });
            }

            if (serie.Bloqueada)
            {
                return BadRequest(new { message = "No se puede modificar una serie bloqueada" });
            }

            if (dto.ProximoNumero < serie.ProximoNumero)
            {
                return BadRequest(new
                {
                    message = $"El proximo numero ({dto.ProximoNumero}) no puede ser menor que el actual ({serie.ProximoNumero}). Podria generar duplicados en documentos ya emitidos."
                });
            }

            serie.ProximoNumero = dto.ProximoNumero;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Prosimo numero de serie {SerieID} (tenant {TenantId} actualizado a {ProximoNumero}", serie.Id, tenantId, dto.ProximoNumero);

            return Ok(MapToDto(serie));
        }

        private async Task<ActionResult<SerieDto>> ActualizarEstadoAsync(int id, Action<SerieNumeracion> actualizar)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null || tenantId == 0)
            {
                return Unauthorized(new { message = "Tenant no identificado" });
            }

            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.Id == id);

            if (serie == null)
            {
                return NotFound(new { message = "Serie no encontrada" });
            }

            actualizar(serie);
            await _context.SaveChangesAsync();

            return Ok(MapToDto(serie));
        }

        private static SerieDto MapToDto(SerieNumeracion serie)
        {
            return new SerieDto
            {
                Id = serie.Id,
                Codigo = serie.Codigo,
                Descripcion = serie.Descripcion,
                TipoDocumento = serie.TipoDocumento,
                Ejercicio = serie.Ejercicio,
                ProximoNumero = serie.ProximoNumero,
                Activo = serie.Activo,
                Bloqueada = serie.Bloqueada,
                Formato = serie.Formato
            };
        }
    }
}

