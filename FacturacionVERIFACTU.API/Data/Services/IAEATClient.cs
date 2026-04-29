using API.Data.Entities;
using FacturacionVERIFACTU.API.Models.VERIFACTU;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public interface IAEATClient
    {
        Task<ResultadoEnvio> EnviarFactura(Factura factura);
    }
}
