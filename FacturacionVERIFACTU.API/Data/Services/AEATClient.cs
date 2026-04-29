using API.Data.Entities;
using FacturacionVERIFACTU.API.Models.VERIFACTU;
using System.Text;
using System.Xml.Linq;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public class AEATClient : IAEATClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AEATClient> _logger;
        private readonly IConfiguration _configuration;
        private readonly bool _usarMock;

        public AEATClient(
            HttpClient httpClient,
            ILogger<AEATClient> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _usarMock = _configuration.GetValue<bool>("VERIFACTU:UsarMock");
        }

        public async Task<ResultadoEnvio> EnviarFactura(Factura factura)
        {
            try
            {
                _logger.LogInformation("Iniciando envio factura {Numero} a AEAT (Mock: UsarMock}",
                    factura.Numero,
                    _usarMock);

                //Si esta en modo mock, simular respuesta exitosa
                if (_usarMock)
                    return await SimularEnvioMock(factura);

                //Generar XML segun espcificaciones AEAT
                var xmlEnvio = GenerarXmlVERIFACTU(factura);

                _logger.LogDebug("XML generado: {Xml}", xmlEnvio);

                //Enviar a AEAT
                var content = new StringContent(xmlEnvio, Encoding.UTF8, "application/xml");
                var response = await _httpClient.PostAsync("", content);

                var xmlRespuesta = await response.Content.ReadAsStringAsync();

                _logger.LogInformation(
                    "Respuesta AEAT recibida, Status: {StatusCode}",
                    response.StatusCode
                    );

                //parsear respuesta
                var resultado = ParsearRespuestaAEAT(xmlRespuesta);
                resultado.XmlEnviado = xmlEnvio;
                resultado.XmlRespuesta = xmlRespuesta;

                if (resultado.Exitoso)
                {
                    _logger.LogInformation(
                        "Factura {Numero} enviada exitosamente. CSV: {CSV}",
                        factura.Numero,
                        resultado.CSV
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "Error al enviar factura {Numero}: {Errores}",
                        factura.Numero,
                        string.Join(", ", resultado.Errores)
                    );
                }

                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Excepción al enviar factura {Numero} a AEAT",
                    factura.Numero
                );

                return new ResultadoEnvio
                {
                    Exitoso = false,
                    Mensaje = "Error de conexión con AEAT",
                    Errores = new List<string> { ex.Message }
                };
            }
        }

        public async Task<ResultadoEnvio>SimularEnvioMock( Factura factura)
        {
            _logger.LogInformation("Simulando envio mock para factura {Numero}", factura.Numero);


            //Simular lantencia de red
            await Task.Delay(500);

            var resultado = new ResultadoEnvio
            {
                Exitoso = true,
                CodigoRespuesta = "0",
                Mensaje ="Registro aceptado (MOCK)",
                CSV = $"MOCK{DateTime.UtcNow:yyyyMMddHHmmss}{factura.Id:D6}",
                FechaRegistro= DateTime.UtcNow,
                XmlEnviado=GenerarXmlVERIFACTU(factura),
                XmlRespuesta = GenerarRespuestaMock(factura)
            };

            _logger.LogInformation("Envio mock completado. CSV generado: {CSV}", resultado.CSV);

            return resultado;
        }

        private string GenerarXmlVERIFACTU(Factura factura)
        {
            // Construir XML según especificaciones AEAT VERIFACTU
            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("soapenv:Envelope",
                    new XAttribute(XNamespace.Xmlns + "soapenv", "http://schemas.xmlsoap.org/soap/envelope/"),
                    new XAttribute(XNamespace.Xmlns + "veri", "https://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/verifactu/ws/VeriFactuService.xsd"),

                    new XElement("soapenv:Header"),
                    new XElement("soapenv:Body",
                        new XElement("veri:RegistroFactura",
                            new XElement("veri:Cabecera",
                                new XElement("veri:ObligadoEmision",
                                    new XElement("veri:NIF", factura.Tenant.NIF ?? ""),
                                    new XElement("veri:Nombre", factura.Tenant.Nombre ?? "")
                                )
                            ),
                            new XElement("veri:RegistroAlta",
                                new XElement("veri:IDFactura",
                                    new XElement("veri:NumSerieFactura", factura.Numero),
                                    new XElement("veri:FechaExpedicion", factura.FechaEmision.ToString("dd-MM-yyyy"))
                                ),
                                new XElement("veri:TipoFactura", factura.TipoFacturaVERIFACTU ?? "F1"),
                                new XElement("veri:Destinatarios",
                                    new XElement("veri:Destinatario",
                                        new XElement("veri:NIF", factura.Cliente.NIF ?? ""),
                                        new XElement("veri:Nombre", factura.Cliente.Nombre ?? "")
                                    )
                                ),
                                new XElement("veri:ImporteTotal", factura.Total.ToString("F2")),
                                new XElement("veri:BaseImponible", factura.BaseImponible.ToString("F2")),
                                new XElement("veri:CuotaIVA", factura.TotalIVA.ToString("F2")),
                                new XElement("veri:Huella", factura.Huella ?? ""),
                                new XElement("veri:HuellaAnterior", factura.HuellaAnterior ?? "")
                            )
                        )
                    )
                )
            );

            return xml.Declaration + Environment.NewLine + xml.ToString();
        }


        private ResultadoEnvio ParsearRespuestaAEAT(string xmlRespuesta)
        {
            try
            {
                var doc = XDocument.Parse(xmlRespuesta);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                var codigoRespuesta = doc.Descendants(ns + "CodigoRespuesta").FirstOrDefault()?.Value;
                var mensaje = doc.Descendants(ns + "Mensaje").FirstOrDefault()?.Value;
                var csv = doc.Descendants(ns + "CSV").FirstOrDefault()?.Value;
                var fechaStr = doc.Descendants(ns + "FechaRegistro").FirstOrDefault().Value;

                var exitoso = codigoRespuesta == "0" || codigoRespuesta == "ACEPTADO";

                var resultado = new ResultadoEnvio
                {
                    Exitoso = exitoso,
                    CodigoRespuesta = codigoRespuesta,
                    Mensaje = mensaje,
                    CSV = csv
                };

                if (DateTime.TryParse(fechaStr, out var fecha))
                {
                    resultado.FechaRegistro = fecha;
                }

                //Parsear errores si existen
                var errores = doc.Descendants(ns + "Error")
                    .Select(e => e.Value)
                    .ToList();

                if (errores.Any())
                {
                    resultado.Errores = errores;
                }

                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al parsear respuesta XML de AEAT");
                return new ResultadoEnvio
                {
                    Exitoso = false,
                    Mensaje = "Error al parsear respuesta de AEAT",
                    Errores = new List<string> { ex.Message }
                };
            }

  
        }

        private string GenerarRespuestaMock(Factura factura)
        {
            var respuesta = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("RespuestaRegistro",
                    new XElement("CodigoRespuesta", "0"),
                    new XElement("Mensaje", "Registro aceptado (MOCK)"),
                    new XElement("CSV", $"MOCK{DateTime.UtcNow:yyyyMMddHHmmss}{factura.Id:D6}"),
                    new XElement("FechaRegistro", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"))
                )
            );

            return respuesta.Declaration + Environment.NewLine + respuesta.ToString();
        }
    }
}

