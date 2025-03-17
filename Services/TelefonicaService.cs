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
        Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10);
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

    }

    public class TelefonicaService : ITelefonicaService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITwilioService _twilioService;
        private readonly IStripeService _stripeService;
        private readonly ILogger<TelefonicaService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ISaldoService _saldoService;

        public TelefonicaService(
            ApplicationDbContext context,
            ITwilioService twilioService,
            IStripeService stripeService,
            ILogger<TelefonicaService> logger,
            ISaldoService saldoService)
        {
            _context = context;
            _twilioService = twilioService;
            _stripeService = stripeService;
            _logger = logger;
            _saldoService = saldoService;

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

        public async Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10)
        {
            try
            {
                _logger.LogInformation($"Solicitando números disponibles para país {pais} con límite {limite}");

                var numeros = await _twilioService.ObtenerNumerosDisponibles(pais, limite);

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


    }
}