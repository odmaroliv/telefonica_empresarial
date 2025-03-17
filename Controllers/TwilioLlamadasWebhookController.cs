using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TelefonicaEmpresaria.Services;
using Twilio.Security;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/webhooks/twilio")]
    public class TwilioLlamadasWebhookController : ControllerBase
    {
        private readonly ILlamadasService _llamadasService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwilioLlamadasWebhookController> _logger;
        private readonly string _twilioAuthToken;

        public TwilioLlamadasWebhookController(
            ILlamadasService llamadasService,
            IConfiguration configuration,
            ILogger<TwilioLlamadasWebhookController> logger)
        {
            _llamadasService = llamadasService;
            _configuration = configuration;
            _logger = logger;
            _twilioAuthToken = _configuration["Twilio:AuthToken"] ?? throw new ArgumentNullException("Twilio:AuthToken no configurado");
        }

        /// <summary>
        /// Webhook para actualizar el estado de llamadas salientes
        /// </summary>
        [HttpPost("llamada-saliente")]
        [AllowAnonymous]
        public async Task<IActionResult> ActualizarLlamadaSaliente([FromForm] TwilioCallStatusEvent callEvent)
        {
            try
            {
                // Validar la firma de Twilio
                if (!ValidateTwilioSignature())
                {
                    _logger.LogWarning("Intento de acceso no autorizado al webhook de llamada saliente");
                    return Unauthorized("Firma inválida");
                }

                _logger.LogInformation($"Recibida actualización de llamada saliente: CallSid={callEvent.CallSid}, Status={callEvent.CallStatus}");

                // Validar datos mínimos necesarios
                if (string.IsNullOrEmpty(callEvent.CallSid) || string.IsNullOrEmpty(callEvent.CallStatus))
                {
                    return BadRequest("Faltan datos requeridos");
                }

                // Convertir duración de string a int si está presente
                // Solo calcular costos cuando el estado es "completed"
                if (callEvent.CallStatus == "completed")
                {
                    // La duración reportada por Twilio es la fuente de verdad
                    int? duracion = null;
                    if (!string.IsNullOrEmpty(callEvent.CallDuration) &&
                        int.TryParse(callEvent.CallDuration, out int duracionParsed))
                    {
                        duracion = duracionParsed;
                    }

                    // Llamar a un método específico para procesar finalizaciones
                    await _llamadasService.ProcesarFinalizacionLlamada(
                        callEvent.CallSid,
                        duracion
                    );
                }
                else
                {
                    // Para otros estados, solo actualizar el estado
                    await _llamadasService.ActualizarEstadoLlamada(
                        callEvent.CallSid,
                        callEvent.CallStatus
                    );
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar webhook de llamada saliente");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }

        private bool ValidateTwilioSignature()
        {
            try
            {
                var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                var parameters = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());
                var signature = Request.Headers["X-Twilio-Signature"].ToString();

                if (string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("Petición sin firma de Twilio");
                    return false;
                }

                var validator = new RequestValidator(_twilioAuthToken);
                return validator.Validate(requestUrl, parameters, signature);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al validar firma de Twilio");
                return false;
            }
        }
    }

    public class TwilioCallStatusEvent
    {
        public string CallSid { get; set; } = string.Empty;
        public string CallStatus { get; set; } = string.Empty;
        public string? CallDuration { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
    }
}