using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.Checkout;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;

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
        Task ProcesarEventoWebhook(string json, string signatureHeader);
        Task<string?> ObtenerURLPago(string sessionId);
    }

    public class StripeService : IStripeService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StripeService> _logger;
        private readonly string _apiKey;
        private readonly string _webhookSecret;
        private readonly AsyncRetryPolicy _retryPolicy;

        public StripeService(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<StripeService> logger)
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

        public async Task ProcesarEventoWebhook(string json, string signatureHeader)
        {
            try
            {
                _logger.LogInformation("Procesando webhook de Stripe");

                // Verificar que el webhook es auténtico
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    signatureHeader,
                    _webhookSecret
                );

                _logger.LogInformation($"Evento Stripe recibido de tipo: {stripeEvent.Type}");

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
                        await ManejarSesionCompletada(sesionCompletada);
                        break;
                }
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
                // Buscar la transacción asociada a esta sesión
                var transaccion = await _context.Transacciones
                    .Include(t => t.NumeroTelefonico)
                    .FirstOrDefaultAsync(t => t.StripePaymentId == sesion.Id);

                if (transaccion == null)
                {
                    _logger.LogWarning($"No se encontró transacción para la sesión {sesion.Id}");
                    return;
                }

                // Actualizar la transacción
                transaccion.Status = "Completado";

                // Si es una suscripción, guardar el ID
                if (sesion.SubscriptionId != null && transaccion.NumeroTelefonico != null)
                {
                    transaccion.NumeroTelefonico.StripeSubscriptionId = sesion.SubscriptionId;
                    transaccion.NumeroTelefonico.Activo = true;

                    _logger.LogInformation($"Número activado y asociado a suscripción {sesion.SubscriptionId}");
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Transacción {transaccion.Id} actualizada correctamente");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al manejar sesión completada: {ex.Message}");
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
    }

    public class StripeCheckoutSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}