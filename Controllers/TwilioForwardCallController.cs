using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using Twilio.Security;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/twilio")]
    public class TwilioForwardCallController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwilioForwardCallController> _logger;
        private readonly string _twilioAuthToken;
        private readonly ApplicationDbContext _context;

        public TwilioForwardCallController(
            IConfiguration configuration,
            ILogger<TwilioForwardCallController> logger,
             ApplicationDbContext context)
        {
            _configuration = configuration;
            _logger = logger;
            _context = context;
            _twilioAuthToken = _configuration["Twilio:AuthToken"] ?? throw new ArgumentNullException("Twilio:AuthToken no configurado");
        }

        /// <summary>
        /// Endpoint para realizar una llamada saliente desde nuestra plataforma a un número externo
        /// </summary>
        [HttpGet("forward-call")]
        [HttpPost("forward-call")]
        [AllowAnonymous]
        public IActionResult ForwardCall()
        {
            try
            {
                // Obtener los parámetros To y From
                string to = Request.Method == "GET"
                    ? Request.Query["To"].ToString()
                    : Request.Form["To"].ToString();

                string from = Request.Method == "GET"
                    ? Request.Query["From"].ToString()
                    : Request.Form["From"].ToString();

                _logger.LogInformation($"LLAMADA-DEBUG: Recibida solicitud para llamada: From={from}, To={to}, Método={Request.Method}");

                // Validar que no sean el mismo número
                if (EnsureE164Format(to) == EnsureE164Format(from))
                {
                    _logger.LogWarning($"LLAMADA-ERROR: Se intentó llamar al mismo número origen y destino: {to}");
                    var errorResponse = new VoiceResponse();
                    errorResponse.Say("Error en la configuración de la llamada. El origen y destino son iguales.", voice: "alice", language: "es-MX");
                    errorResponse.Hangup();
                    return Content(errorResponse.ToString(), "application/xml");
                }

                // Asegurar que los números tengan formato E.164
                string formattedFrom = EnsureE164Format(from);
                string formattedTo = EnsureE164Format(to);

                _logger.LogInformation($"LLAMADA-DEBUG: Números formateados: From={formattedFrom}, To={formattedTo}");

                // Generar TwiML para conectar la llamada
                var response = new VoiceResponse();

                // Opcionalmente, mensaje de bienvenida
                response.Say("Conectando su llamada.", voice: "alice", language: "es-MX");

                // Conectar al número destino
                var dial = new Dial(callerId: formattedFrom);
                dial.Number(formattedTo);
                response.Append(dial);

                string twiml = response.ToString();
                _logger.LogInformation($"LLAMADA-DEBUG: TwiML generado: {twiml}");

                return Content(twiml, "application/xml");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLAMADA-ERROR: Error al generar TwiML: {Message}", ex.Message);
                var errorResponse = new VoiceResponse();
                errorResponse.Say("Lo sentimos, ha ocurrido un error al procesar su llamada.", voice: "alice", language: "es-MX");
                errorResponse.Hangup();
                return Content(errorResponse.ToString(), "application/xml");
            }
        }

        [HttpGet("connect-call/{callId}")]
        [HttpPost("connect-call/{callId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ConnectCallWithId(int callId)
        {
            try
            {
                _logger.LogInformation($"LLAMADA-DEBUG: Conectando llamada con ID en ruta: {callId}");

                // Obtener los detalles de la llamada desde la base de datos
                var llamada = await _context.LlamadasSalientes
                    .Include(l => l.NumeroTelefonico)
                    .FirstOrDefaultAsync(l => l.Id == callId);

                if (llamada == null)
                {
                    _logger.LogWarning($"LLAMADA-DEBUG: No se encontró la llamada con ID {callId}");
                    var errorResponse = new VoiceResponse();
                    errorResponse.Say("No se encontró la información de la llamada. Por favor, intente nuevamente.", voice: "alice", language: "es-MX");
                    errorResponse.Hangup();
                    return Content(errorResponse.ToString(), "application/xml");
                }

                // Extraer los números origen y destino
                string numeroOrigen = llamada.NumeroTelefonico.Numero;
                string numeroDestino = llamada.NumeroDestino;

                _logger.LogInformation($"LLAMADA-DEBUG: Recuperados datos de llamada {callId}: Origen={numeroOrigen}, Destino={numeroDestino}");

                // Crear TwiML para conectar al destino final
                var response = new VoiceResponse();
                response.Say("Conectando con su destino. Por favor espere.", voice: "alice", language: "es-MX");

                var dial = new Dial(callerId: numeroOrigen);
                dial.Number(numeroDestino);
                response.Append(dial);

                string twiml = response.ToString();
                _logger.LogInformation($"LLAMADA-DEBUG: TwiML para conexión final: {twiml}");

                return Content(twiml, "application/xml");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLAMADA-ERROR: Error al conectar llamada final con ID en ruta");
                var errorResponse = new VoiceResponse();
                errorResponse.Say("Lo sentimos, ha ocurrido un error al conectar su llamada.", voice: "alice", language: "es-MX");
                errorResponse.Hangup();
                return Content(errorResponse.ToString(), "application/xml");
            }
        }
        // Método mejorado para formatear números según el país
        private string EnsureE164Format(string phoneNumber)
        {
            try
            {
                // Limpiar el número de espacios, guiones y paréntesis
                string cleaned = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());

                // Si no empieza con +, añadir uno
                if (!cleaned.StartsWith("+"))
                {
                    // Determinar el código de país basado en las primeras cifras
                    if (cleaned.StartsWith("1"))
                    {
                        // USA/Canadá
                        cleaned = "+" + cleaned;
                    }
                    else if (cleaned.StartsWith("52"))
                    {
                        // México
                        cleaned = "+" + cleaned;
                    }
                    else if (cleaned.StartsWith("34"))
                    {
                        // España
                        cleaned = "+" + cleaned;
                    }
                    else if (cleaned.Length >= 10)
                    {
                        // Si no tiene código de país pero tiene 10 dígitos, asumir que es México (por defecto)
                        cleaned = "+52" + cleaned;
                    }
                    else
                    {
                        // Si no podemos determinar el país, loguear y usar +52 como fallback
                        _logger.LogWarning($"No se pudo determinar el código de país para {phoneNumber}, usando +52 como fallback");
                        cleaned = "+52" + cleaned;
                    }
                }

                // Verificar longitud mínima para validez
                if (cleaned.Length < 8)
                {
                    _logger.LogWarning($"Número {cleaned} es demasiado corto para ser válido");
                }

                return cleaned;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al formatear número {phoneNumber}");
                return phoneNumber; // Retornar el original en caso de error
            }
        }

        private bool ValidateTwilioSignature()
        {
            try
            {
                // Determinar si es una solicitud inicial para generar TwiML
                // o un callback de Twilio
                var isInitialTwiMLRequest = Request.Method == "GET" &&
                                           Request.Path.Value?.EndsWith("/forward-call") == true;

                // Si es la solicitud inicial para generar TwiML, permitir sin firma
                if (isInitialTwiMLRequest)
                {
                    _logger.LogInformation("Permitiendo solicitud inicial para generar TwiML sin validar firma");
                    return true;
                }

                // Para otras solicitudes, validar normalmente
                var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                var parameters = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());
                var signature = Request.Headers["X-Twilio-Signature"].ToString();

                // En el modo de desarrollo o pruebas, podemos ser más permisivos con las firmas
                var environment = _configuration["Environment"] ?? "Development";
                if (environment == "Development" && string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("Modo desarrollo: aceptando petición sin firma de Twilio");
                    return true;
                }

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
}