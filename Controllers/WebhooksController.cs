using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;
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
            string json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            string signatureHeader = Request.Headers["Stripe-Signature"];

            if (string.IsNullOrEmpty(signatureHeader))
            {
                _logger.LogWarning("Webhook de Stripe recibido sin encabezado de firma");
                return Unauthorized("Firma requerida");
            }

            try
            {
                // Establecer tiempo máximo de procesamiento para el webhook
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25)); // Asegura que el webhook responda en menos de 30 segundos

                // Extraer el ID del evento para logging (sin verificar firma aún)
                string eventId = "desconocido";
                try
                {
                    var eventObject = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (eventObject != null && eventObject.TryGetValue("id", out var id))
                    {
                        eventId = id.ToString() ?? "desconocido";
                    }
                }
                catch
                {
                    // Ignorar errores al intentar extraer el ID
                }

                _logger.LogInformation($"Webhook de Stripe recibido: {eventId}");

                // Procesar el evento
                await _stripeService.ProcesarEventoWebhook(json, signatureHeader, timeoutCts.Token);

                _logger.LogInformation($"Webhook de Stripe procesado correctamente: {eventId}");
                return Ok();
            }
            catch (StripeException ex) when (ex.StripeError?.Type == "signature_verification_failure")
            {
                _logger.LogWarning(ex, "Intento de webhook con firma inválida");
                return Unauthorized("Firma inválida");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Procesamiento del webhook de Stripe cancelado por timeout");
                // Para Stripe, debemos devolver 200 incluso si hubo timeout, para evitar reintentos innecesarios
                // El procesamiento continuará en segundo plano
                return Ok("Procesamiento en curso");
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al procesar webhook de Stripe");
                // Stripe reintentará automáticamente, así que devolvemos 200 para evitar reintentos innecesarios
                return Ok("Conflicto de concurrencia, reintentando");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no manejado al procesar webhook de Stripe");

                // Para errores temporales, permitir que Stripe reintente
                if (ex is TimeoutException ||
                    ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(500, new { error = "Error temporal, reintentar" });
                }

                // Para errores permanentes, devolver 200 para que Stripe no reintente
                return Ok("Error procesado");
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
                var signature = Request.Headers["X-Twilio-Signature"].ToString();

                if (string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("Webhook de Twilio recibido sin firma");
                    return Unauthorized("Firma requerida");
                }

                var validator = new RequestValidator(_twilioAuthToken);
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
                var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                var parameters = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());
                var signature = Request.Headers["X-Twilio-Signature"].ToString();

                if (string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("Webhook de SMS recibido sin firma");
                    return Unauthorized("Firma requerida");
                }

                var validator = new RequestValidator(_twilioAuthToken);
                if (!validator.Validate(requestUrl, parameters, signature))
                {
                    _logger.LogWarning("Firma Twilio inválida para SMS webhook");
                    return Unauthorized("Firma Twilio inválida");
                }

                // Buscar el número en nuestra base de datos
                var numeroTelefonico = await _context.NumerosTelefonicos
                    .Include(n => n.Usuario)
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

                    // Procesar el costo del SMS y descontar del saldo
                    await ProcesarConsumoSMS(numeroTelefonico, log);

                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"SMS procesado para número {numeroTelefonico.Numero} desde {evento.From}");
                }
                else
                {
                    _logger.LogWarning($"SMS recibido para número no válido o sin SMS habilitado: {evento.To}");
                }

                // Responder con TwiML vacío
                return Content(
                    "<Response></Response>",
                    "application/xml"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar webhook de SMS");
                // Aún devolvemos OK para no generar reintentos de Twilio
                return Content(
                    "<Response></Response>",
                    "application/xml"
                );
            }
        }

        // Método para procesar el consumo de SMS
        private async Task ProcesarConsumoSMS(NumeroTelefonico numero, LogSMS logSMS)
        {
            try
            {
                // Detectar si el SMS es un código de verificación o autenticación
                bool esSMSAutenticacion = DetectarSMSAutenticacion(logSMS.Mensaje);
                string tipo = esSMSAutenticacion ? "autenticación" : "regular";

                // Obtener el servicio de saldo
                var saldoService = HttpContext.RequestServices.GetRequiredService<ISaldoService>();

                // Calcular costo según el tipo
                decimal costo = await CalcularCostoSMS(tipo);

                // Crear concepto descriptivo
                string concepto = $"Recepción de SMS ({tipo}) de {logSMS.NumeroOrigen.Substring(0, Math.Min(8, logSMS.NumeroOrigen.Length))}...";

                // Descontar del saldo
                bool resultado = await saldoService.DescontarSaldo(
                    numero.UserId,
                    costo,
                    concepto,
                    numero.Id);

                if (!resultado)
                {
                    _logger.LogWarning($"No se pudo descontar saldo para SMS del número {numero.Id}");
                    // Continuar procesando el SMS aunque no se pueda descontar saldo
                }
                else
                {
                    _logger.LogInformation($"Saldo descontado: {costo} por SMS de {tipo}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar consumo de SMS para número {numero.Id}");
                // Continuar para no interrumpir el flujo del webhook
            }
        }

        // Método para detectar si un SMS es de autenticación
        private bool DetectarSMSAutenticacion(string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje))
                return false;

            // Patrones comunes en SMS de autenticación
            var patronesAutenticacion = new[]
            {
        // Códigos de verificación
        @"\b(verification|verify|código|codigo|authentication|autenticación|code)\b",
        // Patrones de códigos numéricos de 4-8 dígitos
        @"\b\d{4,8}\b",
        // Frases comunes de servicios
        @"\b(one-time|password|contraseña|PIN|OTP)\b",
        // Servicios conocidos
        @"\b(Google|Apple|Microsoft|Facebook|Twitter|WhatsApp|Amazon|PayPal)\b"
    };

            // Normalizar el mensaje a minúsculas para comparación
            var mensajeLower = mensaje.ToLower();

            // Verificar si el mensaje coincide con algún patrón
            foreach (var patron in patronesAutenticacion)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(mensajeLower, patron, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Método para calcular el costo de un SMS según su tipo
        private async Task<decimal> CalcularCostoSMS(string tipo)
        {
            try
            {
                // Obtener la configuración del sistema
                var dbContext = HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

                // Obtener costo base de SMS desde Twilio
                var saldoService = HttpContext.RequestServices.GetRequiredService<ISaldoService>();
                var twilioService = HttpContext.RequestServices.GetRequiredService<ITwilioService>();
                var costoBaseSMS = await twilioService.ObtenerCostoSMS();

                // Obtener configuraciones
                var configuraciones = await dbContext.ConfiguracionesSistema
                    .Where(c => c.Clave == "MargenGananciaSMS" || c.Clave == "IVA")
                    .ToDictionaryAsync(c => c.Clave, c => decimal.Parse(c.Valor));

                // Obtener valores con defaults
                decimal margenSMS = configuraciones.TryGetValue("MargenGananciaSMS", out var margen) ? margen : 3.5m;
                decimal iva = configuraciones.TryGetValue("IVA", out var ivaVal) ? ivaVal : 0.16m;

                // Multiplicador adicional según tipo de SMS
                decimal multiplicador = tipo == "autenticación" ? 5.0m : 1.0m;

                // Calcular costo con margen, multiplicador e IVA

                decimal costo = costoBaseSMS * (1 + margenSMS) * multiplicador * (1 + iva);

                // Establecer mínimos rentables
                decimal costoMinimo = tipo == "autenticación" ? 8.0m : 2.0m;

                // Tomar el mayor entre el calculado y el mínimo
                decimal costoFinal = Math.Max(costo, costoMinimo);

                // Redondear hacia arriba para maximizar ganancias
                return Math.Ceiling(costoFinal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular costo de SMS");

                // Valores por defecto en caso de error
                decimal costoDefault = tipo == "autenticación" ? 8.0m : 2.0m;
                return costoDefault;
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