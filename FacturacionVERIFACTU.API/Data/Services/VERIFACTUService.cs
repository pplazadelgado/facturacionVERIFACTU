using API.Data.Entities;
using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Services;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FacturacionVERIFACTU.API.Services
{
    /// <summary>
    /// Servicio para implementar el sistema VERIFACTU de la AEAT.
    /// Genera huellas encadenadas, códigos QR y envía facturas según normativa oficial.
    /// </summary>
    public class VERIFACTUService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAEATClient _aeatClient;
        private readonly ILogger<VERIFACTUService> _logger;

        public VERIFACTUService(
            ApplicationDbContext context,
            IAEATClient aeatClient,
            ILogger<VERIFACTUService> logger)
        {
            _context = context;
            _aeatClient = aeatClient;
            _logger = logger;
        }

        /// <summary>
        /// Procesa y envía una factura completa a VERIFACTU:
        /// 1. Calcula la huella encadenada
        /// 2. Genera el código QR
        /// 3. Envía a AEAT (real o mock)
        /// 4. Actualiza los campos de la factura
        /// </summary>
        /// <param name="facturaId">ID de la factura a procesar</param>
        /// <returns>True si el proceso fue exitoso</returns>
        public async Task<bool> ProcesarYEnviarFactura(int facturaId)
        {
            var factura = await _context.Facturas
                .Include(f => f.Tenant)
                .Include(f => f.Cliente)
                .Include(f => f.Serie)
                .Include(f => f.Lineas)
                .FirstOrDefaultAsync(f => f.Id == facturaId);

            if (factura == null)
            {
                _logger.LogWarning("Factura {FacturaId} no encontrada", facturaId);
                return false;
            }

            try
            {
                _logger.LogInformation("Iniciando procesamiento de factura {Numero}", factura.Numero);

                // 1. Obtener huella anterior y calcular nueva huella
                await CalcularHuellaFactura(factura);

                // 2. Determinar tipo de factura
                if (string.IsNullOrWhiteSpace(factura.TipoFacturaVERIFACTU))
                {
                    factura.TipoFacturaVERIFACTU = DeterminarTipoFactura(factura);
                }

                ValidarTipoFacturaVerifactu(factura.TipoFacturaVERIFACTU);

                // 3. Generar código QR
                GenerarQRFactura(factura);

                // 4. Guardar cambios antes de enviar
                await _context.SaveChangesAsync();

                // 5. Enviar a AEAT
                var resultado = await _aeatClient.EnviarFactura(factura);

                if (resultado.Exitoso)
                {
                    // Actualizar campos de envío
                    factura.EnviadaVERIFACTU = true;
                    factura.FechaEnvioVERIFACTU = DateTime.UtcNow;

                    // Guardar CSV y URL de verificación
                    if (!string.IsNullOrEmpty(resultado.CSV))
                    {
                        factura.UrlVERIFACTU = $"https://www.aeat.es/verifactu/{resultado.CSV}";
                    }

                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "Factura {Numero} procesada y enviada exitosamente. CSV: {CSV}",
                        factura.Numero,
                        resultado.CSV
                    );

                    return true;
                }
                else
                {
                    _logger.LogError(
                        "Error al enviar factura {Numero}: {Errores}",
                        factura.Numero,
                        string.Join(", ", resultado.Errores)
                    );

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al procesar factura {FacturaId}",
                    facturaId
                );
                return false;
            }
        }

        /// <summary>
        /// Calcula la huella SHA-256 de una factura y obtiene la huella anterior de la BD.
        /// </summary>
        private async Task CalcularHuellaFactura(Factura factura)
        {
            // Obtener la factura anterior del mismo tenant y serie
            var facturaAnterior = await _context.Facturas
                .Where(f => f.TenantId == factura.TenantId
                    && f.SerieId == factura.SerieId
                    && f.Id < factura.Id)
                .OrderByDescending(f => f.Id)
                .FirstOrDefaultAsync();

            factura.HuellaAnterior = facturaAnterior?.Huella ?? "";

            // Generar huella actual
            factura.Huella = GenerarHuellaFactura(factura, factura.HuellaAnterior);

            _logger.LogDebug(
                "Huella calculada para factura {Numero}: {Huella}",
                factura.Numero,
                factura.Huella
            );
        }

        /// <summary>
        /// Genera la huella SHA-256 de una factura según el algoritmo VERIFACTU.
        /// Cadena de entrada: CIF + NumFactura + Fecha(yyyyMMdd) + TipoFactura + 
        ///                    CuotaIVA + ImporteTotal + "" + HuellaAnterior
        /// </summary>
        /// <param name="factura">Factura a procesar</param>
        /// <param name="huellaAnterior">Huella de la factura anterior (cadena vacía si es la primera)</param>
        /// <returns>Huella en formato Base64</returns>
        public string GenerarHuellaFactura(Factura factura, string huellaAnterior = "")
        {
            if (factura == null)
                throw new ArgumentNullException(nameof(factura));

            // Construir cadena según especificación VERIFACTU
            var cadena = new StringBuilder();

            // 1. CIF del emisor (desde Tenant)
            cadena.Append(factura.Tenant?.NIF ?? string.Empty);

            // 2. Número de factura completo (Serie + Numero)
            cadena.Append(factura.Serie?.Codigo ?? string.Empty);
            cadena.Append(factura.Numero);

            // 3. Fecha de emisión en formato yyyyMMdd
            cadena.Append(factura.FechaEmision.ToString("yyyyMMdd"));

            // 4. Tipo de factura (F1=Normal, F2=Simplificada, F3=Rectificativa)
            var tipoFactura = DeterminarTipoFactura(factura);
            cadena.Append(tipoFactura);

            // 5. Cuota total de IVA (con 2 decimales)
            cadena.Append(factura.TotalIVA.ToString("F2"));

            // 6. Importe total (con 2 decimales)
            cadena.Append(factura.Total.ToString("F2"));

            // 7. Campo vacío (reservado)
            cadena.Append("");

            // 8. Huella de la factura anterior
            cadena.Append(huellaAnterior);

            // Calcular SHA-256
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(cadena.ToString());
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Genera el código QR con la URL de verificación de la AEAT.
        /// </summary>
        private void GenerarQRFactura(Factura factura)
        {
            var urlQR = ConstruirUrlVerificacion(factura);

            using (var qrGenerator = new QRCodeGenerator())
            {
                var qrData = qrGenerator.CreateQrCode(urlQR, QRCodeGenerator.ECCLevel.M);
                using (var qrCode = new PngByteQRCode(qrData))
                {
                    factura.QRVerifactu = qrCode.GetGraphic(20);
                }
            }

            _logger.LogDebug(
                "QR generado para factura {Numero}. Tamaño: {Bytes} bytes",
                factura.Numero,
                factura.QRVerifactu?.Length ?? 0
            );
        }

        /// <summary>
        /// Procesa una factura completa: genera huella, QR y actualiza campos VERIFACTU.
        /// (Método sin envío - útil para testing o pre-procesamiento)
        /// </summary>
        /// <param name="factura">Factura a procesar</param>
        /// <param name="huellaAnterior">Huella de la factura anterior</param>
        public void ProcesarFacturaVERIFACTU(Factura factura, string huellaAnterior = "")
        {
            if (factura == null)
                throw new ArgumentNullException(nameof(factura));

            // 1. Determinar y guardar tipo de factura
            if (string.IsNullOrWhiteSpace(factura.TipoFacturaVERIFACTU))
            {
                factura.TipoFacturaVERIFACTU = DeterminarTipoFactura(factura);
            }

            ValidarTipoFacturaVerifactu(factura.TipoFacturaVERIFACTU);

            // 2. Generar y guardar huella anterior
            if (string.IsNullOrEmpty(factura.HuellaAnterior))
            {
                factura.HuellaAnterior = huellaAnterior;
            }

            // 3. Generar y guardar huella actual
            factura.Huella = GenerarHuellaFactura(factura, huellaAnterior);

            // 4. Generar y guardar QR
            factura.QRVerifactu = GenerarCodigoQR(factura);

            // 5. Guardar URL de verificación
            factura.UrlVERIFACTU = ConstruirUrlVerificacion(factura);

            // 6. Establecer ejercicio si no está definido
            if (factura.Ejercicio == 0)
            {
                factura.Ejercicio = factura.FechaEmision.Year;
            }
        }

        /// <summary>
        /// Genera un código QR con la URL de verificación de la AEAT.
        /// URL: https://prewww2.aeat.es/wlpl/TIKE-CONT/?nif=X&num=Y&fecha=Z&importe=W
        /// </summary>
        /// <param name="factura">Factura a incluir en el QR</param>
        /// <returns>Imagen QR en formato byte array (PNG)</returns>
        public byte[] GenerarCodigoQR(Factura factura)
        {
            if (factura == null)
                throw new ArgumentNullException(nameof(factura));

            // Construir URL de verificación AEAT
            var url = ConstruirUrlVerificacion(factura);

            // Generar QR con QRCoder
            using (var qrGenerator = new QRCodeGenerator())
            {
                var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                using (var qrCode = new PngByteQRCode(qrData))
                {
                    return qrCode.GetGraphic(20); // 20 píxeles por módulo
                }
            }
        }

        /// <summary>
        /// Construye la URL de verificación para el portal TIKE de la AEAT.
        /// </summary>
        public string ConstruirUrlVerificacion(Factura factura)
        {
            var baseUrl = "https://prewww2.aeat.es/wlpl/TIKE-CONT/";

            var nif = Uri.EscapeDataString(factura.Tenant?.NIF ?? string.Empty);
            var num = Uri.EscapeDataString($"{factura.Serie?.Codigo}{factura.Numero}");
            var fecha = factura.FechaEmision.ToString("ddMMyyyy");
            var importe = factura.Total.ToString("F2").Replace(",", ".");

            return $"{baseUrl}?nif={nif}&num={num}&fecha={fecha}&importe={importe}";
        }

        /// <summary>
        /// Determina el tipo de factura según criterios VERIFACTU.
        /// F1: Factura normal
        /// F2: Factura simplificada (importe inferior a 400€)
        /// F3: Factura rectificativa
        /// </summary>
        private string DeterminarTipoFactura(Factura factura)
        {
            // Verificar si es rectificativa
            if(factura.Cliente == null)
            {
                return "F2";
            }

            // Verificar si es simplificada (< 400€)
            var cliente = factura.Cliente;
            var tieneIdentificacion = !string.IsNullOrWhiteSpace(cliente.NIF)
                || !string.Equals(cliente.TipoCliente, "B2C", StringComparison.OrdinalIgnoreCase);

            return tieneIdentificacion ? "F1" : "F2";
        }

        private static readonly HashSet<string> TiposFacturaVerifactuPermitidos = new(StringComparer.OrdinalIgnoreCase)
        {
            "F1",
            "F2",
            "F3",
            "R1",
            "R2",
            "R3",
            "R4"
        };

        private static void ValidarTipoFacturaVerifactu(string? tipoFactura)
        {
            if (string.IsNullOrWhiteSpace(tipoFactura) || !TiposFacturaVerifactuPermitidos.Contains(tipoFactura))
            {
                throw new InvalidOperationException(
                    $"TipoFacturaVERIFACTU inválido: {tipoFactura ?? "<vacío>"}.");
            }
        }
        

        /// <summary>
        /// Valida que una huella calculada coincida con la esperada.
        /// Útil para verificar la integridad de la cadena de facturas.
        /// </summary>
        /// <param name="factura">Factura a validar</param>
        /// <param name="huellaAnterior">Huella de la factura anterior</param>
        /// <param name="huellaEsperada">Huella almacenada en la factura</param>
        /// <returns>True si la huella es válida</returns>
        public bool ValidarHuella(Factura factura, string huellaAnterior, string huellaEsperada)
        {
            var huellaCalculada = GenerarHuellaFactura(factura, huellaAnterior);
            return huellaCalculada == huellaEsperada;
        }

        /// <summary>
        /// Genera el código QR como cadena Base64 (útil para APIs/JSON).
        /// </summary>
        public string GenerarCodigoQRBase64(Factura factura)
        {
            var qrBytes = GenerarCodigoQR(factura);
            return Convert.ToBase64String(qrBytes);
        }

        /// <summary>
        /// Valida la cadena de facturas completa para un tenant.
        /// </summary>
        /// <param name="facturas">Lista de facturas ordenadas por fecha</param>
        /// <returns>True si toda la cadena es válida</returns>
        public bool ValidarCadenaFacturas(List<Factura> facturas)
        {
            if (facturas == null || !facturas.Any())
                return true;

            string huellaAnterior = "";

            foreach (var factura in facturas.OrderBy(f => f.FechaEmision))
            {
                // Validar que la huella anterior coincida
                if (factura.HuellaAnterior != huellaAnterior)
                    return false;

                // Validar que la huella actual sea correcta
                var huellaCalculada = GenerarHuellaFactura(factura, huellaAnterior);
                if (factura.Huella != huellaCalculada)
                    return false;

                // Actualizar huella anterior para la siguiente iteración
                huellaAnterior = factura.Huella ?? "";
            }

            return true;
        }
    }
}