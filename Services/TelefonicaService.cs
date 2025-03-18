using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Services
{
    public interface ITelefonicaService
    {


        Task<(NumeroTelefonico? Numero, string Error)> ComprarNumeroConPeriodo(
                ApplicationUser usuario,
                string numero,
                string numeroRedireccion,
                bool habilitarSMS,
                int periodoMeses,
                decimal descuento);

        Task<(int? NumeroId, string? StripeSessionId, string Error)> IniciarCompraNumero(
            ApplicationUser usuario,
            string numero,
            string numeroRedireccion,
            bool habilitarSMS);

        Task<bool> ProcesarCompraNumero(
            ApplicationUser usuario,
            string numeroTelefono,
            string? numeroRedireccion,
            bool habilitarSMS,
            string sessionId,
            string? subscriptionId);
        Task<bool> VerificarNumeroActivo(string plivoUuid);
        Task<Transaccion?> ObtenerTransaccionPorSesion(string sessionId);

        Task<(NumeroTelefonico? Numero, string Error)> ComprarNumero(ApplicationUser usuario, string numero, string numeroRedireccion, bool habilitarSMS);
        Task<bool> ActualizarRedireccion(int numeroId, string nuevoNumeroRedireccion);
        Task<bool> HabilitarSMS(int numeroId);
        Task<bool> DeshabilitarSMS(int numeroId);
        Task<bool> CancelarNumero(int numeroId);
        Task<List<NumeroTelefonico>> ObtenerNumerosPorUsuario(string userId);
        Task<NumeroTelefonico?> ObtenerNumeroDetalle(int numeroId);
        Task<(decimal CostoNumero, decimal CostoSMS)> ObtenerCostos(string numeroSeleccionado);
        Task<string?> ObtenerURLPago(int numeroId);
        //Saldo
        Task<decimal> CalcularCostoMensualNumero(string numero, bool smsHabilitado);
        Task<bool> VerificarSaldoParaCompra(string userId, string numero, bool smsHabilitado);
        Task<bool> DescontarSaldoMensual(NumeroTelefonico numero);
        Task<bool> ProcesarConsumoLlamada(NumeroTelefonico numero, LogLlamada logLlamada);
        Task<bool> ActualizarConfiguracionLlamadas(int numeroId, bool llamadasEntrantes, bool llamadasSalientes);
        Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10, string ciudad = "");
        Task<List<TwilioNumeroDisponible>> ObtenerNumerosPorCodigoArea(string pais, string codigoArea, int limite = 10);


    }

    public class TelefonicaService : ITelefonicaService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITwilioService _twilioService;
        private readonly IStripeService _stripeService;
        private readonly ILogger<TelefonicaService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ISaldoService _saldoService;
        private readonly IConfiguration _configuration;

        public TelefonicaService(
            ApplicationDbContext context,
            ITwilioService twilioService,
            IStripeService stripeService,
            ILogger<TelefonicaService> logger,
            ISaldoService saldoService,
            IConfiguration configuration)
        {
            _context = context;
            _twilioService = twilioService;
            _stripeService = stripeService;
            _logger = logger;
            _saldoService = saldoService;
            _configuration = configuration;

            // Configurar política de reintentos para operaciones de base de datos
            _retryPolicy = Polly.Policy
                .Handle<DbUpdateException>()
                .Or<DbUpdateConcurrencyException>()
                .WaitAndRetryAsync(
                    3, // Número de reintentos
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning($"Error de base de datos (intento {retryCount}): {exception.Message}. Reintentando en {timeSpan.TotalSeconds} segundos.");
                    }
                );
        }

        public async Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10, string ciudad = "")
        {
            try
            {
                _logger.LogInformation($"Solicitando números disponibles para país {pais} con límite {limite}");

                var numeros = await _twilioService.ObtenerNumerosDisponibles(pais, limite, ciudad);

                _logger.LogInformation($"Se encontraron {numeros.Count} números disponibles para {pais}");

                return numeros;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener números disponibles: {ex.Message}");
                throw;
            }
        }
        public async Task<List<TwilioNumeroDisponible>> ObtenerNumerosPorCodigoArea(string pais, string codigoArea, int limite = 10)
        {
            try
            {
                _logger.LogInformation($"Solicitando números disponibles para país {pais} con límite {limite}");

                var numeros = await _twilioService.ObtenerNumerosPorCodigoArea(pais, codigoArea, limite);

                _logger.LogInformation($"Se encontraron {numeros.Count} números disponibles para {pais}");

                return numeros;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener números disponibles: {ex.Message}");
                throw;
            }
        }


        public async Task<(decimal CostoNumero, decimal CostoSMS)> ObtenerCostos(string numeroSeleccionado)
        {
            try
            {
                _logger.LogInformation($"Calculando costos para el número {numeroSeleccionado}");

                // Obtener costo base del número desde Twilio
                var costoBaseNumero = await _twilioService.ObtenerCostoNumero(numeroSeleccionado);
                var costoBaseSMS = await _twilioService.ObtenerCostoSMS();

                // Obtener configuración de margen de ganancia
                var margenNumero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.ConfiguracionesSistema
                        .Where(c => c.Clave == "MargenGanancia")
                        .Select(c => decimal.Parse(c.Valor))
                        .FirstOrDefaultAsync()
                );

                var margenSMS = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.ConfiguracionesSistema
                        .Where(c => c.Clave == "MargenGananciaSMS")
                        .Select(c => decimal.Parse(c.Valor))
                        .FirstOrDefaultAsync()
                );

                var iva = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.ConfiguracionesSistema
                        .Where(c => c.Clave == "IVA")
                        .Select(c => decimal.Parse(c.Valor))
                        .FirstOrDefaultAsync()
                );

                // Obtener costos mínimos garantizados
                var costoMinimoNumero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.ConfiguracionesSistema
                        .Where(c => c.Clave == "CostoMinimoNumero")
                        .Select(c => decimal.Parse(c.Valor))
                        .FirstOrDefaultAsync()
                );

                var costoMinimoSMS = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.ConfiguracionesSistema
                        .Where(c => c.Clave == "CostoMinimoSMS")
                        .Select(c => decimal.Parse(c.Valor))
                        .FirstOrDefaultAsync()
                );

                // Si no hay configuraciones, usar valores predeterminados
                margenNumero = margenNumero == 0 ? 3.0m : margenNumero;
                margenSMS = margenSMS == 0 ? 3.5m : margenSMS;
                iva = iva == 0 ? 0.16m : iva;
                costoMinimoNumero = costoMinimoNumero == 0 ? 100.0m : costoMinimoNumero;
                costoMinimoSMS = costoMinimoSMS == 0 ? 25.0m : costoMinimoSMS;

                // Calcular precios con margen e IVA
                decimal costoNumeroConMargen = costoBaseNumero * (1 + margenNumero) * (1 + iva);
                decimal costoSMSConMargen = costoBaseSMS * (1 + margenSMS) * (1 + iva);

                // Asegurar que se cumplen los precios mínimos
                decimal costoFinalNumero = Math.Max(costoNumeroConMargen, costoMinimoNumero);
                decimal costoFinalSMS = Math.Max(costoSMSConMargen, costoMinimoSMS);

                // Redondear a 2 decimales
                costoFinalNumero = Math.Ceiling(costoFinalNumero);
                costoFinalSMS = Math.Ceiling(costoFinalSMS);

                _logger.LogInformation($"Costos calculados: Número=${costoFinalNumero}, SMS=${costoFinalSMS}");

                return (costoFinalNumero, costoFinalSMS);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al calcular costos: {ex.Message}");
                // Valores predeterminados si hay un error - aseguramos precios rentables
                return (100.0m, 25.0m);
            }
        }


        public async Task<(NumeroTelefonico? Numero, string Error)> ComprarNumero(
            ApplicationUser usuario,
            string numero,
            string numeroRedireccion,
            bool habilitarSMS)
        {
            try
            {
                _logger.LogInformation($"Iniciando proceso de compra del número {numero} para usuario {usuario.Id}");
                bool precioAceptable = await _twilioService.VerificarPrecioAceptable(numero, 3.0m);

                if (!precioAceptable)
                {
                    _logger.LogWarning($"El número {numero} excede el precio máximo permitido");
                    return (null, "El número seleccionado excede el precio máximo permitido (3 USD). Por favor, selecciona otro número.");
                }

                // 1. Calcular el costo mensual
                var costoMensual = await CalcularCostoMensualNumero(numero, habilitarSMS);

                // 2. Verificar si hay saldo suficiente
                var saldoSuficiente = await _saldoService.VerificarSaldoSuficiente(usuario.Id, costoMensual);

                if (!saldoSuficiente)
                {
                    _logger.LogWarning($"Saldo insuficiente para comprar número. UserId: {usuario.Id}, Costo: {costoMensual}");
                    return (null, "Saldo insuficiente para completar la compra. Por favor, recarga tu saldo.");
                }

                // 3. Obtener costos detallados
                var (costoNumero, costoSMS) = await ObtenerCostos(numero);

                // 4. Comprar el número en Twilio
                // Nota: Esto podría ser una compra real o un placeholder según tu implementación
                var numeroComprado = await _twilioService.ComprarNumero(numero);

                if (numeroComprado == null)
                {
                    _logger.LogError($"Error al comprar número en Twilio: {numero}");
                    return (null, "Error al adquirir el número. Por favor, intenta con otro número.");
                }

                // 5. Registrar el número en nuestra base de datos
                var fechaActual = DateTime.UtcNow;
                var nuevoNumero = new NumeroTelefonico
                {
                    Numero = numero,
                    PlivoUuid = numeroComprado.Sid, // Ahora usamos el Sid real de Twilio
                    UserId = usuario.Id,
                    NumeroRedireccion = numeroRedireccion,
                    FechaCompra = fechaActual,
                    FechaExpiracion = fechaActual.AddMonths(1),
                    CostoMensual = costoNumero,
                    Activo = true, // Activado inmediatamente ya que se cobra por adelantado
                    SMSHabilitado = habilitarSMS,
                    CostoSMS = habilitarSMS ? costoSMS : null
                };

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _context.NumerosTelefonicos.Add(nuevoNumero);
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Número registrado en base de datos con ID {nuevoNumero.Id}");

                // 6. Configurar redirección
                var redirConfigured = await _twilioService.ConfigurarRedireccion(numeroComprado.Sid, numeroRedireccion);

                if (!redirConfigured)
                {
                    _logger.LogWarning($"Error al configurar redirección para {numeroComprado.Sid} a {numeroRedireccion}");
                    // Continuamos a pesar del error, lo intentaremos más tarde
                }

                // 7. Descontar el saldo
                string concepto = $"Compra de número {numero}" + (habilitarSMS ? " con SMS" : "");
                var saldoDescontado = await _saldoService.DescontarSaldo(
                    usuario.Id,
                    costoMensual,
                    concepto,
                    nuevoNumero.Id);

                if (!saldoDescontado)
                {
                    _logger.LogError($"Error al descontar saldo para {usuario.Id}, monto: {costoMensual}");
                    // A pesar del error, continuamos ya que el número ya fue comprado
                }

                return (nuevoNumero, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en proceso de compra: {ex.Message}");
                return (null, $"Error al comprar número: {ex.Message}");
            }
        }
        public async Task<string?> ObtenerURLPago(int numeroId)
        {
            try
            {
                _logger.LogInformation($"Obteniendo URL de pago para número ID {numeroId}");

                // Buscar la transacción pendiente para este número
                var transaccion = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.Transacciones
                        .Where(t => t.NumeroTelefonicoId == numeroId && t.Status == "Pendiente")
                        .OrderByDescending(t => t.Fecha)
                        .FirstOrDefaultAsync()
                );

                if (transaccion == null)
                {
                    _logger.LogWarning($"No se encontró transacción pendiente para el número ID {numeroId}");
                    return null;
                }

                // Obtener URL de pago desde Stripe
                return await _stripeService.ObtenerURLPago(transaccion.StripePaymentId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener URL de pago: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> ActualizarRedireccion(int numeroId, string nuevoNumeroRedireccion)
        {
            try
            {
                _logger.LogInformation($"Actualizando redirección para número ID {numeroId} a {nuevoNumeroRedireccion}");

                var numero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos.FindAsync(numeroId)
                );

                if (numero == null || !numero.Activo)
                {
                    _logger.LogWarning($"No se encontró número activo con ID {numeroId}");
                    return false;
                }

                var resultado = await _twilioService.ConfigurarRedireccion(numero.PlivoUuid, nuevoNumeroRedireccion);
                if (resultado)
                {
                    numero.NumeroRedireccion = nuevoNumeroRedireccion;
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        await _context.SaveChangesAsync();
                    });

                    _logger.LogInformation($"Redirección actualizada correctamente para número ID {numeroId}");
                    return true;
                }

                _logger.LogWarning($"No se pudo actualizar la redirección para número ID {numeroId}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al actualizar redirección: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> HabilitarSMS(int numeroId)
        {
            try
            {
                _logger.LogInformation($"Habilitando SMS para número ID {numeroId}");

                var numero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos.FindAsync(numeroId)
                );

                if (numero == null || !numero.Activo || numero.SMSHabilitado)
                {
                    _logger.LogWarning($"Número no válido para habilitar SMS: ID {numeroId}, Activo={numero?.Activo}, SMSHabilitado={numero?.SMSHabilitado}");
                    return false;
                }

                // Obtener costo de SMS
                var costoSMS = (await ObtenerCostos(numero.Numero)).CostoSMS;

                // Activar SMS en Twilio
                var resultado = await _twilioService.ActivarSMS(numero.PlivoUuid);
                if (!resultado)
                {
                    _logger.LogWarning($"No se pudo activar SMS en Twilio para número ID {numeroId}");
                    return false;
                }

                // Actualizar suscripción en Stripe
                if (!string.IsNullOrEmpty(numero.StripeSubscriptionId))
                {
                    var resultadoStripe = await _stripeService.AgregarSMSASuscripcion(
                        numero.StripeSubscriptionId,
                        costoSMS);

                    if (!resultadoStripe)
                    {
                        // Revertir cambios en Twilio
                        await _twilioService.DesactivarSMS(numero.PlivoUuid);
                        _logger.LogWarning($"No se pudo agregar SMS a la suscripción de Stripe para número ID {numeroId}");
                        return false;
                    }
                }

                // Actualizar en nuestra base de datos
                numero.SMSHabilitado = true;
                numero.CostoSMS = costoSMS;
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"SMS habilitado correctamente para número ID {numeroId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al habilitar SMS: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeshabilitarSMS(int numeroId)
        {
            try
            {
                _logger.LogInformation($"Deshabilitando SMS para número ID {numeroId}");

                var numero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos.FindAsync(numeroId)
                );

                if (numero == null || !numero.Activo || !numero.SMSHabilitado)
                {
                    _logger.LogWarning($"Número no válido para deshabilitar SMS: ID {numeroId}");
                    return false;
                }

                // Desactivar SMS en Twilio
                var resultado = await _twilioService.DesactivarSMS(numero.PlivoUuid);
                if (!resultado)
                {
                    _logger.LogWarning($"No se pudo desactivar SMS en Twilio para número ID {numeroId}");
                    return false;
                }

                // Actualizar suscripción en Stripe
                if (!string.IsNullOrEmpty(numero.StripeSubscriptionId))
                {
                    var resultadoStripe = await _stripeService.QuitarSMSDeSuscripcion(
                        numero.StripeSubscriptionId);

                    if (!resultadoStripe)
                    {
                        // Revertir cambios en Twilio
                        await _twilioService.ActivarSMS(numero.PlivoUuid);
                        _logger.LogWarning($"No se pudo quitar SMS de la suscripción de Stripe para número ID {numeroId}");
                        return false;
                    }
                }

                // Actualizar en nuestra base de datos
                numero.SMSHabilitado = false;
                numero.CostoSMS = null;
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"SMS deshabilitado correctamente para número ID {numeroId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al deshabilitar SMS: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CancelarNumero(int numeroId)
        {
            try
            {
                _logger.LogInformation($"Cancelando número ID {numeroId}");

                var numero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos.FindAsync(numeroId)
                );

                if (numero == null)
                {
                    _logger.LogWarning($"No se encontró número con ID {numeroId}");
                    return false;
                }

                // Cancelar suscripción en Stripe
                if (!string.IsNullOrEmpty(numero.StripeSubscriptionId))
                {
                    await _stripeService.CancelarSuscripcion(numero.StripeSubscriptionId);
                }

                // Liberar número en Twilio solo si está activo y ya se compró
                if (numero.Activo && !string.IsNullOrEmpty(numero.PlivoUuid) && numero.PlivoUuid != "pendiente")
                {
                    await _twilioService.LiberarNumero(numero.PlivoUuid);
                }

                // Actualizar en nuestra base de datos
                numero.Activo = false;
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Número ID {numeroId} cancelado correctamente");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al cancelar número: {ex.Message}");
                return false;
            }
        }

        public async Task<List<NumeroTelefonico>> ObtenerNumerosPorUsuario(string userId)
        {
            try
            {
                _logger.LogInformation($"Obteniendo números para usuario {userId}");

                return await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos
                        .Where(n => n.UserId == userId)
                        .OrderByDescending(n => n.FechaCompra)
                        .ToListAsync()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener números por usuario: {ex.Message}");
                return new List<NumeroTelefonico>();
            }
        }

        public async Task<NumeroTelefonico?> ObtenerNumeroDetalle(int numeroId)
        {
            try
            {
                _logger.LogInformation($"Obteniendo detalles del número ID {numeroId}");

                return await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos
                        .Include(n => n.LogsLlamadas)
                        .Include(n => n.LogsSMS)
                        .FirstOrDefaultAsync(n => n.Id == numeroId)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener detalle del número: {ex.Message}");
                return null;
            }
        }
        public async Task<decimal> CalcularCostoMensualNumero(string numero, bool smsHabilitado)
        {
            try
            {
                var (costoNumero, costoSMS) = await ObtenerCostos(numero);
                return smsHabilitado ? costoNumero + costoSMS : costoNumero;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al calcular costo mensual para {numero}");
                throw;
            }
        }

        public async Task<bool> VerificarSaldoParaCompra(string userId, string numero, bool smsHabilitado)
        {
            try
            {
                var costoMensual = await CalcularCostoMensualNumero(numero, smsHabilitado);
                return await _saldoService.VerificarSaldoSuficiente(userId, costoMensual);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar saldo para compra: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DescontarSaldoMensual(NumeroTelefonico numero)
        {
            try
            {
                decimal costoTotal = numero.CostoMensual;
                if (numero.SMSHabilitado && numero.CostoSMS.HasValue)
                {
                    costoTotal += numero.CostoSMS.Value;
                }

                string concepto = $"Cargo mensual - Número {numero.Numero}";

                return await _saldoService.DescontarSaldo(
                    numero.UserId,
                    costoTotal,
                    concepto,
                    numero.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al descontar saldo mensual para número ID {numero.Id}");
                return false;
            }
        }

        public async Task<bool> ProcesarConsumoLlamada(NumeroTelefonico numero, LogLlamada logLlamada)
        {
            try
            {
                // Solo procesar llamadas completadas
                if (logLlamada.Estado != "completed" && !logLlamada.Estado.Contains("complete"))
                {
                    return true; // No cobrar por llamadas no completadas
                }

                // IMPORTANTE: Verificar si esta llamada ya fue cobrada
                var llamadaYaCobrada = await _context.MovimientosSaldo
                    .AnyAsync(m => m.NumeroTelefonicoId == numero.Id &&
                                  m.Concepto.Contains(logLlamada.IdLlamadaPlivo) &&
                                  m.Fecha > DateTime.UtcNow.AddHours(-24));

                if (llamadaYaCobrada)
                {
                    _logger.LogInformation($"La llamada con ID {logLlamada.IdLlamadaPlivo} ya fue cobrada anteriormente");
                    return true; // Evitar cobro duplicado
                }

                // Extraer el país del número de origen (simplificado)
                string pais = "MX"; // Por defecto México
                if (logLlamada.NumeroOrigen.StartsWith("+1"))
                {
                    pais = "US";
                }
                else if (logLlamada.NumeroOrigen.StartsWith("+34"))
                {
                    pais = "ES";
                }

                // Calcular el costo de la llamada
                decimal costoLlamada = await _saldoService.CalcularCostoLlamada(logLlamada.Duracion, pais);

                if (costoLlamada <= 0)
                {
                    return true; // No cobrar por llamadas sin costo
                }

                // Formato de duración para el concepto
                TimeSpan duracion = TimeSpan.FromSeconds(logLlamada.Duracion);
                string duracionFormateada = $"{duracion.Minutes}:{duracion.Seconds:D2}";

                // Concepto para el movimiento (incluir ID para evitar duplicados)
                string concepto = $"Llamada de {logLlamada.NumeroOrigen} ({duracionFormateada} min) - ID:{logLlamada.IdLlamadaPlivo}";

                // Descontar del saldo
                return await _saldoService.DescontarSaldo(
                    numero.UserId,
                    costoLlamada,
                    concepto,
                    numero.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar consumo de llamada para número ID {numero.Id}");
                return false;
            }
        }

        // Corrige el error de las variables duplicadas costoNumero y costoSMS
        // En el método ProcesarCompraNumero:

        public async Task<bool> ProcesarCompraNumero(
            ApplicationUser usuario,
            string numeroTelefono,
            string? numeroRedireccion,
            bool habilitarSMS,
            string sessionId,
            string? subscriptionId)
        {
            try
            {
                _logger.LogInformation($"Procesando compra de número {numeroTelefono} por webhook para usuario {usuario.Id}");

                // Verificar si ya hay una transacción para esta sesión de Stripe
                var transaccion = await ObtenerTransaccionPorSesion(sessionId);

                // Si la transacción ya existe y está completada, evitar reprocesamiento
                if (transaccion != null && transaccion.Status == "Completado")
                {
                    _logger.LogInformation($"La sesión {sessionId} ya fue procesada anteriormente");
                    return true;
                }

                // Determinar si necesitamos crear un nuevo registro o actualizar uno existente
                NumeroTelefonico? numeroTelefonico;

                if (transaccion != null && transaccion.NumeroTelefonicoId.HasValue)
                {
                    // Actualizar número existente
                    numeroTelefonico = await _context.NumerosTelefonicos.FindAsync(transaccion.NumeroTelefonicoId.Value);

                    if (numeroTelefonico == null)
                    {
                        _logger.LogError($"No se encontró el número con ID {transaccion.NumeroTelefonicoId} para la transacción {sessionId}");
                        return false;
                    }
                }
                else
                {
                    // Crear nuevo registro de número
                    numeroTelefonico = new NumeroTelefonico
                    {
                        Numero = numeroTelefono,
                        UserId = usuario.Id,
                        NumeroRedireccion = numeroRedireccion ?? "pendiente",
                        FechaCompra = DateTime.UtcNow,
                        FechaExpiracion = DateTime.UtcNow.AddMonths(1),
                        Activo = false,
                        SMSHabilitado = habilitarSMS,
                        PlivoUuid = "pendiente" // Valor inicial hasta adquirir el número
                    };

                    _context.NumerosTelefonicos.Add(numeroTelefonico);
                    await _context.SaveChangesAsync(); // Guardar para obtener ID

                    // Si no existía transacción, crearla
                    if (transaccion == null)
                    {
                        // AQUÍ ESTÁ EL CAMBIO: Usar nombres diferentes para las variables locales
                        var costos = await ObtenerCostos(numeroTelefono);
                        decimal costoNumeroFinal = costos.CostoNumero;
                        decimal costoSMSFinal = costos.CostoSMS;

                        transaccion = new Transaccion
                        {
                            UserId = usuario.Id,
                            NumeroTelefonicoId = numeroTelefonico.Id,
                            Fecha = DateTime.UtcNow,
                            Monto = habilitarSMS ? costoNumeroFinal + costoSMSFinal : costoNumeroFinal,
                            Concepto = $"Compra de número {numeroTelefono}" + (habilitarSMS ? " con SMS" : ""),
                            StripePaymentId = sessionId,
                            Status = "Pendiente"
                        };

                        _context.Transacciones.Add(transaccion);
                        await _context.SaveChangesAsync();
                    }
                }

                // Adquirir el número en Twilio
                var twilioNumero = await _twilioService.ComprarNumero(numeroTelefono);

                if (twilioNumero == null)
                {
                    _logger.LogError($"Error al comprar número en Twilio: {numeroTelefono}");
                    transaccion.Status = "Fallido";
                    transaccion.DetalleError = "Error al adquirir el número en Twilio";
                    await _context.SaveChangesAsync();
                    return false;
                }

                // Actualizar datos del número
                numeroTelefonico.PlivoUuid = twilioNumero.Sid;
                numeroTelefonico.StripeSubscriptionId = subscriptionId;
                numeroTelefonico.Activo = true;

                // Calcular y actualizar costos - AQUÍ ESTÁ EL CAMBIO: Usar nombres diferentes
                var costosActualizados = await ObtenerCostos(numeroTelefono);
                numeroTelefonico.CostoMensual = costosActualizados.CostoNumero;
                if (habilitarSMS)
                {
                    numeroTelefonico.CostoSMS = costosActualizados.CostoSMS;
                }

                // Configurar redirección si tenemos número de destino
                if (!string.IsNullOrEmpty(numeroRedireccion) && numeroRedireccion != "pendiente")
                {
                    var redirConfigured = await _twilioService.ConfigurarRedireccion(twilioNumero.Sid, numeroRedireccion);
                    if (!redirConfigured)
                    {
                        _logger.LogWarning($"Error al configurar redirección para {twilioNumero.Sid} a {numeroRedireccion}");
                        // Continuamos a pesar del error
                    }
                }

                // Configurar SMS si está habilitado
                if (habilitarSMS)
                {
                    await _twilioService.ActivarSMS(twilioNumero.Sid);
                }

                // Actualizar transacción
                transaccion.Status = "Completado";
                // Si la clase Transaccion tiene FechaCompletado, descomenta esta línea:
                // transaccion.FechaCompletado = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Número {numeroTelefono} procesado correctamente por webhook. ID: {numeroTelefonico.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar compra de número por webhook: {ex.Message}");
                return false;
            }
        }


        public async Task<(int? NumeroId, string? StripeSessionId, string Error)> IniciarCompraNumero(
            ApplicationUser usuario,
            string numero,
            string numeroRedireccion,
            bool habilitarSMS)
        {
            try
            {
                _logger.LogInformation($"Iniciando proceso de compra del número {numero} para usuario {usuario.Id}");

                // Calcular costos
                var (costoNumero, costoSMS) = await ObtenerCostos(numero);
                decimal costoTotal = habilitarSMS ? costoNumero + costoSMS : costoNumero;

                // Crear un registro preliminar del número (estado pendiente)
                var fechaActual = DateTime.UtcNow;
                var nuevoNumero = new NumeroTelefonico
                {
                    Numero = numero,
                    PlivoUuid = "pendiente", // Se actualizará cuando se complete la compra
                    UserId = usuario.Id,
                    NumeroRedireccion = numeroRedireccion,
                    FechaCompra = fechaActual,
                    FechaExpiracion = fechaActual.AddMonths(1),
                    CostoMensual = costoNumero,
                    Activo = false, // Inactivo hasta que se complete el pago
                    SMSHabilitado = habilitarSMS,
                    CostoSMS = habilitarSMS ? costoSMS : null
                };

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _context.NumerosTelefonicos.Add(nuevoNumero);
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Registro preliminar creado con ID {nuevoNumero.Id}");

                // Crear transacción pendiente
                var transaccion = new Transaccion
                {
                    UserId = usuario.Id,
                    NumeroTelefonicoId = nuevoNumero.Id,
                    Fecha = fechaActual,
                    Monto = costoTotal,
                    Concepto = $"Compra de número {numero}" + (habilitarSMS ? " con SMS" : ""),
                    Status = "Pendiente"
                };

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _context.Transacciones.Add(transaccion);
                    await _context.SaveChangesAsync();
                });

                // Crear sesión de checkout en Stripe
                var stripeSession = await _stripeService.CrearSesionCompra(
                    usuario.StripeCustomerId,
                    numero,
                    costoNumero,
                    habilitarSMS ? costoSMS : null);

                if (string.IsNullOrEmpty(stripeSession.SessionId))
                {
                    _logger.LogError($"Error al crear sesión de Stripe para número {numero}");
                    return (nuevoNumero.Id, null, "Error al crear sesión de pago");
                }

                // Actualizar la transacción con el ID de sesión
                transaccion.StripePaymentId = stripeSession.SessionId;
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Sesión de pago creada: {stripeSession.SessionId} para número {nuevoNumero.Id}");
                return (nuevoNumero.Id, stripeSession.SessionId, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en inicio de compra: {ex.Message}");
                return (null, null, $"Error al iniciar compra: {ex.Message}");
            }
        }

        // 4. Implementar método simplificado para obtener transacción por sesión
        public async Task<Transaccion?> ObtenerTransaccionPorSesion(string sessionId)
        {
            try
            {
                return await _retryPolicy.ExecuteAsync(async () =>
                    await _context.Transacciones
                        .Include(t => t.NumeroTelefonico)
                        .FirstOrDefaultAsync(t => t.StripePaymentId == sessionId)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener transacción para sesión {sessionId}");
                return null;
            }
        }

        // 5. Implementar método para configurar un número después de adquirido (más simple)
        public async Task<bool> ConfigurarNumeroAdquirido(int numeroId, string? numeroRedireccion = null)
        {
            try
            {
                var numero = await _context.NumerosTelefonicos.FindAsync(numeroId);

                if (numero == null)
                {
                    _logger.LogWarning($"No se encontró el número con ID {numeroId} para configurar");
                    return false;
                }

                // Si tenemos número de redirección nuevo, actualizarlo
                if (!string.IsNullOrEmpty(numeroRedireccion) && numeroRedireccion != "pendiente")
                {
                    numero.NumeroRedireccion = numeroRedireccion;

                    // Configurar redirección en Twilio
                    if (!string.IsNullOrEmpty(numero.PlivoUuid) && numero.PlivoUuid != "pendiente")
                    {
                        await _twilioService.ConfigurarRedireccion(numero.PlivoUuid, numeroRedireccion);
                    }
                }

                // Asegurarnos que el número esté activado
                if (!numero.Activo && !string.IsNullOrEmpty(numero.PlivoUuid) && numero.PlivoUuid != "pendiente")
                {
                    numero.Activo = true;

                    // Configurar SMS si está habilitado
                    if (numero.SMSHabilitado)
                    {
                        await _twilioService.ActivarSMS(numero.PlivoUuid);
                    }
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al configurar número adquirido ID {numeroId}");
                return false;
            }
        }
        public async Task<bool> VerificarNumeroActivo(string plivoUuid)
        {
            try
            {
                _logger.LogInformation($"Verificando si el número con PlivoUuid {plivoUuid} sigue activo en Twilio");

                // Si el número tiene un estado especial, retornar false
                if (string.IsNullOrEmpty(plivoUuid) ||
                    plivoUuid == "pendiente" ||
                    plivoUuid == "liberado")
                {
                    return false;
                }

                // Delegar la verificación al servicio de Twilio
                return await _twilioService.VerificarNumeroActivo(plivoUuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar número en Twilio: {ex.Message}");
                // Por seguridad, asumimos que el número no está activo en caso de error
                return false;
            }
        }
        public async Task<(NumeroTelefonico? Numero, string Error)> ComprarNumeroConPeriodo(ApplicationUser usuario, string numero, string numeroRedireccion, bool habilitarSMS,
                            int periodoMeses, decimal descuento)
        {
            try
            {
                _logger.LogInformation($"Iniciando proceso de compra del número {numero} para usuario {usuario.Id} con periodo de {periodoMeses} meses");
                bool precioAceptable = await _twilioService.VerificarPrecioAceptable(numero, 3.0m);

                if (!precioAceptable)
                {
                    _logger.LogWarning($"El número {numero} excede el precio máximo permitido");
                    return (null, "El número seleccionado excede el precio máximo permitido (3 USD). Por favor, selecciona otro número.");
                }

                // 1. Calcular el costo mensual
                var (costoNumero, costoSMS) = await ObtenerCostos(numero);
                decimal costoMensual = costoNumero + (habilitarSMS ? costoSMS : 0);

                // 2. Calcular el costo total del periodo con descuento
                decimal costoTotal = costoMensual * periodoMeses * (1 - descuento);

                // 3. Verificar si hay saldo suficiente
                var saldoSuficiente = await _saldoService.VerificarSaldoSuficiente(usuario.Id, costoTotal);

                if (!saldoSuficiente)
                {
                    _logger.LogWarning($"Saldo insuficiente para comprar número. UserId: {usuario.Id}, Costo: {costoTotal}");
                    return (null, "Saldo insuficiente para completar la compra. Por favor, recarga tu saldo.");
                }

                // 4. Comprar el número en Twilio
                var numeroComprado = await _twilioService.ComprarNumero(numero);

                if (numeroComprado == null)
                {
                    _logger.LogError($"Error al comprar número en Twilio: {numero}");
                    return (null, "Error al adquirir el número. Por favor, intenta con otro número.");
                }

                // 5. Registrar el número en nuestra base de datos
                var fechaActual = DateTime.UtcNow;
                var nuevoNumero = new NumeroTelefonico
                {
                    Numero = numero,
                    PlivoUuid = numeroComprado.Sid,
                    UserId = usuario.Id,
                    NumeroRedireccion = numeroRedireccion,
                    FechaCompra = fechaActual,
                    FechaExpiracion = fechaActual.AddMonths(periodoMeses), // Fecha de expiración según el periodo
                    CostoMensual = costoNumero,
                    Activo = true,
                    SMSHabilitado = habilitarSMS,
                    CostoSMS = habilitarSMS ? costoSMS : null,
                    PeriodoContratado = periodoMeses // Guardar el periodo contratado (asegúrate de que el modelo tenga esta propiedad)
                };

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _context.NumerosTelefonicos.Add(nuevoNumero);
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Número registrado en base de datos con ID {nuevoNumero.Id}");

                // 6. Configurar redirección
                var redirConfigured = await _twilioService.ConfigurarRedireccion(numeroComprado.Sid, numeroRedireccion);

                if (!redirConfigured)
                {
                    _logger.LogWarning($"Error al configurar redirección para {numeroComprado.Sid} a {numeroRedireccion}");
                    // Continuamos a pesar del error, lo intentaremos más tarde
                }

                // 7. Activar SMS si está habilitado
                if (habilitarSMS)
                {
                    await _twilioService.ActivarSMS(numeroComprado.Sid);
                }

                // 8. Descontar el saldo
                string concepto = $"Compra de número {numero}" + (habilitarSMS ? " con SMS" : "") + $" por {periodoMeses} meses";
                var saldoDescontado = await _saldoService.DescontarSaldo(
                    usuario.Id,
                    costoTotal,
                    concepto,
                    nuevoNumero.Id);

                if (!saldoDescontado)
                {
                    _logger.LogError($"Error al descontar saldo para {usuario.Id}, monto: {costoTotal}");
                    // A pesar del error, continuamos ya que el número ya fue comprado
                }

                // 9. Si usamos Stripe para suscripciones (opcional, ajustar según tu implementación)
                if (!string.IsNullOrEmpty(usuario.StripeCustomerId) && periodoMeses > 1)
                {
                    try
                    {
                        // Crear una suscripción en Stripe con el periodo correcto
                        var subscriptionId = await _stripeService.CrearSuscripcionConPeriodo(
                            usuario.StripeCustomerId,
                            $"Número Empresarial: {numero}" + (habilitarSMS ? " con SMS" : ""),
                            costoMensual,
                            periodoMeses);

                        // Guardar el ID de la suscripción
                        if (!string.IsNullOrEmpty(subscriptionId))
                        {
                            nuevoNumero.StripeSubscriptionId = subscriptionId;
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al crear suscripción en Stripe para número {nuevoNumero.Id}");
                        // Continuamos, ya que la compra se completó correctamente
                    }
                }

                return (nuevoNumero, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en proceso de compra con periodo: {ex.Message}");
                return (null, $"Error al comprar número: {ex.Message}");
            }
        }
        public async Task<bool> ActualizarConfiguracionLlamadas(int numeroId, bool llamadasEntrantes, bool llamadasSalientes)
        {
            try
            {
                _logger.LogInformation($"Actualizando configuración de llamadas para número ID {numeroId}: Entrantes={llamadasEntrantes}, Salientes={llamadasSalientes}");

                var numero = await _retryPolicy.ExecuteAsync(async () =>
                    await _context.NumerosTelefonicos.FindAsync(numeroId)
                );

                if (numero == null || !numero.Activo)
                {
                    _logger.LogWarning($"No se encontró número activo con ID {numeroId}");
                    return false;
                }

                // Guardar la configuración anterior para detectar cambios
                bool llamadasEntrantesAntes = numero.LlamadasEntrantes;

                // Actualizar configuración en la base de datos
                numero.LlamadasEntrantes = llamadasEntrantes;
                numero.LlamadasSalientes = llamadasSalientes;

                // Si se desactivan las llamadas entrantes (y antes estaban activas), configurar para rechazar llamadas en Twilio
                if (!llamadasEntrantes && llamadasEntrantesAntes && !string.IsNullOrEmpty(numero.PlivoUuid) && numero.PlivoUuid != "pendiente")
                {
                    // Configurar Twilio para rechazar llamadas entrantes
                    await ConfigurarRechazarLlamadasTwilio(numero.PlivoUuid);
                }
                // Si se activan las llamadas entrantes (y antes estaban desactivadas) y hay un número de redirección
                else if (llamadasEntrantes && !llamadasEntrantesAntes && !string.IsNullOrEmpty(numero.NumeroRedireccion) && numero.NumeroRedireccion != "pendiente")
                {
                    // Restaurar redirección en Twilio
                    await _twilioService.ConfigurarRedireccion(numero.PlivoUuid, numero.NumeroRedireccion);
                }

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Configuración de llamadas actualizada correctamente para número ID {numeroId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar configuración de llamadas para número {numeroId}");
                return false;
            }
        }

        // Método auxiliar para configurar Twilio para rechazar llamadas entrantes
        private async Task<bool> ConfigurarRechazarLlamadasTwilio(string twilioSid)
        {
            try
            {
                _logger.LogInformation($"Configurando Twilio para rechazar llamadas en número {twilioSid}");

                // Construir la URL del endpoint que rechaza llamadas
                var appDomain = _configuration["AppUrl"] ?? "https://localhost:7019";
                var rejectCallUrl = $"{appDomain}/api/twilio/reject-call";

                // Utilizar el servicio de Twilio para actualizar la configuración
                var resultado = await _twilioService.ConfigurarURLRechazo(twilioSid, rejectCallUrl);

                if (resultado)
                {
                    _logger.LogInformation($"Configuración de rechazo de llamadas aplicada correctamente para {twilioSid}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"No se pudo configurar el rechazo de llamadas para {twilioSid}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al configurar rechazo de llamadas en Twilio para {twilioSid}");
                return false;
            }
        }
    }
}