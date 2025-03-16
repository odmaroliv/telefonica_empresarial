using Microsoft.EntityFrameworkCore;
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

            try
            {
                // Obtener números a punto de expirar (en los próximos 2 días)
                var fechaExpiracion = DateTime.UtcNow.AddDays(2);
                var numerosAExpirar = await dbContext.NumerosTelefonicos
                    .Where(n => n.Activo && n.FechaExpiracion <= fechaExpiracion)
                    .Include(n => n.Usuario)
                    .ToListAsync();

                _logger.LogInformation($"Se encontraron {numerosAExpirar.Count} números a punto de expirar");

                foreach (var numero in numerosAExpirar)
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
                        }
                        else
                        {
                            // Saldo insuficiente, desactivar el número
                            await DesactivarNumero(numero, dbContext);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al procesar número {numero.Id}: {ex.Message}");
                    }
                }

                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Job de renovación de números completado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de renovación de números");
            }
        }

        private async Task RenovarNumero(NumeroTelefonico numero, decimal costoTotal, ISaldoService saldoService, ApplicationDbContext dbContext)
        {
            try
            {
                _logger.LogInformation($"Renovando número {numero.Id} ({numero.Numero}) por un mes más");

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

                    _logger.LogInformation($"Número {numero.Id} renovado exitosamente hasta {numero.FechaExpiracion}");
                }
                else
                {
                    _logger.LogWarning($"No se pudo descontar saldo para renovación de número {numero.Id}");
                    await DesactivarNumero(numero, dbContext);
                }
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

                // Desactivar el número
                numero.Activo = false;

                // Registrar transacción fallida
                dbContext.Transacciones.Add(new Transaccion
                {
                    UserId = numero.UserId,
                    NumeroTelefonicoId = numero.Id,
                    Fecha = DateTime.UtcNow,
                    Monto = numero.CostoMensual + (numero.SMSHabilitado && numero.CostoSMS.HasValue ? numero.CostoSMS.Value : 0),
                    Concepto = $"Intento de renovación fallido por falta de saldo - {numero.Numero}",
                    StripePaymentId = "renovacion_fallida",
                    Status = "Fallido",
                    DetalleError = "Saldo insuficiente para renovación"
                });

                _logger.LogInformation($"Número {numero.Id} desactivado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al desactivar número {numero.Id}");
                throw;
            }
        }
    }
}