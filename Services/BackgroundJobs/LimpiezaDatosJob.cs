using Microsoft.EntityFrameworkCore;
using Quartz;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class LimpiezaDatosJob : IJob
    {
        private readonly ILogger<LimpiezaDatosJob> _logger;
        private readonly IServiceProvider _serviceProvider;

        public LimpiezaDatosJob(
            ILogger<LimpiezaDatosJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando job de limpieza de datos");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                // 1. Limpiar eventos de webhook antiguos
                var fechaLimiteEventos = DateTime.UtcNow.AddMonths(-1);
                var eventosAntiguos = await dbContext.EventosWebhook
                    .Where(e => e.Completado && e.FechaRecibido < fechaLimiteEventos)
                    .Take(1000) // Procesar en lotes para evitar bloqueos largos
                    .ToListAsync();

                if (eventosAntiguos.Any())
                {
                    dbContext.EventosWebhook.RemoveRange(eventosAntiguos);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"Se eliminaron {eventosAntiguos.Count} eventos de webhook antiguos");
                }

                // 2. Eliminar logs de llamadas antiguos (mayores a 6 meses)
                var fechaLimiteLlamadas = DateTime.UtcNow.AddMonths(-6);
                var llamadasAntiguas = await dbContext.LogsLlamadas
                    .Where(l => l.FechaHora < fechaLimiteLlamadas)
                    .Take(1000)
                    .ToListAsync();

                if (llamadasAntiguas.Any())
                {
                    dbContext.LogsLlamadas.RemoveRange(llamadasAntiguas);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"Se eliminaron {llamadasAntiguas.Count} logs de llamadas antiguos");
                }

                // 3. Eliminar logs de SMS antiguos (mayores a 6 meses)
                var fechaLimiteSMS = DateTime.UtcNow.AddMonths(-6);
                var smsAntiguos = await dbContext.LogsSMS
                    .Where(s => s.FechaHora < fechaLimiteSMS)
                    .Take(1000)
                    .ToListAsync();

                if (smsAntiguos.Any())
                {
                    dbContext.LogsSMS.RemoveRange(smsAntiguos);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"Se eliminaron {smsAntiguos.Count} logs de SMS antiguos");
                }

                _logger.LogInformation("Job de limpieza de datos completado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de limpieza de datos");
            }
        }
    }
}
