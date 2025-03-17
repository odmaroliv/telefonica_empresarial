using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using System.Text.RegularExpressions;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Services;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace TelefonicaEmpresaria.Services
{
    /// <summary>
    /// Interfaz para el servicio de llamadas salientes
    /// </summary>
    public interface ILlamadasService
    {
        Task FinalizarLlamadasAbandonadas();
        Task ProcesarFinalizacionLlamada(string twilioCallSid, int? duracion);
        Task ActualizarHeartbeat(int llamadaId, string userId);
        /// <summary>
        /// Inicia una llamada desde un número del usuario a un destinatario
        /// </summary>
        /// <param name="numeroTelefonicoId">ID del número desde el que se hará la llamada</param>
        /// <param name="numeroDestino">Número de teléfono destino (formato E.164: +521234567890)</param>
        /// <param name="userId">ID del usuario que realiza la llamada</param>
        /// <returns>Información de la llamada iniciada o error</returns>
        Task<(LlamadaSaliente Llamada, string Error)> IniciarLlamada(int numeroTelefonicoId, string numeroDestino, string userId);

        /// <summary>
        /// Obtiene el estado actual de una llamada
        /// </summary>
        /// <param name="llamadaId">ID de la llamada</param>
        /// <param name="userId">ID del usuario propietario de la llamada</param>
        /// <returns>Estado actual de la llamada o error</returns>
        Task<(LlamadaSaliente Llamada, string Error)> ObtenerEstadoLlamada(int llamadaId, string userId);

        /// <summary>
        /// Finaliza una llamada en curso
        /// </summary>
        /// <param name="llamadaId">ID de la llamada a finalizar</param>
        /// <param name="userId">ID del usuario propietario de la llamada</param>
        /// <returns>True si se finalizó correctamente, False en caso contrario</returns>
        Task<bool> FinalizarLlamada(int llamadaId, string userId);

        /// <summary>
        /// Actualiza el estado de una llamada basado en webhook de Twilio
        /// </summary>
        /// <param name="twilioCallSid">SID de la llamada en Twilio</param>
        /// <param name="estado">Nuevo estado de la llamada</param>
        /// <param name="duracion">Duración en segundos (si está disponible)</param>
        /// <returns>True si se actualizó correctamente, False en caso contrario</returns>
        Task<bool> ActualizarEstadoLlamada(string twilioCallSid, string estado, int? duracion = null);

        /// <summary>
        /// Calcula el costo estimado de una llamada
        /// </summary>
        /// <param name="numeroOrigen">Número desde el que se realiza la llamada</param>
        /// <param name="numeroDestino">Número al que se llama</param>
        /// <param name="duracionEstimadaMinutos">Duración estimada en minutos</param>
        /// <returns>Costo estimado de la llamada</returns>
        Task<decimal> CalcularCostoEstimadoLlamada(string numeroOrigen, string numeroDestino, int duracionEstimadaMinutos);

        /// <summary>
        /// Obtiene el historial de llamadas salientes de un usuario
        /// </summary>
        /// <param name="userId">ID del usuario</param>
        /// <param name="limite">Cantidad máxima de registros a retornar</param>
        /// <returns>Lista de llamadas salientes</returns>
        Task<List<LlamadaSaliente>> ObtenerHistorialLlamadas(string userId, int limite = 50);
    }


    /// <summary>
    /// Implementación del servicio de llamadas salientes
    /// </summary>
    public class LlamadasService : ILlamadasService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LlamadasService> _logger;
        private readonly ISaldoService _saldoService;
        private readonly IValidationService _validationService;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly string _twilioAccountSid;
        private readonly string _twilioAuthToken;
        private readonly string _appUrl;


        public LlamadasService(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<LlamadasService> logger,
            ISaldoService saldoService,
            IValidationService validationService)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _saldoService = saldoService;
            _validationService = validationService;

            // Inicializar credenciales de Twilio
            _twilioAccountSid = _configuration["Twilio:AccountSid"] ?? throw new ArgumentNullException("Twilio:AccountSid");
            _twilioAuthToken = _configuration["Twilio:AuthToken"] ?? throw new ArgumentNullException("Twilio:AuthToken");
            _appUrl = _configuration["AppUrl"] ?? "https://localhost:7019";

            // Configurar política de reintentos
            _retryPolicy = Polly.Policy
                .Handle<ApiException>(ex => ex.Status != 400) // No reintentar errores de validación
                .Or<TaskCanceledException>()
                .Or<HttpRequestException>()
                .WaitAndRetryAsync(
                    3, // Número de reintentos
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning($"Error al comunicarse con Twilio (intento {retryCount}): {exception.Message}. Reintentando en {timeSpan.TotalSeconds} segundos.");
                    }
                );

            // Inicializar el cliente de Twilio
            TwilioClient.Init(_twilioAccountSid, _twilioAuthToken);
        }

        public async Task ActualizarHeartbeat(int llamadaId, string userId)
        {
            try
            {
                var llamada = await _context.LlamadasSalientes
                    .FirstOrDefaultAsync(l => l.Id == llamadaId && l.UserId == userId);

                if (llamada != null && llamada.Estado == "en-curso")
                {
                    llamada.UltimoHeartbeat = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar heartbeat para llamada {llamadaId}");
            }
        }

        /// <summary>
        /// Inicia una llamada desde un número del usuario a un destinatario
        /// </summary>
        public async Task<(LlamadaSaliente Llamada, string Error)> IniciarLlamada(int numeroTelefonicoId, string numeroDestino, string userId)
        {
            try
            {
                _logger.LogInformation($"Iniciando llamada desde número ID {numeroTelefonicoId} a {numeroDestino} por usuario {userId}");

                // Validar el formato del número destino
                if (!_validationService.IsValidPhoneNumber(numeroDestino))
                {
                    return (null, "El número de destino no tiene un formato válido. Debe estar en formato E.164 (ej: +521234567890)");
                }

                // Obtener el número telefónico del usuario
                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.Id == numeroTelefonicoId && n.UserId == userId);

                if (numeroTelefonico == null)
                {
                    return (null, "No se encontró el número telefónico especificado o no pertenece al usuario");
                }

                if (!numeroTelefonico.Activo)
                {
                    return (null, "El número telefónico no está activo");
                }

                // Verificar saldo disponible para al menos 1 minuto de llamada
                decimal costoEstimado = await CalcularCostoEstimadoLlamada(numeroTelefonico.Numero, numeroDestino, 1);
                bool saldoSuficiente = await _saldoService.VerificarSaldoSuficiente(userId, costoEstimado);

                if (!saldoSuficiente)
                {
                    return (null, $"Saldo insuficiente para realizar la llamada. Se requiere al menos ${costoEstimado}");
                }

                // Registrar la llamada en la base de datos
                var llamada = new LlamadaSaliente
                {
                    UserId = userId,
                    NumeroTelefonicoId = numeroTelefonicoId,
                    NumeroDestino = numeroDestino,
                    FechaInicio = DateTime.UtcNow,
                    Estado = "iniciando"
                };

                _context.LlamadasSalientes.Add(llamada);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Llamada registrada con ID {llamada.Id}");

                // Iniciar la llamada con Twilio usando el número del usuario como caller ID
                string statusCallbackUrl = $"{_appUrl}/api/webhooks/twilio/llamada-saliente";

                try
                {
                    var twilioCall = await _retryPolicy.ExecuteAsync(async () =>
                    {
                        // Obtener el número de redirección configurado para este número Twilio
                        string numeroRedireccion = numeroTelefonico.NumeroRedireccion;

                        // Si no hay número de redirección configurado, mostrar un error
                        if (string.IsNullOrEmpty(numeroRedireccion))
                        {
                            throw new Exception("No hay número de redirección configurado para este número telefónico");
                        }

                        _logger.LogInformation($"Iniciando click-to-call: Desde={numeroTelefonico.Numero} a redirección={numeroRedireccion} para llamar a destino={numeroDestino}");

                        // Usar una URL con el ID de llamada directamente en la ruta
                        var twimlUrl = new Uri($"{_appUrl}/api/twilio/connect-call/{llamada.Id}");

                        _logger.LogInformation($"URL de TwiML generada: {twimlUrl}");

                        // Iniciar la llamada a tu teléfono primero
                        return await CallResource.CreateAsync(
                            to: new PhoneNumber(numeroRedireccion),
                            from: new PhoneNumber(numeroTelefonico.Numero),
                            url: twimlUrl,
                            statusCallback: new Uri(statusCallbackUrl),
                            statusCallbackMethod: Twilio.Http.HttpMethod.Post,
                            statusCallbackEvent: new List<string> { "initiated", "ringing", "answered", "completed" }
                        );
                    });

                    // Actualizar la llamada con el SID de Twilio
                    if (twilioCall != null)
                    {
                        llamada.TwilioCallSid = twilioCall.Sid;
                        llamada.Estado = "en-curso";
                        await _context.SaveChangesAsync();

                        _logger.LogInformation($"Llamada iniciada con éxito. Twilio SID: {twilioCall.Sid}");
                        return (llamada, string.Empty);
                    }
                    else
                    {
                        throw new Exception("Respuesta nula de Twilio al iniciar la llamada");
                    }
                }
                catch (Exception ex)
                {
                    // Marcar la llamada como fallida
                    llamada.Estado = "fallida";
                    llamada.Detalles = $"Error al iniciar la llamada: {ex.Message}";
                    await _context.SaveChangesAsync();

                    _logger.LogError(ex, $"Error al iniciar llamada {llamada.Id} con Twilio");
                    return (llamada, $"Error al iniciar la llamada: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error general al procesar solicitud de llamada");
                return (null, $"Error al procesar la solicitud: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene el estado actual de una llamada
        /// </summary>
        public async Task<(LlamadaSaliente Llamada, string Error)> ObtenerEstadoLlamada(int llamadaId, string userId)
        {
            try
            {
                // Obtener la llamada de la base de datos
                var llamada = await _context.LlamadasSalientes
                    .Include(l => l.NumeroTelefonico)
                    .FirstOrDefaultAsync(l => l.Id == llamadaId && l.UserId == userId);

                if (llamada == null)
                {
                    return (null, "No se encontró la llamada especificada o no pertenece al usuario");
                }

                // Si la llamada está en curso y tiene un SID de Twilio, verificar su estado actual
                if (llamada.Estado == "en-curso" && !string.IsNullOrEmpty(llamada.TwilioCallSid))
                {
                    try
                    {
                        var twilioCall = await _retryPolicy.ExecuteAsync(async () =>
                            await CallResource.FetchAsync(pathSid: llamada.TwilioCallSid)
                        );

                        if (twilioCall != null)
                        {
                            // Actualizar el estado según Twilio
                            await ActualizarEstadoLlamadaDesdeTwilio(
                                     llamada,
                                     twilioCall.Status.ToString().ToLower(), // convertir el enum a string según tu definición
                                     int.TryParse(twilioCall.Duration, out var segundos) ? segundos : null // convertir duration (string) a int?
                                 );

                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"No se pudo obtener el estado actualizado de Twilio para la llamada {llamadaId}");
                        // Continuamos con el estado guardado en la base de datos
                    }
                }

                return (llamada, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener estado de llamada {llamadaId}");
                return (null, $"Error al obtener estado de la llamada: {ex.Message}");
            }
        }

        /// <summary>
        /// Finaliza una llamada en curso
        /// </summary>
        // Método público que acepta solicitudes de finalización desde UI o jobs
        public async Task<bool> FinalizarLlamada(int llamadaId, string userId)
        {
            try
            {
                // Verificar si la llamada ya está finalizada
                var llamada = await _context.LlamadasSalientes
                    .FirstOrDefaultAsync(l => l.Id == llamadaId && l.UserId == userId);

                if (llamada == null)
                {
                    _logger.LogWarning($"Intento de finalizar llamada inexistente {llamadaId}");
                    return false;
                }

                // Si la llamada ya está finalizada, simplemente retornar éxito
                if (llamada.Estado == "completada" || llamada.Estado == "fallida" ||
                    llamada.Estado == "cancelada")
                {
                    _logger.LogInformation($"La llamada {llamadaId} ya está finalizada con estado {llamada.Estado}");
                    return true;
                }

                // Continuar con la finalización
                return await FinalizarLlamadaInterna(llamadaId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al finalizar llamada {llamadaId}");
                return false;
            }
        }


        public async Task ProcesarFinalizacionLlamada(string twilioCallSid, int? duracion)
        {
            try
            {
                var llamada = await _context.LlamadasSalientes
                    .FirstOrDefaultAsync(l => l.TwilioCallSid == twilioCallSid);

                if (llamada == null)
                {
                    _logger.LogWarning($"No se encontró llamada con SID: {twilioCallSid}");
                    return;
                }

                // Determinar el estado final adecuado
                // Si está en "finalizando", significa que el usuario la terminó manualmente
                string estadoFinal = llamada.Estado == "finalizando" ? "finalizada_usuario" : "completada";

                // Actualizar estado y duración reportada por Twilio
                llamada.Estado = estadoFinal;
                llamada.FechaFin = DateTime.UtcNow;
                llamada.Duracion = duracion;

                // Calcular costo basado en la duración real reportada
                if (duracion.HasValue && duracion.Value > 0)
                {

                    // Obtener el país destino del número
                    string paisDestino = ObtenerPaisDesdeNumero(llamada.NumeroDestino);

                    // Calcular el costo usando el método asíncrono
                    decimal costo = await CalcularCostoLlamadaReal(paisDestino, duracion.Value);
                    llamada.Costo = costo;

                    // Registrar consumo en el saldo
                    await ProcesarConsumoLlamada(llamada);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar finalización de llamada con SID {twilioCallSid}");
            }
        }

        // Método interno que realiza la finalización real
        private async Task<bool> FinalizarLlamadaInterna(int llamadaId, string userId)
        {
            try
            {
                // Obtener la llamada
                var llamada = await _context.LlamadasSalientes
                    .FirstOrDefaultAsync(l => l.Id == llamadaId && l.UserId == userId);

                if (llamada == null || llamada.Estado != "en-curso")
                {
                    return false;
                }

                // Si tiene CallSid, intentar finalizarla en Twilio
                if (!string.IsNullOrEmpty(llamada.TwilioCallSid))
                {
                    try
                    {
                        await _retryPolicy.ExecuteAsync(async () =>
                            await CallResource.UpdateAsync(
                                pathSid: llamada.TwilioCallSid,
                                status: "completed"
                            )
                        );

                        _logger.LogInformation($"Llamada {llamadaId} finalizada en Twilio");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al finalizar llamada {llamadaId} en Twilio");
                        // Continuamos para actualizar estado en BD
                    }
                }

                // Marcar la llamada como "finalizando" en la base de datos
                // Este es un estado transitorio mientras esperamos el webhook de Twilio
                llamada.Estado = "finalizando";
                llamada.FechaFin = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en FinalizarLlamadaInterna para llamada {llamadaId}");
                return false;
            }
        }

        /// <summary>
        /// Actualiza el estado de una llamada basado en webhook de Twilio
        /// </summary>
        public async Task<bool> ActualizarEstadoLlamada(string twilioCallSid, string estado, int? duracion = null)
        {
            try
            {
                if (string.IsNullOrEmpty(twilioCallSid))
                {
                    _logger.LogWarning("Intento de actualizar llamada con SID nulo");
                    return false;
                }

                // Buscar la llamada por el SID de Twilio
                var llamada = await _context.LlamadasSalientes
                    .FirstOrDefaultAsync(l => l.TwilioCallSid == twilioCallSid);

                if (llamada == null)
                {
                    _logger.LogWarning($"No se encontró llamada con SID de Twilio: {twilioCallSid}");
                    return false;
                }

                // Actualizar el estado según la información de Twilio
                return await ActualizarEstadoLlamadaDesdeTwilio(llamada, estado, duracion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar estado de llamada con SID {twilioCallSid}");
                return false;
            }
        }

        /// <summary>
        /// Calcula el costo estimado de una llamada
        /// </summary>
        public async Task<decimal> CalcularCostoEstimadoLlamada(string numeroOrigen, string numeroDestino, int duracionEstimadaMinutos)
        {
            try
            {
                // Determinar el país de destino basado en el prefijo
                string paisDestino = ObtenerPaisDesdeNumero(numeroDestino);

                // Obtener las tarifas base según el país
                var tarifas = ObtenerTarifasBase();
                decimal tarifaMinuto = tarifas.ContainsKey(paisDestino)
                    ? tarifas[paisDestino]
                    : tarifas["default"];

                // Convertir USD a MXN (si es necesario)
                decimal tipoCambioUSDMXN = 20.0m; // Esta tarifa debería venir de un servicio externo o configuración
                tarifaMinuto = tarifaMinuto * tipoCambioUSDMXN;

                // Obtener configuraciones de margen e IVA
                var configuraciones = await _context.ConfiguracionesSistema
                    .Where(c => c.Clave == "MargenGananciaLlamadas" || c.Clave == "IVA")
                    .ToDictionaryAsync(c => c.Clave, c => decimal.Parse(c.Valor));

                decimal margenLlamadas = configuraciones.ContainsKey("MargenGananciaLlamadas")
                    ? configuraciones["MargenGananciaLlamadas"]
                    : 4.0m; // 400% por defecto

                decimal iva = configuraciones.ContainsKey("IVA")
                    ? configuraciones["IVA"]
                    : 0.16m; // 16% por defecto

                // Calcular el costo estimado con margen e IVA
                decimal costoEstimado = tarifaMinuto * duracionEstimadaMinutos * (1 + margenLlamadas) * (1 + iva);

                // Asegurar un costo mínimo
                const decimal costoMinimo = 5.0m;
                return Math.Max(costoEstimado, costoMinimo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular costo estimado de llamada");
                // Valor de fallback
                return 20.0m * duracionEstimadaMinutos; // Estimado conservador
            }
        }

        /// <summary>
        /// Obtiene el historial de llamadas salientes de un usuario
        /// </summary>
        public async Task<List<LlamadaSaliente>> ObtenerHistorialLlamadas(string userId, int limite = 50)
        {
            try
            {
                return await _context.LlamadasSalientes
                    .Where(l => l.UserId == userId)
                    .OrderByDescending(l => l.FechaInicio)
                    .Take(limite)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener historial de llamadas para usuario {userId}");
                return new List<LlamadaSaliente>();
            }
        }

        #region Métodos privados auxiliares

        /// <summary>
        /// Actualiza el estado de una llamada con la información de Twilio
        /// </summary>
        private async Task<bool> ActualizarEstadoLlamadaDesdeTwilio(LlamadaSaliente llamada, string estadoTwilio, int? duracion)
        {
            try
            {
                // Mapear el estado de Twilio a nuestro modelo
                string nuevoEstado = MapearEstadoTwilio(estadoTwilio);
                llamada.Estado = nuevoEstado;

                // Si la llamada ha finalizado, actualizar la fecha de fin y la duración
                if (nuevoEstado == "completada" || nuevoEstado == "fallida" || nuevoEstado == "cancelada")
                {
                    llamada.FechaFin = DateTime.UtcNow;
                    llamada.Duracion = duracion ?? (int)(llamada.FechaFin.Value - llamada.FechaInicio).TotalSeconds;

                    // Procesar el consumo si no se ha hecho ya
                    if (!llamada.ConsumoRegistrado)
                    {
                        await ProcesarConsumoLlamada(llamada);
                    }
                }

                // Guardar los cambios
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar estado de llamada {llamada.Id} desde Twilio");
                return false;
            }
        }

        /// <summary>
        /// Mapea el estado de Twilio a nuestro modelo
        /// </summary>
        private string MapearEstadoTwilio(string estadoTwilio)
        {
            return estadoTwilio.ToLower() switch
            {
                "queued" => "iniciando",
                "ringing" => "en-curso",
                "in-progress" => "en-curso",
                "completed" => "completada",
                "busy" => "fallida",
                "failed" => "fallida",
                "no-answer" => "fallida",
                "canceled" => "cancelada",
                _ => "en-curso"
            };
        }

        /// <summary>
        /// Procesa el consumo de una llamada finalizada
        /// </summary>
        private async Task<bool> ProcesarConsumoLlamada(LlamadaSaliente llamada)
        {
            try
            {
                // Si ya se procesó el consumo, no hacerlo de nuevo
                if (llamada.ConsumoRegistrado)
                {
                    return true;
                }

                // Si la llamada no está finalizada o no tiene duración, no procesar
                if (llamada.Estado != "completada" && llamada.Estado != "fallida" && llamada.Estado != "cancelada")
                {
                    return false;
                }

                if (!llamada.FechaFin.HasValue || !llamada.Duracion.HasValue)
                {
                    return false;
                }

                // Obtener el número de origen
                var numeroOrigen = await _context.NumerosTelefonicos
                    .FindAsync(llamada.NumeroTelefonicoId);

                if (numeroOrigen == null)
                {
                    _logger.LogError($"No se encontró el número de origen para procesar consumo de llamada {llamada.Id}");
                    return false;
                }

                // Calcular el costo de la llamada
                string paisDestino = ObtenerPaisDesdeNumero(llamada.NumeroDestino);
                decimal costoLlamada = await CalcularCostoLlamadaReal(paisDestino, llamada.Duracion.Value);

                // Registrar el costo
                llamada.Costo = costoLlamada;

                // Descontar del saldo solo si la llamada tuvo duración
                if (llamada.Duracion > 0 && costoLlamada > 0)
                {
                    // Formatear la duración para el concepto
                    TimeSpan duracion = TimeSpan.FromSeconds(llamada.Duracion.Value);
                    string duracionFormateada = duracion.Minutes > 0
                        ? $"{duracion.Minutes}:{duracion.Seconds:D2} min"
                        : $"{duracion.Seconds} seg";

                    // Concepto para el movimiento
                    string concepto = $"Llamada a {llamada.NumeroDestino} ({duracionFormateada}) - ID:{llamada.Id}";

                    bool saldoDescontado = await _saldoService.DescontarSaldo(
                        llamada.UserId,
                        costoLlamada,
                        concepto,
                        llamada.NumeroTelefonicoId
                    );

                    if (!saldoDescontado)
                    {
                        _logger.LogWarning($"No se pudo descontar saldo para la llamada {llamada.Id}");
                        // Continuamos a pesar del error
                    }
                }

                // Marcar la llamada como procesada
                llamada.ConsumoRegistrado = true;
                llamada.FechaProcesamientoConsumo = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Consumo procesado para llamada {llamada.Id}: ${llamada.Costo}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar consumo de llamada {llamada.Id}");
                return false;
            }
        }

        /// <summary>
        /// Calcula el costo real de una llamada completada
        /// </summary>
        private async Task<decimal> CalcularCostoLlamadaReal(string paisDestino, int duracionSegundos)
        {
            try
            {
                // Obtener las tarifas base
                var tarifas = ObtenerTarifasBase();
                decimal tarifaMinuto = tarifas.ContainsKey(paisDestino)
                    ? tarifas[paisDestino]
                    : tarifas["default"];

                // Convertir USD a MXN (si es necesario)
                decimal tipoCambioUSDMXN = 20.0m; // Esta tarifa debería venir de un servicio externo o configuración
                tarifaMinuto = tarifaMinuto * tipoCambioUSDMXN;

                // Obtener configuraciones de margen e IVA
                var configuraciones = await _context.ConfiguracionesSistema
                    .Where(c => c.Clave == "MargenGananciaLlamadas" || c.Clave == "IVA")
                    .ToDictionaryAsync(c => c.Clave, c => decimal.Parse(c.Valor));

                decimal margenLlamadas = configuraciones.ContainsKey("MargenGananciaLlamadas")
                    ? configuraciones["MargenGananciaLlamadas"]
                    : 4.0m; // 400% por defecto

                decimal iva = configuraciones.ContainsKey("IVA")
                    ? configuraciones["IVA"]
                    : 0.16m; // 16% por defecto

                decimal costoFinal;

                // Para llamadas menores a 60 segundos, cobrar proporcionalmente, pero con un mínimo
                if (duracionSegundos < 60)
                {
                    decimal fraccionMinuto = (decimal)duracionSegundos / 60;
                    costoFinal = tarifaMinuto * fraccionMinuto * (1 + margenLlamadas) * (1 + iva);

                    // Establecer un costo mínimo para llamadas muy cortas
                    decimal costoMinimo = 5.0m;
                    costoFinal = Math.Max(costoFinal, costoMinimo);
                }
                else
                {
                    // Para llamadas de más de 1 minuto, redondear hacia arriba
                    decimal minutos = Math.Ceiling((decimal)duracionSegundos / 60);
                    costoFinal = tarifaMinuto * minutos * (1 + margenLlamadas) * (1 + iva);
                }

                return Math.Ceiling(costoFinal); // Redondear hacia arriba al peso completo
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular costo real de llamada");
                // Valor de fallback
                return Math.Ceiling((decimal)duracionSegundos / 60) * 10.0m; // 10 pesos por minuto como fallback
            }
        }

        /// <summary>
        /// Obtiene un diccionario con las tarifas base por minuto para diferentes países (en USD)
        /// </summary>
        private Dictionary<string, decimal> ObtenerTarifasBase()
        {
            // Estas tarifas deberían venir de una tabla en la base de datos o un servicio externo
            return new Dictionary<string, decimal>
            {
                { "MX", 0.013m }, // 0.013 USD por minuto en México
                { "US", 0.015m }, // 0.015 USD por minuto en EE.UU.
                { "CA", 0.015m }, // 0.015 USD por minuto en Canadá
                { "ES", 0.025m }, // 0.025 USD por minuto en España
                { "CO", 0.019m }, // 0.019 USD por minuto en Colombia
                { "AR", 0.019m }, // 0.019 USD por minuto en Argentina
                { "CL", 0.018m }, // 0.018 USD por minuto en Chile
                { "BR", 0.022m }, // 0.022 USD por minuto en Brasil
                { "GB", 0.020m }, // 0.020 USD por minuto en Reino Unido
                { "DE", 0.018m }, // 0.018 USD por minuto en Alemania
                { "FR", 0.018m }, // 0.018 USD por minuto en Francia
                { "IT", 0.018m }, // 0.018 USD por minuto en Italia
                { "default", 0.030m } // 0.030 USD por minuto para otros países
            };
        }

        /// <summary>
        /// Extrae el código de país desde un número telefónico en formato E.164
        /// </summary>
        private string ObtenerPaisDesdeNumero(string numeroTelefono)
        {
            if (string.IsNullOrEmpty(numeroTelefono) || !numeroTelefono.StartsWith("+"))
            {
                return "default";
            }

            // Mapa de prefijos a códigos de país
            var prefijosPaises = new Dictionary<string, string>
            {
                { "+1", "US" },     // EEUU/Canadá
                { "+52", "MX" },    // México
                { "+34", "ES" },    // España
                { "+57", "CO" },    // Colombia
                { "+54", "AR" },    // Argentina
                { "+56", "CL" },    // Chile
                { "+55", "BR" },    // Brasil
                { "+44", "GB" },    // Reino Unido
                { "+49", "DE" },    // Alemania
                { "+33", "FR" },    // Francia
                { "+39", "IT" }     // Italia
            };

            // Verificar prefijos conocidos
            foreach (var prefijo in prefijosPaises.Keys)
            {
                if (numeroTelefono.StartsWith(prefijo))
                {
                    return prefijosPaises[prefijo];
                }
            }

            // Si no se encontró un prefijo conocido, intentar extraer el código de país
            // según las reglas de E.164 (complicado y no totalmente preciso)
            try
            {
                // Usamos una expresión regular para extraer el código de país
                var match = Regex.Match(numeroTelefono, @"^\+(\d{1,3})");
                if (match.Success)
                {
                    string codigoPais = match.Groups[1].Value;
                    // Aquí podríamos tener una tabla más completa de códigos de país
                    return "default"; // Por ahora retornamos default
                }
            }
            catch
            {
                // En caso de error en la regex, retornar default
            }

            return "default";
        }
        public async Task FinalizarLlamadasAbandonadas()
        {
            try
            {
                // Buscar llamadas activas sin heartbeat reciente
                var llamadasAbandonadas = await _context.LlamadasSalientes
                    .Where(l => l.Estado == "en-curso" &&
                           l.UltimoHeartbeat.HasValue &&
                           l.UltimoHeartbeat.Value.AddSeconds(30) < DateTime.UtcNow)
                    .ToListAsync();

                _logger.LogInformation($"Encontradas {llamadasAbandonadas.Count} llamadas abandonadas");

                foreach (var llamada in llamadasAbandonadas)
                {
                    _logger.LogInformation($"Finalizando automáticamente llamada abandonada: {llamada.Id}");
                    await FinalizarLlamadaInterna(llamada.Id, llamada.UserId);
                }

                // También finalizar llamadas que excedan el tiempo máximo (30 minutos)
                var llamadasLargas = await _context.LlamadasSalientes
                    .Where(l => l.Estado == "en-curso" &&
                              l.FechaInicio.AddMinutes(30) < DateTime.UtcNow)
                    .ToListAsync();

                _logger.LogInformation($"Encontradas {llamadasLargas.Count} llamadas que exceden tiempo máximo");

                foreach (var llamada in llamadasLargas)
                {
                    _logger.LogInformation($"Finalizando automáticamente llamada larga: {llamada.Id}");
                    await FinalizarLlamadaInterna(llamada.Id, llamada.UserId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al finalizar llamadas abandonadas");
            }
        }

        #endregion
    }

}
