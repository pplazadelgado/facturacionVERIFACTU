using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;



namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class EjerciciosController:ControllerBase
    {
        private readonly ApplicationDbContext _contex;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<EjerciciosController> _logger;

        public EjerciciosController(
            ApplicationDbContext context,
            ITenantContext tenantContext,
            ILogger<EjerciciosController> logger)
        {
            _contex = context;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<int>),StatusCodes.Status200OK)]
        public async Task<ActionResult<List<int>>> ObtenerEjercicios()
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                    return Unauthorized(new { mensaje = "Tenant no identificado" });

                var ejercicios = await _contex.SeriesNumeracion
                    .Where(s => s.TenantId == tenantId)
                    .Select(s => s.Ejercicio)
                    .Distinct()
                    .OrderByDescending(e => e)
                    .ToListAsync();

                return Ok(ejercicios);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al obtener los ejercicios");
                return StatusCode(500, new { mensaje = "Error al obtener los ejercicios" });
            }
        }
    }
}
