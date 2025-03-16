using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresarial.Services;
using Twilio.Security;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/webhooks")]
    public class WebhooksController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IStripeService _stripeService;
        private readonly IConfiguration _configuration;
        private readonly string _twilioAuthToken;
        private readonly ILogger<WebhooksController> _logger;

        public WebhooksController(
            ApplicationDbContext context,
            IStripeService stripeService,
            IConfiguration configuration,
            ILogger<WebhooksController> logger)
        {
            _context = context;
            _stripeService = stripeService;
            _configuration = configuration;
            _twilioAuthToken = _configuration["Twilio:AuthToken"] ?? throw new ArgumentNullException("Twilio:AuthToken");
            _logger = logger;
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

        [HttpPost("twilio/llamada")]
        public async Task<IActionResult> TwilioLlamadaWebhook([FromForm] TwilioLlamadaEvento evento)
        {
            try
            {
                // Verificar la firma de Twilio
                var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                var parameters = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());

                var validator = new RequestValidator(_twilioAuthToken);
                var signature = Request.Headers["X-Twilio-Signature"].ToString();

                if (!validator.Validate(requestUrl, parameters, signature))
                {
                    _logger.LogWarning("Firma Twilio inválida");
                    return Unauthorized("Firma Twilio inválida");
                }

                // Buscar el número en nuestra base de datos (To en Twilio es nuestro número)
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
                        Duracion = evento.CallDuration,
                        Estado = evento.CallStatus,
                        IdLlamadaPlivo = evento.CallSid
                    };

                    _context.LogsLlamadas.Add(log);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Llamada registrada: De={evento.From}, A={evento.To}, Estado={evento.CallStatus}, Duración={evento.CallDuration}s");

                    // Procesar el consumo de la llamada solo si está completada y tiene duración
                    if ((evento.CallStatus == "completed" || evento.CallStatus.Contains("complete")) && evento.CallDuration > 0)
                    {
                        try
                        {
                            // Opcional: Inyectar el servicio de telefonía
                            var telefonicaService = HttpContext.RequestServices.GetRequiredService<ITelefonicaService>();
                            var resultado = await telefonicaService.ProcesarConsumoLlamada(numeroTelefonico, log);

                            if (resultado)
                            {
                                _logger.LogInformation($"Consumo procesado correctamente para llamada {evento.CallSid}");
                            }
                            else
                            {
                                _logger.LogWarning($"No se pudo procesar el consumo para llamada {evento.CallSid}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error al procesar consumo para llamada {evento.CallSid}");
                            // Continuamos a pesar del error para no interrumpir el flujo normal
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"No se encontró número en la base de datos para: {evento.To}");
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar webhook de llamada");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("twilio/sms")]
        public async Task<IActionResult> TwilioSMSWebhook([FromForm] TwilioSMSEvento evento)
        {
            try
            {
                // Verificar la firma de Twilio
                var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                var parameters = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());

                var validator = new RequestValidator(_twilioAuthToken);
                var signature = Request.Headers["X-Twilio-Signature"].ToString();

                if (!validator.Validate(requestUrl, parameters, signature))
                {
                    return Unauthorized("Firma Twilio inválida");
                }

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
                        Mensaje = evento.Body,
                        IdMensajePlivo = evento.MessageSid
                    };

                    _context.LogsSMS.Add(log);
                    await _context.SaveChangesAsync();
                }

                // Responder con TwiML vacío
                return Content(
                    "<Response></Response>",
                    "application/xml"
                );
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    // Clases para mapear eventos de Twilio
    public class TwilioLlamadaEvento
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string CallSid { get; set; } = string.Empty;
        public int CallDuration { get; set; }
        public string CallStatus { get; set; } = string.Empty;
    }

    public class TwilioSMSEvento
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string MessageSid { get; set; } = string.Empty;
    }
}