using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;

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
    }

    public class TelefonicaService : ITelefonicaService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITwilioService _twilioService;
        private readonly IStripeService _stripeService;
        private readonly ILogger<TelefonicaService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public TelefonicaService(
            ApplicationDbContext context,
            ITwilioService twilioService,
            IStripeService stripeService,
            ILogger<TelefonicaService> logger)
        {
            _context = context;
            _twilioService = twilioService;
            _stripeService = stripeService;
            _logger = logger;

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

                // Obtener configuración de margen de ganancia del sistema
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

                // Si no hay configuraciones, usar valores predeterminados
                margenNumero = margenNumero == 0 ? 0.8m : margenNumero;
                margenSMS = margenSMS == 0 ? 0.85m : margenSMS;
                iva = iva == 0 ? 0.16m : iva;

                // Calcular precios con margen e IVA
                decimal costoFinalNumero = costoBaseNumero * (1 + margenNumero) * (1 + iva);
                decimal costoFinalSMS = costoBaseSMS * (1 + margenSMS) * (1 + iva);

                // Redondear a 2 decimales
                costoFinalNumero = Math.Round(costoFinalNumero, 2);
                costoFinalSMS = Math.Round(costoFinalSMS, 2);

                _logger.LogInformation($"Costos calculados: Número=${costoFinalNumero}, SMS=${costoFinalSMS}");

                return (costoFinalNumero, costoFinalSMS);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al calcular costos: {ex.Message}");
                // Valores predeterminados si hay un error
                return (20.0m, 5.0m);
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

                // 1. Verificar que el usuario tenga StripeCustomerId
                if (string.IsNullOrEmpty(usuario.StripeCustomerId))
                {
                    _logger.LogInformation($"Creando cliente en Stripe para usuario {usuario.Id}");

                    usuario.StripeCustomerId = await _stripeService.CrearClienteStripe(usuario);
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        _context.Users.Update(usuario);
                        await _context.SaveChangesAsync();
                    });
                }

                // 2. Obtener costos
                var (costoNumero, costoSMS) = await ObtenerCostos(numero);

                // 3. Crear sesión de checkout en Stripe
                var checkoutSession = await _stripeService.CrearSesionCompra(
                    usuario.StripeCustomerId,
                    numero,
                    costoNumero,
                    habilitarSMS ? costoSMS : null);

                _logger.LogInformation($"Sesión de checkout creada: {checkoutSession.SessionId}");

                // 4. Registrar el número en nuestra base de datos (sin comprar aún en Twilio)
                var fechaActual = DateTime.UtcNow;
                var nuevoNumero = new NumeroTelefonico
                {
                    Numero = numero,
                    PlivoUuid = "pendiente", // Se actualizará después del pago
                    UserId = usuario.Id,
                    NumeroRedireccion = numeroRedireccion,
                    FechaCompra = fechaActual,
                    FechaExpiracion = fechaActual.AddMonths(1),
                    CostoMensual = costoNumero,
                    Activo = false, // Se activa cuando se completa el pago
                    SMSHabilitado = habilitarSMS,
                    CostoSMS = habilitarSMS ? costoSMS : null
                };

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _context.NumerosTelefonicos.Add(nuevoNumero);
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Número registrado en base de datos con ID {nuevoNumero.Id}");

                // 5. Crear transacción pendiente
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _context.Transacciones.Add(new Transaccion
                    {
                        UserId = usuario.Id,
                        NumeroTelefonicoId = nuevoNumero.Id,
                        Fecha = fechaActual,
                        Monto = habilitarSMS ? costoNumero + costoSMS : costoNumero,
                        Concepto = $"Compra de número - {numero}",
                        StripePaymentId = checkoutSession.SessionId,
                        Status = "Pendiente"
                    });
                    await _context.SaveChangesAsync();
                });

                _logger.LogInformation($"Transacción pendiente creada para el número {numero}");

                return (nuevoNumero, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en proceso de compra: {ex.Message}");
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
    }
}