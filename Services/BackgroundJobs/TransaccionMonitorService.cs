using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;

namespace TelefonicaEmpresaria.Services.BackgroundJobs
{
    public interface ITransaccionMonitorService
    {
        Task RegistrarInicioTransaccion(string tipoOperacion, string referenciaExterna, string userId, decimal monto, string datosRequest = null);
        Task ActualizarEstadoTransaccion(string referenciaExterna, string estado, string detalleError = null);
        Task<List<TransaccionAuditoria>> ObtenerTransaccionesPendientes(int horasAtras = 24);
        Task<TransaccionAuditoria> ObtenerTransaccion(string referenciaExterna);
        Task<List<TransaccionAuditoria>> ObtenerTodasLasTransacciones(int horasAtras = 24);
    }
    public class TransaccionMonitorService : ITransaccionMonitorService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TransaccionMonitorService> _logger;

        public TransaccionMonitorService(ApplicationDbContext context, ILogger<TransaccionMonitorService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task RegistrarInicioTransaccion(string tipoOperacion, string referenciaExterna, string userId, decimal monto, string datosRequest = null)
        {
            try
            {
                // Verificar si ya existe
                var transaccionExistente = await _context.TransaccionesAuditoria
                    .FirstOrDefaultAsync(t => t.ReferenciaExterna == referenciaExterna);

                if (transaccionExistente != null)
                {
                    _logger.LogInformation($"Transacción {referenciaExterna} ya registrada anteriormente");
                    return;
                }

                // Registrar nueva transacción
                var nuevaTransaccion = new TransaccionAuditoria
                {
                    TipoOperacion = tipoOperacion,
                    ReferenciaExterna = referenciaExterna,
                    UserId = userId,
                    Monto = monto,
                    Estado = "Iniciada",
                    FechaCreacion = DateTime.UtcNow,
                    DatosRequest = datosRequest,
                    DetalleError = ""
                };

                _context.TransaccionesAuditoria.Add(nuevaTransaccion);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Transacción {referenciaExterna} registrada: {tipoOperacion} para usuario {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al registrar inicio de transacción {referenciaExterna}");
                // No lanzar excepción para no interrumpir el flujo principal
            }
        }
        public async Task<List<TransaccionAuditoria>> ObtenerTodasLasTransacciones(int horasAtras = 24)
        {
            try
            {
                var fechaLimite = DateTime.UtcNow.AddHours(-horasAtras);

                return await _context.TransaccionesAuditoria
                    .Where(t => t.FechaCreacion >= fechaLimite)  // Sin filtro de estados
                    .OrderByDescending(t => t.FechaCreacion)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todas las transacciones");
                return new List<TransaccionAuditoria>();
            }
        }
        public async Task ActualizarEstadoTransaccion(string referenciaExterna, string estado, string detalleError = null)
        {
            try
            {
                var transaccion = await _context.TransaccionesAuditoria
                    .FirstOrDefaultAsync(t => t.ReferenciaExterna == referenciaExterna);

                if (transaccion == null)
                {
                    _logger.LogWarning($"No se encontró la transacción {referenciaExterna} para actualizar estado");
                    return;
                }

                transaccion.Estado = estado;

                if (!string.IsNullOrEmpty(detalleError))
                {
                    transaccion.DetalleError = detalleError;
                }

                transaccion.FechaActualizacion = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Transacción {referenciaExterna} actualizada a estado: {estado}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar estado de transacción {referenciaExterna}");
            }
        }

        public async Task<List<TransaccionAuditoria>> ObtenerTransaccionesPendientes(int horasAtras = 24)
        {
            try
            {
                var fechaLimite = DateTime.UtcNow.AddHours(-horasAtras);

                return await _context.TransaccionesAuditoria
                    .Where(t => t.Estado != "Completada" && t.Estado != "Fallida" && t.FechaCreacion >= fechaLimite)
                    .OrderByDescending(t => t.FechaCreacion)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener transacciones pendientes");
                return new List<TransaccionAuditoria>();
            }
        }

        public async Task<TransaccionAuditoria> ObtenerTransaccion(string referenciaExterna)
        {
            try
            {
                return await _context.TransaccionesAuditoria
                    .FirstOrDefaultAsync(t => t.ReferenciaExterna == referenciaExterna);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener transacción {referenciaExterna}");
                return null;
            }
        }
    }
}
