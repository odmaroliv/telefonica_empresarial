using Quartz;
using TelefonicaEmpresaria.Services;

public class LlamadasMonitorJob : IJob
{
    private readonly ILlamadasService _llamadasService;
    private readonly ILogger<LlamadasMonitorJob> _logger;

    public LlamadasMonitorJob(ILlamadasService llamadasService, ILogger<LlamadasMonitorJob> logger)
    {
        _llamadasService = llamadasService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Ejecutando job de monitoreo de llamadas");
        await _llamadasService.FinalizarLlamadasAbandonadas();
    }
}