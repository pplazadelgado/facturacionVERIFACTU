using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.DTOs;
using System.Threading.Tasks;

namespace FacturacionVERIFACTU.API.Data.Interfaces
{
    public interface ICierreEjercicioService
    {
        Task<EstadisticasCierreDTO> ObtenerEstadisticasEjercicio(int ejercicio, int tenantId);

        Task<(bool existo, string mensaje, CierreRealizadoDTO resultado)> CerrarEjercicio(
            int ejercicio, int tenantId, int usuarioId);

        Task<(bool existo, string mensaje)> ReabrirEjercicio(
            int cierreId, string motivo, int usuarioId);

        Task<CierreEjercicioDTO> ObtenerCierreDTO(int cierreId, int tenantId);

        Task<CierreEjercicio> ObtenerCierreEntidad(int cierreId, int tenantId);

        Task<(bool esValido, string mensaje)> ValidarCierre(int ejercicio, int tenantId);

        Task<PaginatedResponseDto<CierreEjercicioDTO>> ObtenerHistorialCierresDTO(
            int tenantId, int page, int pageSize, int? ejercicio = null);
        Task<List<int>> ObtenerEjerciciosDisponiblesAsync(int tenantId);
    }
}
