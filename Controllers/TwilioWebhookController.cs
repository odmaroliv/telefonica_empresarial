using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/twilio")]

    public class TwilioWebhookController : ControllerBase
    {
        private readonly ILogger<TwilioWebhookController> _logger;

        public TwilioWebhookController(ILogger<TwilioWebhookController> logger)
        {
            _logger = logger;
        }


        [HttpPost("redirect")]
        [AllowAnonymous]
        public IActionResult RedirectCall([FromForm] string To = null, [FromForm] string From = null, [FromForm] string CallSid = null)
        {
            try
            {
                // Obtener el número de redirección
                string redirectTo = HttpContext.Request.Query["RedirectTo"].ToString();

                // Asegurarse de que el número tenga formato correcto (eliminar espacios)
                redirectTo = redirectTo.Replace(" ", "");
                if (!redirectTo.StartsWith("+"))
                {
                    redirectTo = "+" + redirectTo;
                }

                // Crear TwiML
                var response = new VoiceResponse();
                response.Say("Redirigiendo su llamada, por favor espere.", voice: "alice", language: "es-MX");

                // Usar el método Dial directamente para evitar el formato anidado
                response.Dial(redirectTo);

                string twiml = response.ToString();
                return Content(twiml, "application/xml");
            }
            catch (Exception ex)
            {
                // Manejar error
                var response = new VoiceResponse();
                response.Say("Ocurrió un error al procesar su llamada.", voice: "alice", language: "es-MX");
                return Content(response.ToString(), "application/xml");
            }
        }

        [HttpPost("voice")]
        public IActionResult HandleIncomingVoice([FromForm] string To, [FromForm] string From, [FromForm] string CallSid)
        {
            try
            {
                _logger.LogInformation($"Recibida llamada entrante: De={From}, A={To}");

                // Buscar en la base de datos el número de redirección
                // (Este endpoint se usará como alternativa si prefieres gestionar las redirecciones centralmente)

                var response = new VoiceResponse();
                response.Say("Este número no tiene configurada una redirección válida.", voice: "alice", language: "es-MX");

                return Content(response.ToString(), "application/xml");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar llamada entrante");

                var response = new VoiceResponse();
                response.Say("Lo sentimos, ha ocurrido un error al procesar su llamada.", voice: "alice", language: "es-MX");

                return Content(response.ToString(), "application/xml");
            }
        }
    }
}