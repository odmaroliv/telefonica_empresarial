using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/webhooks/smspool")]
    public class SMSPoolWebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SMSPoolWebhookController> _logger;

        public SMSPoolWebhookController(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<SMSPoolWebhookController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("sms")]
        public async Task<IActionResult> RecibirSMS([FromBody] SMSPoolWebhookEvento evento)
        {
            try
            {
                _logger.LogInformation($"Webhook de SMSPool recibido - OrderId: {evento.orderid}, Mensaje: {(evento.full_sms?.Length > 20 ? evento.full_sms.Substring(0, 20) + "..." : evento.full_sms)}");

                // Ya no necesitamos verificar API key porque SMSPool no la envía en sus webhooks
                // En su lugar, podrías implementar algún otro método de autenticación o usar HTTPS

                // Buscar el número en nuestra base de datos
                var numero = await _context.SMSPoolNumeros
                    .FirstOrDefaultAsync(n => n.OrderId == evento.orderid);

                if (numero == null)
                {
                    _logger.LogWarning($"SMS recibido para OrderId desconocido: {evento.orderid}");
                    return BadRequest("OrderId no encontrado");
                }

                // Verificar si el mensaje ya existe
                var mensajeExistente = await _context.SMSPoolVerificaciones
                    .AnyAsync(v => v.NumeroId == numero.Id && v.MensajeCompleto == evento.full_sms);

                if (mensajeExistente)
                {
                    _logger.LogInformation($"Mensaje duplicado para OrderId: {evento.orderid}");
                    return Ok("Mensaje duplicado");
                }

                // Usar el código ya extraído por SMSPool o extraerlo nosotros mismos
                string codigo = !string.IsNullOrEmpty(evento.sms)
                    ? evento.sms
                    : await ExtraerCodigoVerificacion(evento.full_sms);

                // Guardar el SMS
                var verificacion = new SMSPoolVerificacion
                {
                    NumeroId = numero.Id,
                    FechaRecepcion = DateTime.UtcNow,
                    MensajeCompleto = evento.full_sms,
                    CodigoExtraido = codigo,
                    Remitente = "SMSPool Webhook" // SMSPool no proporciona remitente en el webhook
                };

                _context.SMSPoolVerificaciones.Add(verificacion);

                // Actualizar estado del número
                numero.SMSRecibido = true;
                numero.CodigoRecibido = codigo;
                numero.FechaUltimaComprobacion = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"SMS guardado correctamente para OrderId: {evento.orderid}");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar webhook de SMSPool");
                return StatusCode(500, "Error interno del servidor");
            }
        }

        private async Task<string> ExtraerCodigoVerificacion(string mensaje)
        {
            try
            {
                if (string.IsNullOrEmpty(mensaje))
                {
                    return "";
                }

                // Buscar patrón de código (4-8 dígitos)
                var regex = new System.Text.RegularExpressions.Regex(@"\b\d{4,8}\b");
                var match = regex.Match(mensaje);

                if (match.Success)
                {
                    return match.Value;
                }

                // Buscar patrón de código con letras y números (común en algunos servicios)
                regex = new System.Text.RegularExpressions.Regex(@"\b[A-Z0-9]{4,8}\b");
                match = regex.Match(mensaje);

                if (match.Success)
                {
                    return match.Value;
                }

                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al extraer código de verificación");
                return "";
            }
        }
    }

    // Clase actualizada para mapear eventos de SMSPool según la documentación
    public class SMSPoolWebhookEvento
    {
        public string orderid { get; set; }
        public string sms { get; set; }
        public string full_sms { get; set; }
        public string timestamp { get; set; }
    }
}