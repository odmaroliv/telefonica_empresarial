using Microsoft.EntityFrameworkCore;
using Quartz;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresarial.Services;

public class PendingOrdersJob : IJob
{
    private readonly ISMSPoolService _smsPoolService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PendingOrdersJob> _logger;

    public PendingOrdersJob(
        ISMSPoolService smsPoolService,
        ApplicationDbContext context,
        ILogger<PendingOrdersJob> logger)
    {
        _smsPoolService = smsPoolService;
        _context = context;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("Iniciando job de resolución de órdenes pendientes");

            // Obtener todos los usuarios con órdenes pendientes
            var usuariosConPendientes = await _context.SMSPoolNumeros
                .Where(n => n.Estado == "Pendiente")
                .Select(n => n.UserId)
                .Distinct()
                .ToListAsync();

            foreach (var userId in usuariosConPendientes)
            {
                await _smsPoolService.ResolverNumerosPendientes(userId);
            }

            _logger.LogInformation("Job de resolución de órdenes pendientes completado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en job de resolución de órdenes pendientes");
        }
    }
}