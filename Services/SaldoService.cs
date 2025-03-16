namespace TelefonicaEmpresaria.Services
{
    using Microsoft.EntityFrameworkCore;
    using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
    using TelefonicaEmpresaria.Models;

    namespace TelefonicaEmpresarial.Services
    {
        public interface ISaldoService
        {
            Task<decimal> ObtenerSaldoUsuario(string userId);
            Task<bool> VerificarSaldoSuficiente(string userId, decimal montoRequerido);
            Task<bool> AgregarSaldo(string userId, decimal monto, string concepto, string referenciaExterna);
            Task<bool> DescontarSaldo(string userId, decimal monto, string concepto, int? numeroTelefonicoId = null);
            Task<List<MovimientoSaldo>> ObtenerMovimientosUsuario(string userId, int limite = 20);
            Task<decimal> CalcularCostoLlamada(int duracionSegundos, string pais = "MX");
            Task<bool> ExisteTransaccion(string referenciaExterna);
        }

        public class SaldoService : ISaldoService
        {
            private readonly ApplicationDbContext _context;
            private readonly ILogger<SaldoService> _logger;

            public SaldoService(ApplicationDbContext context, ILogger<SaldoService> logger)
            {
                _context = context;
                _logger = logger;
            }

            public async Task<decimal> ObtenerSaldoUsuario(string userId)
            {
                try
                {
                    var saldoCuenta = await _context.SaldosCuenta
                        .FirstOrDefaultAsync(s => s.UserId == userId);

                    if (saldoCuenta == null)
                    {
                        // Crear nuevo saldo si no existe
                        saldoCuenta = new SaldoCuenta
                        {
                            UserId = userId,
                            Saldo = 0,
                            UltimaActualizacion = DateTime.UtcNow
                        };

                        _context.SaldosCuenta.Add(saldoCuenta);
                        await _context.SaveChangesAsync();
                    }

                    return saldoCuenta.Saldo;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error al obtener saldo del usuario {userId}");
                    throw;
                }
            }

            public async Task<bool> VerificarSaldoSuficiente(string userId, decimal montoRequerido)
            {
                var saldoActual = await ObtenerSaldoUsuario(userId);
                return saldoActual >= montoRequerido;
            }

            public async Task<bool> AgregarSaldo(string userId, decimal monto, string concepto, string referenciaExterna)
            {
                if (monto <= 0)
                {
                    _logger.LogWarning($"Intento de agregar saldo negativo o cero: {monto} para usuario {userId}");
                    return false;
                }

                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Obtener o crear saldo
                    var saldoCuenta = await _context.SaldosCuenta
                        .FirstOrDefaultAsync(s => s.UserId == userId);

                    if (saldoCuenta == null)
                    {
                        saldoCuenta = new SaldoCuenta
                        {
                            UserId = userId,
                            Saldo = 0,
                            UltimaActualizacion = DateTime.UtcNow
                        };

                        _context.SaldosCuenta.Add(saldoCuenta);
                        await _context.SaveChangesAsync();
                    }

                    // Actualizar saldo
                    saldoCuenta.Saldo += monto;
                    saldoCuenta.UltimaActualizacion = DateTime.UtcNow;

                    // Registrar el movimiento
                    var movimiento = new MovimientoSaldo
                    {
                        UserId = userId,
                        Monto = monto,
                        Concepto = concepto,
                        Fecha = DateTime.UtcNow,
                        TipoMovimiento = "Recarga",
                        ReferenciaExterna = referenciaExterna
                    };

                    _context.MovimientosSaldo.Add(movimiento);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Saldo agregado: {monto} para usuario {userId}, nuevo saldo: {saldoCuenta.Saldo}");
                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Error al agregar saldo ({monto}) para usuario {userId}");
                    throw;
                }
            }

            public async Task<bool> DescontarSaldo(string userId, decimal monto, string concepto, int? numeroTelefonicoId = null)
            {
                if (monto <= 0)
                {
                    _logger.LogWarning($"Intento de descontar saldo negativo o cero: {monto} para usuario {userId}");
                    return false;
                }

                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Verificar si hay saldo suficiente
                    var saldoCuenta = await _context.SaldosCuenta
                        .FirstOrDefaultAsync(s => s.UserId == userId);

                    if (saldoCuenta == null || saldoCuenta.Saldo < monto)
                    {
                        _logger.LogWarning($"Saldo insuficiente para usuario {userId}. Requerido: {monto}, Disponible: {saldoCuenta?.Saldo ?? 0}");
                        return false;
                    }

                    // Actualizar saldo
                    saldoCuenta.Saldo -= monto;
                    saldoCuenta.UltimaActualizacion = DateTime.UtcNow;

                    // Registrar el movimiento
                    var movimiento = new MovimientoSaldo
                    {
                        UserId = userId,
                        Monto = monto,
                        Concepto = concepto,
                        Fecha = DateTime.UtcNow,
                        TipoMovimiento = "Consumo",
                        NumeroTelefonicoId = numeroTelefonicoId
                    };

                    _context.MovimientosSaldo.Add(movimiento);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Saldo descontado: {monto} para usuario {userId}, nuevo saldo: {saldoCuenta.Saldo}");
                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Error al descontar saldo ({monto}) para usuario {userId}");
                    throw;
                }
            }

            public async Task<List<MovimientoSaldo>> ObtenerMovimientosUsuario(string userId, int limite = 20)
            {
                try
                {
                    return await _context.MovimientosSaldo
                        .Where(m => m.UserId == userId)
                        .OrderByDescending(m => m.Fecha)
                        .Take(limite)
                        .ToListAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error al obtener movimientos para usuario {userId}");
                    throw;
                }
            }

            public async Task<decimal> CalcularCostoLlamada(int duracionSegundos, string pais = "MX")
            {
                // Tarifas por minuto según el país de destino
                var tarifasPorMinuto = new Dictionary<string, decimal>
            {
                { "MX", 0.20m }, // 0.20 MXN por minuto en México
                { "US", 0.30m }, // 0.30 MXN por minuto en EE.UU.
                { "CA", 0.30m }, // 0.30 MXN por minuto en Canadá
                { "ES", 0.50m }, // 0.50 MXN por minuto en España
                { "default", 0.40m } // Tarifa por defecto para otros países
            };

                // Obtener la tarifa correspondiente al país
                var tarifaMinuto = tarifasPorMinuto.ContainsKey(pais)
                    ? tarifasPorMinuto[pais]
                    : tarifasPorMinuto["default"];

                // Convertir segundos a minutos y calcular el costo
                decimal minutos = (decimal)duracionSegundos / 60;

                // Redondear hacia arriba al minuto más cercano
                minutos = Math.Ceiling(minutos);

                return minutos * tarifaMinuto;
            }
            // Añadir un método para verificar si una transacción ya existe
            public async Task<bool> ExisteTransaccion(string referenciaExterna)
            {
                if (string.IsNullOrEmpty(referenciaExterna))
                    return false;

                return await _context.MovimientosSaldo
                    .AnyAsync(m => m.ReferenciaExterna == referenciaExterna);
            }
        }
    }
}
