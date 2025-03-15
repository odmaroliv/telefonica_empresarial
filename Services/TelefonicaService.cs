namespace TelefonicaEmpresaria.Services
{
    using Microsoft.EntityFrameworkCore;
    using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
    using TelefonicaEmpresaria.Models;

    namespace TelefonicaEmpresarial.Services
    {
        public interface ITelefonicaService
        {
            Task<List<PlivoNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "mx", int limite = 10);
            Task<(NumeroTelefonico? Numero, string Error)> ComprarNumero(ApplicationUser usuario, string numero, string numeroRedireccion, bool habilitarSMS);
            Task<bool> ActualizarRedireccion(int numeroId, string nuevoNumeroRedireccion);
            Task<bool> HabilitarSMS(int numeroId);
            Task<bool> DeshabilitarSMS(int numeroId);
            Task<bool> CancelarNumero(int numeroId);
            Task<List<NumeroTelefonico>> ObtenerNumerosPorUsuario(string userId);
            Task<NumeroTelefonico?> ObtenerNumeroDetalle(int numeroId);
            Task<(decimal CostoNumero, decimal CostoSMS)> ObtenerCostos(string numeroSeleccionado);
        }

        public class TelefonicaService : ITelefonicaService
        {
            private readonly ApplicationDbContext _context;
            private readonly IPlivoService _plivoService;
            private readonly IStripeService _stripeService;

            public TelefonicaService(
                ApplicationDbContext context,
                IPlivoService plivoService,
                IStripeService stripeService)
            {
                _context = context;
                _plivoService = plivoService;
                _stripeService = stripeService;
            }

            public async Task<List<PlivoNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "mx", int limite = 10)
            {
                return await _plivoService.ObtenerNumerosDisponibles(pais, limite);
            }

            public async Task<(decimal CostoNumero, decimal CostoSMS)> ObtenerCostos(string numeroSeleccionado)
            {
                // Obtener costo base del número desde Plivo
                var costoBaseNumero = await _plivoService.ObtenerCostoNumero(numeroSeleccionado);
                var costoBaseSMS = await _plivoService.ObtenerCostoSMS();

                // Obtener configuración de margen de ganancia del sistema
                var margenNumero = await _context.ConfiguracionesSistema
                    .Where(c => c.Clave == "MargenGanancia")
                    .Select(c => decimal.Parse(c.Valor))
                    .FirstOrDefaultAsync();

                var margenSMS = await _context.ConfiguracionesSistema
                    .Where(c => c.Clave == "MargenGananciaSMS")
                    .Select(c => decimal.Parse(c.Valor))
                    .FirstOrDefaultAsync();

                var iva = await _context.ConfiguracionesSistema
                    .Where(c => c.Clave == "IVA")
                    .Select(c => decimal.Parse(c.Valor))
                    .FirstOrDefaultAsync();

                // Calcular precios con margen e IVA
                decimal costoFinalNumero = costoBaseNumero * (1 + margenNumero) * (1 + iva);
                decimal costoFinalSMS = costoBaseSMS * (1 + margenSMS) * (1 + iva);

                // Redondear a 2 decimales
                costoFinalNumero = Math.Round(costoFinalNumero, 2);
                costoFinalSMS = Math.Round(costoFinalSMS, 2);

                return (costoFinalNumero, costoFinalSMS);
            }

            public async Task<(NumeroTelefonico? Numero, string Error)> ComprarNumero(
                ApplicationUser usuario,
                string numero,
                string numeroRedireccion,
                bool habilitarSMS)
            {
                try
                {
                    // 1. Verificar que el usuario tenga StripeCustomerId
                    if (string.IsNullOrEmpty(usuario.StripeCustomerId))
                    {
                        usuario.StripeCustomerId = await _stripeService.CrearClienteStripe(usuario);
                        _context.Users.Update(usuario);
                        await _context.SaveChangesAsync();
                    }

                    // 2. Comprar el número en Plivo
                    var numeroComprado = await _plivoService.ComprarNumero(numero);
                    if (numeroComprado == null)
                    {
                        return (null, "No se pudo comprar el número en Plivo");
                    }

                    // 3. Configurar redirección
                    var redireccionExitosa = await _plivoService.ConfigurarRedireccion(
                        numeroComprado.Uuid,
                        numeroRedireccion);
                    if (!redireccionExitosa)
                    {
                        // Intentar liberar el número si falla la redirección
                        await _plivoService.LiberarNumero(numeroComprado.Uuid);
                        return (null, "No se pudo configurar la redirección del número");
                    }

                    // 4. Obtener costos
                    var (costoNumero, costoSMS) = await ObtenerCostos(numero);

                    // 5. Crear sesión de checkout en Stripe
                    var checkoutSession = await _stripeService.CrearSesionCompra(
                        usuario.StripeCustomerId,
                        numero,
                        costoNumero,
                        habilitarSMS ? costoSMS : null);

                    // 6. Registrar el número en nuestra base de datos
                    var fechaActual = DateTime.UtcNow;
                    var nuevoNumero = new NumeroTelefonico
                    {
                        Numero = numero,
                        PlivoUuid = numeroComprado.Uuid,
                        UserId = usuario.Id,
                        NumeroRedireccion = numeroRedireccion,
                        FechaCompra = fechaActual,
                        FechaExpiracion = fechaActual.AddMonths(1),
                        CostoMensual = costoNumero,
                        Activo = false, // Se activa cuando se completa el pago
                        SMSHabilitado = habilitarSMS,
                        CostoSMS = habilitarSMS ? costoSMS : null
                    };

                    _context.NumerosTelefonicos.Add(nuevoNumero);
                    await _context.SaveChangesAsync();

                    // 7. Si se solicitó SMS, habilitarlo en Plivo
                    if (habilitarSMS)
                    {
                        await _plivoService.ActivarSMS(numeroComprado.Uuid);
                    }

                    // 8. Crear transacción pendiente
                    _context.Transacciones.Add(new Transaccion
                    {
                        UserId = usuario.Id,
                        NumeroTelefonicoId = nuevoNumero.Id,
                        Fecha = fechaActual,
                        Monto = habilitarSMS ? costoNumero + costoSMS : costoNumero,
                        Concepto = $"Compra de número - {numero}",
                        StripePaymentId = checkoutSession.SessionId,
                        Status = "Pendiente"
                    });

                    await _context.SaveChangesAsync();

                    return (nuevoNumero, string.Empty);
                }
                catch (Exception ex)
                {
                    return (null, $"Error al comprar número: {ex.Message}");
                }
            }

            public async Task<bool> ActualizarRedireccion(int numeroId, string nuevoNumeroRedireccion)
            {
                var numero = await _context.NumerosTelefonicos.FindAsync(numeroId);
                if (numero == null || !numero.Activo)
                {
                    return false;
                }

                var resultado = await _plivoService.ConfigurarRedireccion(numero.PlivoUuid, nuevoNumeroRedireccion);
                if (resultado)
                {
                    numero.NumeroRedireccion = nuevoNumeroRedireccion;
                    await _context.SaveChangesAsync();
                    return true;
                }

                return false;
            }

            public async Task<bool> HabilitarSMS(int numeroId)
            {
                var numero = await _context.NumerosTelefonicos.FindAsync(numeroId);
                if (numero == null || !numero.Activo || numero.SMSHabilitado)
                {
                    return false;
                }

                // Obtener costo de SMS
                var costoSMS = (await ObtenerCostos(numero.Numero)).CostoSMS;

                // Activar SMS en Plivo
                var resultado = await _plivoService.ActivarSMS(numero.PlivoUuid);
                if (!resultado)
                {
                    return false;
                }

                // Actualizar suscripción en Stripe
                if (!string.IsNullOrEmpty(numero.StripeSubscriptionId))
                {
                    var resultadoStripe = await _stripeService.AgregarSMSASuscripcion(
                        numero.StripeSubscriptionId,
                        costoSMS);

                    if (!resultadoStripe)
                    {
                        // Revertir cambios en Plivo
                        await _plivoService.DesactivarSMS(numero.PlivoUuid);
                        return false;
                    }
                }

                // Actualizar en nuestra base de datos
                numero.SMSHabilitado = true;
                numero.CostoSMS = costoSMS;
                await _context.SaveChangesAsync();

                return true;
            }

            public async Task<bool> DeshabilitarSMS(int numeroId)
            {
                var numero = await _context.NumerosTelefonicos.FindAsync(numeroId);
                if (numero == null || !numero.Activo || !numero.SMSHabilitado)
                {
                    return false;
                }

                // Desactivar SMS en Plivo
                var resultado = await _plivoService.DesactivarSMS(numero.PlivoUuid);
                if (!resultado)
                {
                    return false;
                }

                // Actualizar suscripción en Stripe
                if (!string.IsNullOrEmpty(numero.StripeSubscriptionId))
                {
                    var resultadoStripe = await _stripeService.QuitarSMSDeSuscripcion(
                        numero.StripeSubscriptionId);

                    if (!resultadoStripe)
                    {
                        // Revertir cambios en Plivo
                        await _plivoService.ActivarSMS(numero.PlivoUuid);
                        return false;
                    }
                }

                // Actualizar en nuestra base de datos
                numero.SMSHabilitado = false;
                numero.CostoSMS = null;
                await _context.SaveChangesAsync();

                return true;
            }

            public async Task<bool> CancelarNumero(int numeroId)
            {
                var numero = await _context.NumerosTelefonicos.FindAsync(numeroId);
                if (numero == null)
                {
                    return false;
                }

                // Cancelar suscripción en Stripe
                if (!string.IsNullOrEmpty(numero.StripeSubscriptionId))
                {
                    await _stripeService.CancelarSuscripcion(numero.StripeSubscriptionId);
                }

                // Liberar número en Plivo
                await _plivoService.LiberarNumero(numero.PlivoUuid);

                // Actualizar en nuestra base de datos
                numero.Activo = false;
                await _context.SaveChangesAsync();

                return true;
            }

            public async Task<List<NumeroTelefonico>> ObtenerNumerosPorUsuario(string userId)
            {
                return await _context.NumerosTelefonicos
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.FechaCompra)
                    .ToListAsync();
            }

            public async Task<NumeroTelefonico?> ObtenerNumeroDetalle(int numeroId)
            {
                return await _context.NumerosTelefonicos
                    .Include(n => n.LogsLlamadas)
                    .Include(n => n.LogsSMS)
                    .FirstOrDefaultAsync(n => n.Id == numeroId);
            }
        }
    }
}
