using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/webhooks")]
    public class WebhooksController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IStripeService _stripeService;

        public WebhooksController(ApplicationDbContext context, IStripeService stripeService)
        {
            _context = context;
            _stripeService = stripeService;
        }

        [HttpPost("stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var signatureHeader = Request.Headers["Stripe-Signature"];

            try
            {
                await _stripeService.ProcesarEventoWebhook(json, signatureHeader);
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("plivo/llamada")]
        public async Task<IActionResult> PlivoLlamadaWebhook([FromForm] PlivoLlamadaEvento evento)
        {
            try
            {
                // Buscar el número en nuestra base de datos
                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.Numero == evento.To);

                if (numeroTelefonico != null)
                {
                    // Registrar la llamada
                    var log = new LogLlamada
                    {
                        NumeroTelefonicoId = numeroTelefonico.Id,
                        NumeroOrigen = evento.From,
                        FechaHora = DateTime.UtcNow,
                        Duracion = evento.Duration,
                        Estado = evento.Status,
                        IdLlamadaPlivo = evento.CallUuid
                    };

                    _context.LogsLlamadas.Add(log);
                    await _context.SaveChangesAsync();
                }

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("plivo/sms")]
        public async Task<IActionResult> PlivoSMSWebhook([FromForm] PlivoSMSEvento evento)
        {
            try
            {
                // Buscar el número en nuestra base de datos
                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.Numero == evento.To);

                if (numeroTelefonico != null && numeroTelefonico.SMSHabilitado)
                {
                    // Registrar el SMS
                    var log = new LogSMS
                    {
                        NumeroTelefonicoId = numeroTelefonico.Id,
                        NumeroOrigen = evento.From,
                        FechaHora = DateTime.UtcNow,
                        Mensaje = evento.Text,
                        IdMensajePlivo = evento.MessageUuid
                    };

                    _context.LogsSMS.Add(log);
                    await _context.SaveChangesAsync();
                }

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    // Clases para mapear eventos de Plivo
    public class PlivoLlamadaEvento
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string CallUuid { get; set; } = string.Empty;
        public int Duration { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class PlivoSMSEvento
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string MessageUuid { get; set; } = string.Empty;
    }
}