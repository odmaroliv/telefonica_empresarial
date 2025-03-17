using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.Checkout;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Services
{
    public interface IStripeService
    {
        Task<string> CrearClienteStripe(ApplicationUser usuario);
        Task<StripeCheckoutSession> CrearSesionCompra(string customerId, string numeroTelefono, decimal costoMensual, decimal? costoSMS = null);
        Task<bool> VerificarPagoCompletado(string sessionId);
        Task<string> CrearSuscripcion(string customerId, string nombrePlan, decimal montoPlan, string descripcion);
        Task<bool> CancelarSuscripcion(string subscriptionId);
        Task<bool> ActualizarSuscripcion(string subscriptionId, decimal nuevoCosto);
        Task<bool> AgregarSMSASuscripcion(string subscriptionId, decimal costoSMS);
        Task<bool> QuitarSMSDeSuscripcion(string subscriptionId);
        Task ProcesarEventoWebhook(string json, string signatureHeader, CancellationToken cancellationToken = default);
        Task<string?> ObtenerURLPago(string sessionId);

        //saldo
        Task<StripeCheckoutSession> CrearSesionRecarga(string customerId, decimal monto);
        Task<Stripe.Checkout.Session> ObtenerDetallesSesion(string sessionId);




    }

    public class StripeService : IStripeService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StripeService> _logger;
        private readonly string _apiKey;
        private readonly string _webhookSecret;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IServiceProvider _serviceProvider;

        public StripeService(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<StripeService> logger,
            IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;
            _apiKey = _configuration["Stripe:SecretKey"] ?? throw new ArgumentNullException("Stripe:SecretKey");
            _webhookSecret = _configuration["Stripe:WebhookSecret"] ?? throw new ArgumentNullException("Stripe:WebhookSecret");

            // Configurar la API de Stripe
            StripeConfiguration.ApiKey = _apiKey;

            // Configurar política de reintentos
            _retryPolicy = Polly.Policy
                .Handle<StripeException>(ex =>
                    ex.StripeError?.Type == "api_connection_error" || // Error de conexión
                    ex.StripeError?.Type == "api_error" || // Error de API
                    ex.StripeError?.Type == "rate_limit_error") // Error de límite de tasa
                .WaitAndRetryAsync(
                    3, // Número de reintentos
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning($"Error en Stripe (intento {retryCount}): {exception.Message}. Reintentando en {timeSpan.TotalSeconds} segundos.");
                    }
                );
            _serviceProvider = serviceProvider;
        }

        public async Task<string> CrearClienteStripe(ApplicationUser usuario)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Creando cliente en Stripe para usuario: {usuario.Id}");

                    var options = new CustomerCreateOptions
                    {
                        Email = usuario.Email,
                        Name = $"{usuario.Nombre} {usuario.Apellidos}".Trim(),
                        Phone = usuario.PhoneNumber,
                        Address = new AddressOptions
                        {
                            Line1 = usuario.Direccion,
                            City = usuario.Ciudad,
                            PostalCode = usuario.CodigoPostal,
                            Country = usuario.Pais
                        },
                        Metadata = new Dictionary<string, string>
                        {
                            { "UserId", usuario.Id },
                            { "RFC", usuario.RFC ?? "Sin RFC" }
                        }
                    };

                    var service = new CustomerService();
                    var customer = await service.CreateAsync(options);

                    _logger.LogInformation($"Cliente creado en Stripe. ID: {customer.Id}");

                    return customer.Id;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al crear cliente: {ex.Message}, Tipo: {ex.StripeError?.Type}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al crear cliente en Stripe: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<StripeCheckoutSession> CrearSesionCompra(string customerId, string numeroTelefono, decimal costoMensual, decimal? costoSMS = null)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Creando sesión de compra para número {numeroTelefono}. Cliente: {customerId}");

                    var lineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                UnitAmount = (long)(costoMensual * 100), // Convertir a centavos
                                Currency = "mxn",
                                Recurring = new SessionLineItemPriceDataRecurringOptions
                                {
                                    Interval = "month",
                                },
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = $"Número Empresarial: {numeroTelefono}",
                                    Description = "Suscripción mensual para número empresarial"
                                }
                            },
                            Quantity = 1
                        }
                    };

                    // Si se incluye el servicio SMS, añadir como item adicional
                    if (costoSMS.HasValue && costoSMS.Value > 0)
                    {
                        lineItems.Add(new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                UnitAmount = (long)(costoSMS.Value * 100), // Convertir a centavos
                                Currency = "mxn",
                                Recurring = new SessionLineItemPriceDataRecurringOptions
                                {
                                    Interval = "month",
                                },
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = $"Servicio SMS para: {numeroTelefono}",
                                    Description = "Recepción de mensajes SMS para autenticación"
                                }
                            },
                            Quantity = 1
                        });
                    }

                    var appUrl = _configuration["AppUrl"] ?? "https://localhost:7019";

                    var options = new SessionCreateOptions
                    {
                        Customer = customerId,
                        PaymentMethodTypes = new List<string> { "card" },
                        LineItems = lineItems,
                        Mode = "subscription",
                        SuccessUrl = $"{appUrl}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
                        CancelUrl = $"{appUrl}/checkout/cancel",
                        Metadata = new Dictionary<string, string>
                        {
                            { "NumeroTelefono", numeroTelefono },
                            { "IncluirSMS", costoSMS.HasValue ? "true" : "false" }
                        }
                    };

                    var service = new SessionService();
                    var session = await service.CreateAsync(options);

                    _logger.LogInformation($"Sesión de compra creada. ID: {session.Id}");

                    return new StripeCheckoutSession
                    {
                        SessionId = session.Id,
                        Url = session.Url
                    };
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al crear sesión de pago: {ex.Message}, Tipo: {ex.StripeError?.Type}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al crear sesión de pago: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<string?> ObtenerURLPago(string sessionId)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Obteniendo URL de pago para la sesión: {sessionId}");

                    var sessionService = new SessionService();
                    var session = await sessionService.GetAsync(sessionId);

                    if (session != null)
                    {
                        _logger.LogInformation($"URL de pago obtenida: {session.Url}");
                        return session.Url;
                    }

                    _logger.LogWarning($"No se encontró la sesión {sessionId}");
                    return null;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al obtener URL de pago: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al obtener URL de pago: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> VerificarPagoCompletado(string sessionId)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Verificando estado de pago para sesión: {sessionId}");

                    var service = new SessionService();
                    var session = await service.GetAsync(sessionId);

                    bool completado = session.PaymentStatus == "paid";
                    _logger.LogInformation($"Estado de pago para sesión {sessionId}: {session.PaymentStatus}");

                    return completado;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al verificar pago: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al verificar pago: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<string> CrearSuscripcion(string customerId, string nombrePlan, decimal montoPlan, string descripcion)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Creando suscripción para cliente {customerId}: {nombrePlan}");

                    // Crear producto
                    var productoService = new ProductService();
                    var producto = await productoService.CreateAsync(new ProductCreateOptions
                    {
                        Name = nombrePlan,
                        Description = descripcion
                    });

                    // Crear precio
                    var precioService = new PriceService();
                    var precio = await precioService.CreateAsync(new PriceCreateOptions
                    {
                        Product = producto.Id,
                        UnitAmount = (long)(montoPlan * 100), // Convertir a centavos
                        Currency = "mxn",
                        Recurring = new PriceRecurringOptions
                        {
                            Interval = "month"
                        }
                    });

                    // Crear suscripción
                    var suscripcionService = new SubscriptionService();
                    var suscripcion = await suscripcionService.CreateAsync(new SubscriptionCreateOptions
                    {
                        Customer = customerId,
                        Items = new List<SubscriptionItemOptions>
                        {
                            new SubscriptionItemOptions
                            {
                                Price = precio.Id
                            }
                        }
                    });

                    _logger.LogInformation($"Suscripción creada. ID: {suscripcion.Id}");

                    return suscripcion.Id;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al crear suscripción: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al crear suscripción: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> CancelarSuscripcion(string subscriptionId)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Cancelando suscripción: {subscriptionId}");

                    var service = new SubscriptionService();
                    await service.CancelAsync(subscriptionId, new SubscriptionCancelOptions
                    {
                        InvoiceNow = true,
                        Prorate = true
                    });

                    _logger.LogInformation($"Suscripción {subscriptionId} cancelada correctamente");

                    return true;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al cancelar suscripción: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al cancelar suscripción: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> ActualizarSuscripcion(string subscriptionId, decimal nuevoCosto)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Actualizando suscripción {subscriptionId} a nuevo costo: {nuevoCosto}");

                    // Obtener la suscripción
                    var subscriptionService = new SubscriptionService();
                    var subscription = await subscriptionService.GetAsync(subscriptionId);
                    var itemId = subscription.Items.Data[0].Id;

                    // Crear un nuevo precio
                    var productoId = subscription.Items.Data[0].Price.ProductId;
                    var precioService = new PriceService();
                    var nuevoPrecio = await precioService.CreateAsync(new PriceCreateOptions
                    {
                        Product = productoId,
                        UnitAmount = (long)(nuevoCosto * 100), // Convertir a centavos
                        Currency = "mxn",
                        Recurring = new PriceRecurringOptions
                        {
                            Interval = "month"
                        }
                    });

                    // Actualizar el item de la suscripción
                    var itemService = new SubscriptionItemService();
                    await itemService.UpdateAsync(itemId, new SubscriptionItemUpdateOptions
                    {
                        Price = nuevoPrecio.Id
                    });

                    _logger.LogInformation($"Suscripción {subscriptionId} actualizada correctamente al nuevo precio");

                    return true;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al actualizar suscripción: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al actualizar suscripción: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> AgregarSMSASuscripcion(string subscriptionId, decimal costoSMS)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Agregando servicio SMS a suscripción {subscriptionId} con costo: {costoSMS}");

                    // Crear producto para SMS
                    var productoService = new ProductService();
                    var producto = await productoService.CreateAsync(new ProductCreateOptions
                    {
                        Name = "Servicio SMS",
                        Description = "Recepción de mensajes SMS para autenticación"
                    });

                    // Crear precio para SMS
                    var precioService = new PriceService();
                    var precio = await precioService.CreateAsync(new PriceCreateOptions
                    {
                        Product = producto.Id,
                        UnitAmount = (long)(costoSMS * 100), // Convertir a centavos
                        Currency = "mxn",
                        Recurring = new PriceRecurringOptions
                        {
                            Interval = "month"
                        }
                    });

                    // Añadir a la suscripción
                    var itemService = new SubscriptionItemService();
                    await itemService.CreateAsync(new SubscriptionItemCreateOptions
                    {
                        Subscription = subscriptionId,
                        Price = precio.Id,
                        Quantity = 1
                    });

                    _logger.LogInformation($"Servicio SMS agregado correctamente a la suscripción {subscriptionId}");

                    return true;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al agregar SMS a suscripción: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al agregar SMS a suscripción: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<bool> QuitarSMSDeSuscripcion(string subscriptionId)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Quitando servicio SMS de suscripción {subscriptionId}");

                    // Obtener la suscripción
                    var subscriptionService = new SubscriptionService();
                    var subscription = await subscriptionService.GetAsync(subscriptionId);

                    // Buscar el item de SMS (asumimos que es el segundo item, el primero es el número)
                    if (subscription.Items.Data.Count > 1)
                    {
                        var itemId = subscription.Items.Data[1].Id;
                        var itemService = new SubscriptionItemService();
                        await itemService.DeleteAsync(itemId);

                        _logger.LogInformation($"Servicio SMS eliminado correctamente de la suscripción {subscriptionId}");
                    }
                    else
                    {
                        _logger.LogWarning($"No se encontró el servicio SMS en la suscripción {subscriptionId}");
                    }

                    return true;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al quitar SMS de suscripción: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al quitar SMS de suscripción: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task ProcesarEventoWebhook(string json, string signatureHeader, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Procesando webhook de Stripe");

                // Verificar que el webhook es auténtico con manejo de errores mejorado
                Event stripeEvent;
                try
                {
                    stripeEvent = EventUtility.ConstructEvent(
                        json,
                        signatureHeader,
                        _webhookSecret,
                        300 // Tolerancia de 5 minutos para diferencias de reloj
                    );
                }
                catch (StripeException ex) when (ex.StripeError?.Type == "signature_verification_failure")
                {
                    _logger.LogWarning(ex, "Verificación de firma de Stripe fallida");
                    throw; // Propagar para manejar en el controller
                }

                _logger.LogInformation($"Evento Stripe recibido de tipo: {stripeEvent.Type}, ID: {stripeEvent.Id}");

                // Verificar si el evento ya fue procesado (idempotencia)
                bool yaFueProcesado = await VerificarEventoProcesado(stripeEvent.Id);
                if (yaFueProcesado)
                {
                    _logger.LogInformation($"Evento Stripe {stripeEvent.Id} ya fue procesado anteriormente");
                    return;
                }

                // Registrar que empezamos a procesar este evento
                await RegistrarProcesamientoEvento(stripeEvent.Id);


                // Manejar eventos relevantes
                switch (stripeEvent.Type)
                {
                    case "invoice.paid":
                        var invoice = stripeEvent.Data.Object as Invoice;
                        await ManejarPagoExitoso(invoice);
                        break;

                    case "invoice.payment_failed":
                        var facturaFallida = stripeEvent.Data.Object as Invoice;
                        await ManejarPagoFallido(facturaFallida);
                        break;

                    case "customer.subscription.deleted":
                        var suscripcionCancelada = stripeEvent.Data.Object as Subscription;
                        await ManejarCancelacionSuscripcion(suscripcionCancelada);
                        break;

                    case "checkout.session.completed":
                        var sesionCompletada = stripeEvent.Data.Object as Session;

                        // Verificar si es una sesión de recarga
                        if (sesionCompletada?.Metadata?.TryGetValue("TipoTransaccion", out var tipoTransaccion) == true
                            && tipoTransaccion == "RecargaSaldo")
                        {
                            // Procesar como recarga de saldo
                            await ProcesarRecargaSaldo(sesionCompletada.Id);
                        }
                        else
                        {
                            // Procesar como una compra normal
                            await ManejarSesionCompletada(sesionCompletada);
                        }
                        break;

                    case "payment_intent.succeeded":
                        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                        // Procesar pago exitoso (si es necesario)
                        break;

                    case "payment_intent.payment_failed":
                        var paymentFailed = stripeEvent.Data.Object as PaymentIntent;
                        // Procesar pago fallido (si es necesario)
                        break;
                }
                await MarcarEventoComoCompletado(stripeEvent.Id);
            }
            catch (StripeException ex)
            {
                _logger.LogError($"Error de Stripe al procesar webhook: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error general al procesar webhook: {ex.Message}");
                throw;
            }
        }

        // Método para verificar si un evento ya fue procesado (idempotencia)
        // Mejoras en los métodos de idempotencia en StripeService.cs

        // 1. Mejorar el método para verificar si un evento ya fue procesado
        private async Task<bool> VerificarEventoProcesado(string eventId)
        {
            try
            {
                // Utilizar una consulta optimizada con FirstOrDefaultAsync en lugar de Any
                var evento = await _context.EventosWebhook
                    .FirstOrDefaultAsync(e => e.EventoId == eventId);

                if (evento == null)
                {
                    return false; // No existe, no ha sido procesado
                }

                // Si existe pero no está completado, verificar si ha pasado mucho tiempo
                if (!evento.Completado && evento.FechaRecibido < DateTime.UtcNow.AddHours(-1))
                {
                    _logger.LogWarning($"Evento {eventId} en estado incompleto por más de 1 hora, permitiendo reprocesamiento");
                    return false; // Permitir reprocesar eventos antiguos incompletos
                }

                return evento.Completado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar procesamiento de evento {eventId}");
                return false; // Ante la duda, procesar nuevamente
            }
        }

        // 2. Mejorar el registro de eventos
        private async Task RegistrarProcesamientoEvento(string eventId)
        {
            try
            {
                // Usar transacción para evitar problemas de concurrencia
                using var transaction = await _context.Database.BeginTransactionAsync();

                // Intentar encontrar el evento existente con bloqueo
                var evento = await _context.EventosWebhook
                    .FromSqlRaw("SELECT * FROM EventosWebhook WITH (UPDLOCK) WHERE EventoId = {0}", eventId)
                    .FirstOrDefaultAsync();

                if (evento == null)
                {
                    // Crear nuevo registro si no existe
                    evento = new EventoWebhook
                    {
                        EventoId = eventId,
                        FechaRecibido = DateTime.UtcNow,
                        NumeroIntentos = 1,
                        FechaUltimoIntento = DateTime.UtcNow,
                        Completado = false
                    };
                    _context.EventosWebhook.Add(evento);
                }
                else
                {
                    // Actualizar el registro existente
                    evento.FechaUltimoIntento = DateTime.UtcNow;
                    evento.NumeroIntentos += 1;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"Evento {eventId} registrado para procesamiento (intento #{evento.NumeroIntentos})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al registrar inicio de procesamiento del evento {eventId}");
                // Continuar a pesar del error
            }
        }

        // 3. Mejorar el marcado de eventos como completados
        private async Task MarcarEventoComoCompletado(string eventId)
        {
            try
            {
                // Usar transacción para evitar problemas de concurrencia
                using var transaction = await _context.Database.BeginTransactionAsync();

                // Intentar encontrar el evento existente con bloqueo
                var evento = await _context.EventosWebhook
                    .FromSqlRaw("SELECT * FROM EventosWebhook WITH (UPDLOCK) WHERE EventoId = {0}", eventId)
                    .FirstOrDefaultAsync();

                if (evento != null)
                {
                    evento.Completado = true;
                    evento.FechaCompletado = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation($"Evento {eventId} marcado como completado");
                }
                else
                {
                    _logger.LogWarning($"No se encontró el evento {eventId} para marcarlo como completado");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al marcar evento {eventId} como completado");
                // Continuar a pesar del error
            }
        }

        // 4. Mejorar la verificación de transacciones procesadas
        private async Task<bool> VerificarTransaccionYaProcesada(string sessionId, string? userId = null)
        {
            try
            {
                // Consultar si existe transacción completada con este sessionId
                var transaccionCompletada = await _context.Transacciones
                    .AnyAsync(t => t.StripePaymentId == sessionId && t.Status == "Completado");

                if (transaccionCompletada)
                {
                    _logger.LogInformation($"La transacción {sessionId} ya fue completada anteriormente");
                    return true;
                }

                // Verificar si ya existe un registro para esta sesión en movimientos de saldo
                var recargaProcesada = await _context.MovimientosSaldo
                    .AnyAsync(m => m.ReferenciaExterna == sessionId);

                if (recargaProcesada)
                {
                    _logger.LogInformation($"La recarga {sessionId} ya fue procesada anteriormente");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar si la transacción {sessionId} ya fue procesada");
                return false; // En caso de error, asumimos que no está procesada para evitar perder transacciones
            }
        }

        private async Task ProcesarRecargaSaldo(string sessionId)
        {
            try
            {
                // PRIMERO: Verificar si esta sesión ya fue procesada
                var saldoService = _serviceProvider.GetRequiredService<ISaldoService>();
                bool transaccionExistente = await saldoService.ExisteTransaccion(sessionId);

                if (transaccionExistente)
                {
                    _logger.LogInformation($"Webhook: La sesión {sessionId} ya fue procesada anteriormente");
                    return; // Salir sin procesar nuevamente
                }

                // Obtener detalles de la sesión
                var sessionService = new SessionService();
                var session = await sessionService.GetAsync(sessionId);

                if (session == null || session.PaymentStatus != "paid")
                {
                    _logger.LogWarning($"Sesión {sessionId} no está pagada o no existe");
                    return;
                }

                // Extraer el ID del usuario del cliente de Stripe
                var customerId = session.CustomerId;
                var usuario = await _context.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);

                if (usuario == null)
                {
                    _logger.LogWarning($"No se encontró usuario para el cliente de Stripe {customerId}");
                    return;
                }

                // Calcular monto (Stripe usa centavos)
                decimal monto = (decimal)session.AmountTotal / 100;

                // Registrar la recarga con transacción de BD
                using var dbTransaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Usar un bloqueo optimista en la tabla de saldo para evitar condiciones de carrera
                    var resultado = await saldoService.AgregarSaldo(
                        usuario.Id,
                        monto,
                        "Recarga de saldo (webhook)",
                        sessionId
                    );

                    if (resultado)
                    {
                        await dbTransaction.CommitAsync();
                        _logger.LogInformation($"Recarga de ${monto} procesada correctamente para usuario {usuario.Id} mediante webhook");
                    }
                    else
                    {
                        await dbTransaction.RollbackAsync();
                        _logger.LogError($"Error al procesar recarga mediante webhook para sesión {sessionId}");
                    }
                }
                catch (Exception ex)
                {
                    await dbTransaction.RollbackAsync();
                    _logger.LogError(ex, $"Error en transacción de BD al procesar recarga para sesión {sessionId}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar recarga de saldo para sesión {sessionId}");
            }
        }
        // Mejora del método ManejarSesionCompletada en StripeService.cs
        // En la clase StripeService.cs, actualiza el método ManejarSesionCompletada:

        private async Task ManejarSesionCompletada(Session sesion)
        {
            if (sesion == null)
            {
                _logger.LogWarning("Sesión completada nula en webhook");
                return;
            }

            _logger.LogInformation($"Procesando sesión completada {sesion.Id}");

            try
            {
                // Verificar si la sesión ya fue procesada anteriormente (idempotencia)
                if (await VerificarTransaccionYaProcesada(sesion.Id, null))
                {
                    _logger.LogInformation($"Sesión {sesion.Id} ya fue procesada anteriormente, omitiendo");
                    return;
                }

                // Obtener metadata de la sesión para saber si es una compra de número o una recarga
                if (sesion.Metadata?.TryGetValue("TipoTransaccion", out var tipoTransaccion) == true
                    && tipoTransaccion == "RecargaSaldo")
                {
                    // Procesar como recarga de saldo (ya está implementado)
                    await ProcesarRecargaSaldo(sesion.Id);
                    return;
                }

                // Buscar la transacción asociada a esta sesión
                var transaccion = await _context.Transacciones
                    .Include(t => t.NumeroTelefonico)
                    .FirstOrDefaultAsync(t => t.StripePaymentId == sesion.Id);

                if (transaccion == null)
                {
                    // Si no existe una transacción previa, podría ser una compra iniciada
                    // pero no completada. En este caso, necesitamos datos adicionales.
                    if (sesion.Metadata?.TryGetValue("NumeroTelefono", out var numeroTelefono) == true)
                    {
                        var customerId = sesion.CustomerId;
                        var usuario = await _context.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);

                        if (usuario != null)
                        {
                            // Extraer datos de la sesión para procesar la compra
                            bool incluirSMS = sesion.Metadata.TryGetValue("IncluirSMS", out var incluirSMSValue)
                                && incluirSMSValue == "true";

                            // Obtener servicio de telefonía para procesar la compra
                            var telefoniaService = _serviceProvider.GetRequiredService<ITelefonicaService>();

                            // Procesar la compra directamente desde el webhook
                            await telefoniaService.ProcesarCompraNumero(
                                usuario,
                                numeroTelefono,
                                null, // Número de redirección (el usuario lo configurará después)
                                incluirSMS,
                                sesion.Id,
                                sesion.SubscriptionId
                            );
                        }
                        else
                        {
                            _logger.LogWarning($"No se encontró usuario para el cliente {customerId}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"No se encontró transacción ni metadata para la sesión {sesion.Id}");
                    }
                    return;
                }

                // Actualizar la transacción existente
                transaccion.Status = "Completado";

                // Si tu modelo de Transaccion tiene FechaCompletado, usa esto:
                transaccion.FechaCompletado = DateTime.UtcNow;

                // Si es una suscripción, guardar el ID y activar el número
                if (sesion.SubscriptionId != null && transaccion.NumeroTelefonico != null)
                {
                    transaccion.NumeroTelefonico.StripeSubscriptionId = sesion.SubscriptionId;
                    transaccion.NumeroTelefonico.Activo = true;
                    transaccion.NumeroTelefonico.FechaExpiracion = DateTime.UtcNow.AddMonths(1);

                    // Aquí configuraríamos el número en Twilio si no está configurado

                    if (!string.IsNullOrEmpty(transaccion.NumeroTelefonico.NumeroRedireccion) &&
                        transaccion.NumeroTelefonico.NumeroRedireccion != "pendiente")
                    {
                        var twilioService = _serviceProvider.GetRequiredService<ITwilioService>();
                        await twilioService.ConfigurarRedireccion(
                            transaccion.NumeroTelefonico.PlivoUuid,
                            transaccion.NumeroTelefonico.NumeroRedireccion
                        );

                        if (transaccion.NumeroTelefonico.SMSHabilitado)
                        {
                            await twilioService.ActivarSMS(transaccion.NumeroTelefonico.PlivoUuid);
                        }
                    }

                    _logger.LogInformation($"Número {transaccion.NumeroTelefonico.Numero} activado y configurado por webhook");
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"Transacción {transaccion.Id} actualizada correctamente por webhook");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al manejar sesión completada: {ex.Message}");
                throw;
            }
        }

        private async Task ManejarPagoExitoso(Invoice invoice)
        {
            if (invoice == null)
            {
                _logger.LogWarning("Factura nula en webhook");
                return;
            }

            _logger.LogInformation($"Procesando pago exitoso para factura {invoice.Id}");

            try
            {
                // Obtener la suscripción y actualizar en nuestra base de datos
                var subscriptionId = invoice.SubscriptionId;

                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.StripeSubscriptionId == subscriptionId);

                if (numeroTelefonico != null)
                {
                    // Crear transacción exitosa
                    _context.Transacciones.Add(new Transaccion
                    {
                        UserId = numeroTelefonico.UserId,
                        NumeroTelefonicoId = numeroTelefonico.Id,
                        Fecha = DateTime.UtcNow,
                        Monto = (decimal)invoice.AmountPaid / 100, // Convertir de centavos
                        Concepto = $"Pago mensual - {numeroTelefonico.Numero}",
                        StripePaymentId = invoice.PaymentIntentId,
                        Status = "Completado"
                    });

                    // Actualizar fecha de expiración
                    numeroTelefonico.FechaExpiracion = DateTime.UtcNow.AddMonths(1);

                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Pago exitoso procesado para número {numeroTelefonico.Numero}");
                }
                else
                {
                    _logger.LogWarning($"No se encontró número telefónico para la suscripción {subscriptionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al manejar pago exitoso: {ex.Message}");
                throw;
            }
        }

        private async Task ManejarPagoFallido(Invoice invoice)
        {
            if (invoice == null)
            {
                _logger.LogWarning("Factura fallida nula en webhook");
                return;
            }

            _logger.LogInformation($"Procesando pago fallido para factura {invoice.Id}");

            try
            {
                var subscriptionId = invoice.SubscriptionId;

                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.StripeSubscriptionId == subscriptionId);

                if (numeroTelefonico != null)
                {
                    // Registrar la transacción fallida
                    _context.Transacciones.Add(new Transaccion
                    {
                        UserId = numeroTelefonico.UserId,
                        NumeroTelefonicoId = numeroTelefonico.Id,
                        Fecha = DateTime.UtcNow,
                        Monto = (decimal)invoice.AmountDue / 100, // Convertir de centavos
                        Concepto = $"Intento de pago fallido - {numeroTelefonico.Numero}",
                        StripePaymentId = invoice.PaymentIntentId,
                        Status = "Fallido",
                        DetalleError = "Pago rechazado por el proveedor"
                    });

                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Pago fallido registrado para número {numeroTelefonico.Numero}");
                }
                else
                {
                    _logger.LogWarning($"No se encontró número telefónico para la suscripción {subscriptionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al manejar pago fallido: {ex.Message}");
                throw;
            }
        }

        private async Task ManejarCancelacionSuscripcion(Subscription subscription)
        {
            if (subscription == null)
            {
                _logger.LogWarning("Suscripción cancelada nula en webhook");
                return;
            }

            _logger.LogInformation($"Procesando cancelación de suscripción {subscription.Id}");

            try
            {
                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.StripeSubscriptionId == subscription.Id);

                if (numeroTelefonico != null)
                {
                    // Desactivar el número
                    numeroTelefonico.Activo = false;

                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Número {numeroTelefonico.Numero} desactivado por cancelación de suscripción");
                }
                else
                {
                    _logger.LogWarning($"No se encontró número telefónico para la suscripción {subscription.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al manejar cancelación de suscripción: {ex.Message}");
                throw;
            }
        }
        public async Task<StripeCheckoutSession> CrearSesionRecarga(string customerId, decimal monto)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Creando sesión de recarga para cliente {customerId}, monto: {monto}");

                    var lineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(monto * 100), // Convertir a centavos
                        Currency = "mxn",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Recarga de saldo: ${monto} MXN",
                            Description = "Recarga de saldo para servicios de telefonía"
                        }
                    },
                    Quantity = 1
                }
            };

                    var appUrl = _configuration["AppUrl"] ?? "https://localhost:7019";

                    var options = new SessionCreateOptions
                    {
                        Customer = customerId,
                        PaymentMethodTypes = new List<string> { "card" },
                        LineItems = lineItems,
                        Mode = "payment",
                        SuccessUrl = $"{appUrl}/saldo/recarga/exito?session_id={{CHECKOUT_SESSION_ID}}",
                        CancelUrl = $"{appUrl}/saldo/recarga/cancelada",
                        Metadata = new Dictionary<string, string>
                {
                    { "TipoTransaccion", "RecargaSaldo" },
                    { "Monto", monto.ToString() }
                }
                    };

                    var service = new SessionService();
                    var session = await service.CreateAsync(options);

                    _logger.LogInformation($"Sesión de recarga creada. ID: {session.Id}");

                    return new StripeCheckoutSession
                    {
                        SessionId = session.Id,
                        Url = session.Url
                    };
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al crear sesión de recarga: {ex.Message}, Tipo: {ex.StripeError?.Type}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al crear sesión de recarga: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<Stripe.Checkout.Session> ObtenerDetallesSesion(string sessionId)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogInformation($"Obteniendo detalles de sesión: {sessionId}");

                    var sessionService = new SessionService();
                    var session = await sessionService.GetAsync(sessionId);

                    return session;
                }
                catch (StripeException ex)
                {
                    _logger.LogError($"Error de Stripe al obtener detalles de sesión: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al obtener detalles de sesión: {ex.Message}");
                    throw;
                }
            });
        }
    }

    public class StripeCheckoutSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}