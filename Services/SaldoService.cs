namespace TelefonicaEmpresaria.Services
{
    using global::TelefonicaEmpresarial.Infrastructure.Resilience;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage;
    using Microsoft.Extensions.DependencyInjection;
    using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
    using TelefonicaEmpresaria.Models;

    namespace TelefonicaEmpresarial.Services
    {

        public interface ISaldoService
        {
            Task<decimal> ObtenerSaldoUsuario(string userId);
            Task<bool> VerificarSaldoSuficiente(string userId, decimal montoRequerido);
            Task<bool> AgregarSaldo(string userId, decimal monto, string concepto, string referenciaExterna, IDbContextTransaction existingTransaction = null);
            Task<bool> DescontarSaldo(string userId, decimal monto, string concepto, int? numeroTelefonicoId = null);
            Task<List<MovimientoSaldo>> ObtenerMovimientosUsuario(string userId, int limite = 20);
            Task<decimal> CalcularCostoLlamada(int duracionSegundos, string pais = "MX");
            Task<bool> ExisteTransaccion(string referenciaExterna);
        }

        public class SaldoService : ISaldoService
        {
            private readonly ApplicationDbContext _context;
            private readonly ILogger<SaldoService> _logger;
            private readonly IServiceScopeFactory _serviceScopeFactory;

            public SaldoService(ApplicationDbContext context, ILogger<SaldoService> logger, IServiceScopeFactory serviceScopeFactory)
            {
                _context = context;
                _logger = logger;
                _serviceScopeFactory = serviceScopeFactory;
            }

            public async Task<decimal> ObtenerSaldoUsuario(string userId)
            {
                try
                {
                    // Buscar el registro de saldo más reciente para el usuario
                    var saldoCuenta = await _context.SaldosCuenta
                        .Where(s => s.UserId == userId)
                        .OrderByDescending(s => s.UltimaActualizacion)
                        .FirstOrDefaultAsync();

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

            public async Task<bool> AgregarSaldo(string userId, decimal monto, string concepto, string referenciaExterna, IDbContextTransaction existingTransaction = null)
            {
                if (monto <= 0)
                {
                    _logger.LogWarning($"Intento de agregar saldo negativo o cero: {monto} para usuario {userId}");
                    return false;
                }

                // Si no hay una transacción existente, crear una nueva
                IDbContextTransaction localTransaction = existingTransaction;
                bool ownsTransaction = localTransaction == null;

                try
                {
                    if (ownsTransaction)
                    {
                        localTransaction = await _context.Database.BeginTransactionAsync();
                    }

                    // Obtener el registro de saldo más reciente o crear uno nuevo
                    var saldoCuenta = await _context.SaldosCuenta
                        .Where(s => s.UserId == userId)
                        .OrderByDescending(s => s.UltimaActualizacion)
                        .FirstOrDefaultAsync();

                    if (saldoCuenta == null)
                    {
                        saldoCuenta = new SaldoCuenta
                        {
                            UserId = userId,
                            Saldo = 0,
                            UltimaActualizacion = DateTime.UtcNow
                        };

                        _context.SaldosCuenta.Add(saldoCuenta);
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

                    if (ownsTransaction)
                    {
                        await localTransaction.CommitAsync();
                    }
                    _logger.LogInformation($"Saldo agregado: {monto} para usuario {userId}, nuevo saldo: {saldoCuenta.Saldo}");

                    return true;
                }
                catch (Exception ex)
                {
                    if (ownsTransaction && localTransaction != null)
                    {
                        await localTransaction.RollbackAsync();
                    }
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
                    // Verificar si hay saldo suficiente usando el registro más reciente
                    var saldoCuenta = await _context.SaldosCuenta
                        .Where(s => s.UserId == userId)
                        .OrderByDescending(s => s.UltimaActualizacion)
                        .FirstOrDefaultAsync();

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
                try
                {
                    // Tarifas por minuto según el país de destino
                    var tarifasPorMinuto = new Dictionary<string, decimal>
        {
            { "MX", 0.20m }, // 0.20 USD por minuto en México
            { "US", 0.30m }, // 0.30 USD por minuto en EE.UU.
            { "CA", 0.30m }, // 0.30 USD por minuto en Canadá
            { "ES", 0.50m }, // 0.50 USD por minuto en España
            { "default", 0.40m } // Tarifa por defecto para otros países
        };

                    // Obtener la tarifa correspondiente al país
                    var tarifaMinuto = tarifasPorMinuto.ContainsKey(pais)
                        ? tarifasPorMinuto[pais]
                        : tarifasPorMinuto["default"];

                    // Convertir USD a MXN (aproximado)
                    tarifaMinuto = tarifaMinuto * 20.0m; // 20 pesos por dólar

                    // Obtener configuración de margen e IVA
                    using var scope = _serviceScopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var configuraciones = await dbContext.ConfiguracionesSistema
                        .Where(c => c.Clave == "MargenGananciaLlamadas" || c.Clave == "IVA")
                        .ToDictionaryAsync(c => c.Clave, c => decimal.Parse(c.Valor));

                    var margenLlamadas = configuraciones.ContainsKey("MargenGananciaLlamadas")
                        ? configuraciones["MargenGananciaLlamadas"]
                        : 4.0m; // 400% de margen por defecto

                    var iva = configuraciones.ContainsKey("IVA")
                        ? configuraciones["IVA"]
                        : 0.16m; // 16% IVA por defecto


                    decimal costoFinal;

                    if (duracionSegundos < 60)
                    {
                        // Para llamadas menores a 1 minuto, cobrar proporcionalmente
                        decimal fraccionMinuto = (decimal)duracionSegundos / 60;
                        costoFinal = tarifaMinuto * fraccionMinuto * (1 + margenLlamadas) * (1 + iva);

                        // Asegurar un mínimo rentable para llamadas muy cortas (por ejemplo, 5 segundos)
                        decimal costoMinimo = 3.0m; // Ajustar según tu modelo de negocio
                        costoFinal = Math.Max(costoFinal, costoMinimo);
                    }
                    else
                    {
                        // Para llamadas de más de 1 minuto, redondear hacia arriba
                        decimal minutos = Math.Ceiling((decimal)duracionSegundos / 60);
                        costoFinal = tarifaMinuto * minutos * (1 + margenLlamadas) * (1 + iva);
                    }

                    // Asegurar un mínimo rentable general
                    costoFinal = Math.Max(costoFinal, 5.0m); // Mínimo 5 pesos por llamada

                    // Redondear a 2 decimales
                    return Math.Ceiling(costoFinal);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al calcular costo de llamada");
                    // Usar un valor seguro que no sea demasiado bajo en caso de error
                    return Math.Ceiling((decimal)duracionSegundos / 60) * 10.0m; // Mínimo 10 pesos por minuto
                }
            }


            public async Task<bool> ExisteTransaccion(string referenciaExterna)
            {
                if (string.IsNullOrEmpty(referenciaExterna))
                    return false;

                try
                {


                    var policyContext = new Polly.Context
                    {
                        ["logger"] = _logger
                    };


                    return await PoliciasReintentos.ObtenerPoliticaDB().ExecuteAsync(
                        async (ctx) =>
                        {

                            return await _context.MovimientosSaldo
                                .AsNoTracking()
                                .AnyAsync(m => m.ReferenciaExterna == referenciaExterna);
                        },
                        policyContext
                            );

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error al verificar si existe la transacción con referencia {referenciaExterna}");
                    return false;
                }
            }
        }
    }
}
