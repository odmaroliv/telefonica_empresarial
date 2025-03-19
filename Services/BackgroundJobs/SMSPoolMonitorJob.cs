using Microsoft.EntityFrameworkCore;
using Quartz;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class SMSPoolMonitorJob : IJob
    {
        private readonly ILogger<SMSPoolMonitorJob> _logger;
        private readonly IServiceProvider _serviceProvider;

        public SMSPoolMonitorJob(
            ILogger<SMSPoolMonitorJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando monitoreo de verificaciones SMSPool");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var smsPoolService = scope.ServiceProvider.GetRequiredService<ISMSPoolService>();

            try
            {
                await VerificarNuevosMensajes(dbContext, smsPoolService);
                await MarcarNumerosExpirados(dbContext);

                _logger.LogInformation("Monitoreo de verificaciones SMSPool completado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en monitoreo de verificaciones SMSPool");
            }
        }

        private async Task VerificarNuevosMensajes(ApplicationDbContext dbContext, ISMSPoolService smsPoolService)
        {
            try
            {
                // Obtener números activos sin SMS recibidos
                var numerosActivos = await dbContext.SMSPoolNumeros
                    .Where(n => n.Estado == "Activo" && !n.SMSRecibido)
                    .OrderBy(n => n.FechaUltimaComprobacion ?? DateTime.MinValue)
                    .Take(20) // Procesar en lotes de 20
                    .ToListAsync();

                _logger.LogInformation($"Verificando mensajes para {numerosActivos.Count} números activos");

                foreach (var numero in numerosActivos)
                {
                    try
                    {
                        // Verificar con la API
                        await smsPoolService.VerificarNuevosMensajes(numero.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al verificar mensajes para número ID {numero.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar nuevos mensajes");
                throw;
            }
        }

        private async Task MarcarNumerosExpirados(ApplicationDbContext dbContext)
        {
            try
            {
                var ahora = DateTime.UtcNow;

                // Obtener números expirados pero aún marcados como activos
                var numerosExpirados = await dbContext.SMSPoolNumeros
                    .Where(n => n.Estado == "Activo" && n.FechaExpiracion < ahora)
                    .ToListAsync();

                _logger.LogInformation($"Marcando {numerosExpirados.Count} números como expirados");

                foreach (var numero in numerosExpirados)
                {
                    numero.Estado = "Expirado";
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar números expirados");
                throw;
            }
        }
    }
}