using System.Drawing;
using System.Net;
using System.Net.Mail;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string toEmail, string nombre, string resetUrl);
    }
    
    public class EmailService :IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string nombre, string resetUrl)
        {
            var smtpHost = _configuration["Email:Smtphost"];
            var smtpPort = _configuration.GetValue<int>("Email:SmtpPort", 587);
            var smtpUser = _configuration["Email:SmtpUser"];
            var smtpPass = _configuration["Email:SmtpPassword"];
            var fromEmail = _configuration["Email:FromEmail"] ?? smtpUser;
            var fromName = _configuration["Email:FromName"] ?? "FacturacionVERIFACTU";

            //Si no hay SMTP configurado, logeamos el enlace
            if(string.IsNullOrEmpty(smtpHost))
            {
                _logger.LogWarning(
                    "SMTP no configurado. Enlace de reseteo para {Email}: {Url}", toEmail, resetUrl);
                return;
            }

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpUser, smtpPass)
            };

            var body = GenerarCuerpoEmail(nombre, resetUrl);

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "Restablecer contraseña - FacturacionVERIFACTU",
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Email de reseteo enviado a {Email}", toEmail);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al enviar emial de reseteo a {Email}", toEmail);
            }
        }

        private static string GenerarCuerpoEmail(string nombre, string resetUrl)
        {
            return $"""
                <!DOCTYPE html>
                <html>
                <body style="font-family: Inter, Arial, sans-serif; background: #f6f7fb; padding: 40px 0;">
                  <div style="max-width: 520px; margin: 0 auto; background: #fff; border-radius: 16px; 
                              border: 1px solid #e8e8e8; padding: 40px; box-shadow: 0 4px 16px rgba(0,0,0,0.06);">
                    
                    <div style="text-align: center; margin-bottom: 32px;">
                      <h1 style="margin: 0; font-size: 22px; font-weight: 800; color: #0f172a;">
                        Restablecer contraseña
                      </h1>
                    </div>

                    <p style="color: #475569; font-size: 15px; line-height: 1.6; margin-bottom: 8px;">
                      Hola <strong>{nombre}</strong>,
                    </p>
                    <p style="color: #475569; font-size: 15px; line-height: 1.6; margin-bottom: 28px;">
                      Hemos recibido una solicitud para restablecer la contraseña de tu cuenta. 
                      Haz clic en el botón para crear una nueva contraseña:
                    </p>

                    <div style="text-align: center; margin-bottom: 32px;">
                      <a href="{resetUrl}" 
                         style="display: inline-block; background: #0f172a; color: #fff; 
                                font-weight: 700; font-size: 15px; padding: 14px 32px; 
                                border-radius: 12px; text-decoration: none;">
                        Restablecer contraseña
                      </a>
                    </div>

                    <p style="color: #94a3b8; font-size: 13px; line-height: 1.6; margin-bottom: 8px;">
                      Este enlace expira en <strong>1 hora</strong>.
                    </p>
                    <p style="color: #94a3b8; font-size: 13px; line-height: 1.6; margin-bottom: 0;">
                      Si no solicitaste restablecer tu contraseña, puedes ignorar este mensaje.
                    </p>

                    <hr style="border: none; border-top: 1px solid #f1f1f0; margin: 28px 0;" />
                    <p style="color: #cbd5e1; font-size: 12px; text-align: center; margin: 0;">
                      FacturacionVERIFACTU · Sistema de facturación
                    </p>
                  </div>
                </body>
                </html>
                """;
        }
    }
}
