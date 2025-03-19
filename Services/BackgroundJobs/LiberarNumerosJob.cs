using Microsoft.EntityFrameworkCore;
using Quartz;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class LiberarNumerosJob : IJob
    {
        private readonly ILogger<LiberarNumerosJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private const int TAMANO_LOTE = 50; // Procesar en lotes de 50 números

        public LiberarNumerosJob(
            ILogger<LiberarNumerosJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando job de liberación de números cancelados");

            // Crear scope para acceder a servicios scoped
            using var scope = _serviceProvider.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var twilioService = scope.ServiceProvider.GetRequiredService<ITwilioService>();

            try
            {
                await LiberarNumerosCancelados(dbContext, twilioService);
                _logger.LogInformation("Job de liberación de números cancelados completado exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de liberación de números cancelados");
            }
        }

        private async Task LiberarNumerosCancelados(
            ApplicationDbContext dbContext,
            ITwilioService twilioService)
        {
            try
            {
                var fechaActual = DateTime.UtcNow;

                // Buscar números:
                // 1. Marcados como inactivos (cancelados)
                // 2. Cuya fecha de expiración ya pasó
                // 3. Y que todavía no han sido liberados (PlivoUuid no es "liberado")
                var numerosParaLiberar = await dbContext.NumerosTelefonicos
                    .Where(n => !n.Activo &&
                           n.FechaExpiracion < fechaActual &&
                           n.PlivoUuid != "liberado" &&
                           n.PlivoUuid != "pendiente")
                    .OrderBy(n => n.FechaExpiracion) // Liberar primero los más antiguos
                    .Take(TAMANO_LOTE)
                    .ToListAsync();

                _logger.LogInformation($"Se encontraron {numerosParaLiberar.Count} números para liberar");

                foreach (var numero in numerosParaLiberar)
                {
                    try
                    {
                        // Verificar si el número ya existe en Twilio
                        bool numeroExisteEnTwilio = await twilioService.VerificarNumeroActivo(numero.PlivoUuid);

                        if (numeroExisteEnTwilio)
                        {
                            // Liberar el número en Twilio
                            bool liberado = await twilioService.LiberarNumero(numero.PlivoUuid);

                            if (liberado)
                            {
                                _logger.LogInformation($"Número {numero.Numero} (ID: {numero.Id}) liberado correctamente en Twilio");
                            }
                            else
                            {
                                _logger.LogWarning($"No se pudo liberar el número {numero.Numero} (ID: {numero.Id}) en Twilio");
                                continue; // Pasar al siguiente sin actualizar nuestro registro
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"Número {numero.Numero} (ID: {numero.Id}) ya no existe en Twilio o no pudo ser verificado");
                        }

                        // Actualizar en nuestra base de datos, marcando el número como liberado
                        numero.PlivoUuid = "liberado";
                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation($"Número {numero.Numero} (ID: {numero.Id}) marcado como liberado en base de datos");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al liberar número {numero.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al liberar números cancelados");
                throw;
            }
        }
    }
}