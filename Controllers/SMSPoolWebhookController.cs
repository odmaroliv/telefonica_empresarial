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
        public async Task<IActionResult> RecibirSMS([FromForm] SMSPoolSMSEvento evento)
        {
            try
            {
                _logger.LogInformation($"Webhook de SMSPool recibido - OrderId: {evento.OrderId}, Mensaje: {(evento.Message?.Length > 20 ? evento.Message.Substring(0, 20) + "..." : evento.Message)}");

                // Verificar firma o token (implementación básica)
                var apiKey = _configuration["SMSPool:ApiKey"];
                if (string.IsNullOrEmpty(evento.Key) || evento.Key != apiKey)
                {
                    _logger.LogWarning("Webhook de SMSPool recibido con API key inválida");
                    return Unauthorized("API key inválida");
                }

                // Buscar el número en nuestra base de datos
                var numero = await _context.SMSPoolNumeros
                    .FirstOrDefaultAsync(n => n.OrderId == evento.OrderId);

                if (numero == null)
                {
                    _logger.LogWarning($"SMS recibido para OrderId desconocido: {evento.OrderId}");
                    return BadRequest("OrderId no encontrado");
                }

                // Verificar si el mensaje ya existe
                var mensajeExistente = await _context.SMSPoolVerificaciones
                    .AnyAsync(v => v.NumeroId == numero.Id && v.MensajeCompleto == evento.Message);

                if (mensajeExistente)
                {
                    _logger.LogInformation($"Mensaje duplicado para OrderId: {evento.OrderId}");
                    return Ok("Mensaje duplicado");
                }

                // Extraer código de verificación
                string codigo = await ExtraerCodigoVerificacion(evento.Message);

                // Guardar el SMS
                var verificacion = new SMSPoolVerificacion
                {
                    NumeroId = numero.Id,
                    FechaRecepcion = DateTime.UtcNow,
                    MensajeCompleto = evento.Message,
                    CodigoExtraido = codigo,
                    Remitente = evento.Sender ?? "Desconocido"
                };

                _context.SMSPoolVerificaciones.Add(verificacion);

                // Actualizar estado del número
                numero.SMSRecibido = true;
                numero.CodigoRecibido = codigo;
                numero.FechaUltimaComprobacion = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"SMS guardado correctamente para OrderId: {evento.OrderId}");
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

    // Clase para mapear eventos de SMSPool
    public class SMSPoolSMSEvento
    {
        public string Key { get; set; }
        public string OrderId { get; set; }
        public string Message { get; set; }
        public string Sender { get; set; }
    }
}