using Microsoft.EntityFrameworkCore;
using Polly;
using Quartz;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class RenovacionNumerosJob : IJob
    {
        private readonly ILogger<RenovacionNumerosJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private const int TAMANO_LOTE = 50; // Procesar en lotes de 50 números
        private const int DIAS_GRACIA = 2; // Período de gracia de 2 días

        public RenovacionNumerosJob(
            ILogger<RenovacionNumerosJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando job de renovación de números");

            // Crear scope para acceder a servicios scoped
            using var scope = _serviceProvider.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var telefonicaService = scope.ServiceProvider.GetRequiredService<ITelefonicaService>();
            var saldoService = scope.ServiceProvider.GetRequiredService<ISaldoService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            try
            {
                // 1. Primero, procesar números vencidos que están en período de gracia
                await ProcesarNumerosEnGracia(dbContext, saldoService, notificationService);

                // 2. Luego, procesar números a punto de vencer (en los próximos 2 días)
                await ProcesarNumerosPorVencer(dbContext, saldoService, notificationService);

                _logger.LogInformation("Job de renovación de números completado exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de renovación de números");
            }
        }

        private async Task ProcesarNumerosEnGracia(
            ApplicationDbContext dbContext,
            ISaldoService saldoService,
            INotificationService notificationService)
        {
            try
            {
                // Obtener números vencidos pero aún activos (gracias a período de gracia)
                // Filtramos números que vencieron hace menos de DIAS_GRACIA días
                var fechaLimiteGracia = DateTime.UtcNow.AddDays(-DIAS_GRACIA);
                var fechaHoy = DateTime.UtcNow;

                var numerosEnGracia = await dbContext.NumerosTelefonicos
                    .Where(n => n.Activo &&
                           n.FechaExpiracion < fechaHoy &&
                           n.FechaExpiracion > fechaLimiteGracia)
                    .Include(n => n.Usuario)
                    .OrderBy(n => n.FechaExpiracion) // Procesar primero los más antiguos
                    .Take(TAMANO_LOTE)
                    .ToListAsync();

                _logger.LogInformation($"Se encontraron {numerosEnGracia.Count} números en período de gracia");

                foreach (var numero in numerosEnGracia)
                {
                    try
                    {
                        // Verificar cuántos días han pasado desde la expiración
                        var diasPasados = (fechaHoy - numero.FechaExpiracion).Days;
                        var diasRestantes = DIAS_GRACIA - diasPasados;

                        // Verificar si el usuario tiene saldo suficiente
                        decimal costoTotal = numero.CostoMensual;
                        if (numero.SMSHabilitado && numero.CostoSMS.HasValue)
                        {
                            costoTotal += numero.CostoSMS.Value;
                        }

                        bool saldoSuficiente = await saldoService.VerificarSaldoSuficiente(numero.UserId, costoTotal);

                        if (saldoSuficiente)
                        {
                            // El usuario ha cargado saldo, renovar el número
                            await RenovarNumero(numero, costoTotal, saldoService, dbContext);

                            // Notificar al usuario que su número ha sido renovado
                            //await notificationService.EnviarNotificacion(
                            //    numero.UserId,
                            //    "Número renovado automáticamente",
                            //    $"Tu número {numero.Numero} ha sido renovado automáticamente por un mes más.",
                            //    "success");
                        }
                        else if (diasRestantes <= 0)
                        {
                            // Se acabó el período de gracia, desactivar el número
                            await DesactivarNumero(numero, dbContext);

                            // Notificar al usuario que su número ha sido desactivado
                            //await notificationService.EnviarNotificacion(
                            //    numero.UserId,
                            //    "Número desactivado por falta de saldo",
                            //    $"Tu número {numero.Numero} ha sido desactivado porque no se pudo renovar por falta de saldo. Puedes reactivarlo agregando saldo y contactando a soporte.",
                            //    "danger");
                        }
                        else
                        {
                            // Todavía hay días de gracia, notificar al usuario
                            //await notificationService.EnviarNotificacion(
                            //    numero.UserId,
                            //    "Renovación pendiente - Saldo insuficiente",
                            //    $"Tu número {numero.Numero} vencerá en {diasRestantes} día(s). Por favor, recarga saldo para evitar que se desactive.",
                            //    "warning");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al procesar número en gracia {numero.Id}: {ex.Message}");
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar números en período de gracia");
                throw;
            }
        }

        private async Task ProcesarNumerosPorVencer(
            ApplicationDbContext dbContext,
            ISaldoService saldoService,
            INotificationService notificationService)
        {
            try
            {
                // Obtener números a punto de expirar (en los próximos 2 días)
                var fechaHoy = DateTime.UtcNow;
                var fechaLimite = fechaHoy.AddDays(2);

                var numerosPorVencer = await dbContext.NumerosTelefonicos
                    .Where(n => n.Activo &&
                           n.FechaExpiracion > fechaHoy &&
                           n.FechaExpiracion <= fechaLimite)
                    .Include(n => n.Usuario)
                    .OrderBy(n => n.FechaExpiracion) // Procesar primero los más cercanos a vencer
                    .Take(TAMANO_LOTE)
                    .ToListAsync();

                _logger.LogInformation($"Se encontraron {numerosPorVencer.Count} números a punto de vencer (próximos 2 días)");

                foreach (var numero in numerosPorVencer)
                {
                    try
                    {
                        // Verificar si el usuario tiene saldo suficiente
                        decimal costoTotal = numero.CostoMensual;
                        if (numero.SMSHabilitado && numero.CostoSMS.HasValue)
                        {
                            costoTotal += numero.CostoSMS.Value;
                        }

                        bool saldoSuficiente = await saldoService.VerificarSaldoSuficiente(numero.UserId, costoTotal);

                        if (saldoSuficiente)
                        {
                            // Renovar el número por un mes más
                            await RenovarNumero(numero, costoTotal, saldoService, dbContext);

                            // Notificar al usuario que su número ha sido renovado
                            //await notificationService.EnviarNotificacion(
                            //    numero.UserId,
                            //    "Número renovado automáticamente",
                            //    $"Tu número {numero.Numero} ha sido renovado automáticamente por un mes más.",
                            //    "success");
                        }
                        else
                        {
                            // Saldo insuficiente, pero aún no vence - notificar al usuario
                            //var diasRestantes = (numero.FechaExpiracion - fechaHoy).Days;

                            //await notificationService.EnviarNotificacion(
                            //    numero.UserId,
                            //    "Saldo insuficiente para renovación",
                            //    $"Tu número {numero.Numero} vencerá en {diasRestantes} día(s). Por favor, recarga saldo para evitar que se desactive.",
                            //    "warning");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al procesar número por vencer {numero.Id}: {ex.Message}");
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar números por vencer");
                throw;
            }
        }

        private async Task RenovarNumero(NumeroTelefonico numero, decimal costoTotal, ISaldoService saldoService, ApplicationDbContext dbContext)
        {
            try
            {
                _logger.LogInformation($"Renovando número {numero.Id} ({numero.Numero}) por un mes más");

                // Utilizar una política de reintentos para operaciones críticas
                var retryPolicy = Policy
                    .Handle<DbUpdateException>()
                    .Or<DbUpdateConcurrencyException>()
                    .WaitAndRetryAsync(
                        3, // Número de reintentos
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                        (exception, timeSpan, retryCount, context) =>
                        {
                            _logger.LogWarning($"Error en BD al renovar número (intento {retryCount}): {exception.Message}");
                        }
                    );

                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Descontar saldo
                    string concepto = $"Renovación mensual - Número {numero.Numero}";
                    bool saldoDescontado = await saldoService.DescontarSaldo(
                        numero.UserId,
                        costoTotal,
                        concepto,
                        numero.Id);

                    if (saldoDescontado)
                    {
                        // Actualizar fecha de expiración
                        numero.FechaExpiracion = DateTime.UtcNow.AddMonths(1);

                        // Asegurarse que esté marcado como activo
                        numero.Activo = true;

                        // Registrar transacción exitosa
                        dbContext.Transacciones.Add(new Transaccion
                        {
                            UserId = numero.UserId,
                            NumeroTelefonicoId = numero.Id,
                            Fecha = DateTime.UtcNow,
                            Monto = costoTotal,
                            Concepto = concepto,
                            StripePaymentId = "renovacion_automatica",
                            Status = "Completado"
                        });

                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation($"Número {numero.Id} renovado exitosamente hasta {numero.FechaExpiracion}");
                    }
                    else
                    {
                        _logger.LogWarning($"No se pudo descontar saldo para renovación de número {numero.Id}");
                        throw new Exception("No se pudo descontar el saldo");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al renovar número {numero.Id}");
                throw;
            }
        }

        private async Task DesactivarNumero(NumeroTelefonico numero, ApplicationDbContext dbContext)
        {
            try
            {
                _logger.LogInformation($"Desactivando número {numero.Id} por falta de saldo");

                // Utilizar una política de reintentos para operaciones críticas
                var retryPolicy = Policy
                    .Handle<DbUpdateException>()
                    .Or<DbUpdateConcurrencyException>()
                    .WaitAndRetryAsync(
                        3, // Número de reintentos 
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                        (exception, timeSpan, retryCount, context) =>
                        {
                            _logger.LogWarning($"Error en BD al desactivar número (intento {retryCount}): {exception.Message}");
                        }
                    );

                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Desactivar el número
                    numero.Activo = false;

                    // Registrar transacción fallida
                    dbContext.Transacciones.Add(new Transaccion
                    {
                        UserId = numero.UserId,
                        NumeroTelefonicoId = numero.Id,
                        Fecha = DateTime.UtcNow,
                        Monto = numero.CostoMensual + (numero.SMSHabilitado && numero.CostoSMS.HasValue ? numero.CostoSMS.Value : 0),
                        Concepto = $"Desactivación por falta de saldo - {numero.Numero}",
                        StripePaymentId = "renovacion_fallida",
                        Status = "Fallido",
                        DetalleError = "Saldo insuficiente para renovación después del período de gracia"
                    });

                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation($"Número {numero.Id} desactivado por falta de saldo");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al desactivar número {numero.Id}");
                throw;
            }
        }
    }
}