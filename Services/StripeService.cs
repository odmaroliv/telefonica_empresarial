namespace TelefonicaEmpresaria.Services
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Stripe;
    using Stripe.Checkout;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
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
        }

        public class StripeService : IStripeService
        {
            private readonly IConfiguration _configuration;
            private readonly ApplicationDbContext _context;
            private readonly string _apiKey;
            private readonly string _webhookSecret;

            public StripeService(IConfiguration configuration, ApplicationDbContext context)
            {
                _configuration = configuration;
                _context = context;
                _apiKey = _configuration["Stripe:SecretKey"] ?? throw new ArgumentNullException("Stripe:SecretKey");
                _webhookSecret = _configuration["Stripe:WebhookSecret"] ?? throw new ArgumentNullException("Stripe:WebhookSecret");

                StripeConfiguration.ApiKey = _apiKey;
            }

            public async Task<string> CrearClienteStripe(ApplicationUser usuario)
            {
                try
                {
                    var options = new CustomerCreateOptions
                    {
                        Email = usuario.Email,
                        Name = $"{usuario.Nombre} {usuario.Apellidos}",
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

                    return customer.Id;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear cliente en Stripe: {ex.Message}");
                    throw;
                }
            }

            public async Task<StripeCheckoutSession> CrearSesionCompra(string customerId, string numeroTelefono, decimal costoMensual, decimal? costoSMS = null)
            {
                try
                {
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

                    var options = new SessionCreateOptions
                    {
                        Customer = customerId,
                        PaymentMethodTypes = new List<string> { "card" },
                        LineItems = lineItems,
                        Mode = "subscription",
                        SuccessUrl = $"{_configuration["AppUrl"]}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
                        CancelUrl = $"{_configuration["AppUrl"]}/checkout/cancel",
                        Metadata = new Dictionary<string, string>
                    {
                        { "NumeroTelefono", numeroTelefono },
                        { "IncluirSMS", costoSMS.HasValue ? "true" : "false" }
                    }
                    };

                    var service = new SessionService();
                    var session = await service.CreateAsync(options);

                    return new StripeCheckoutSession
                    {
                        SessionId = session.Id,
                        Url = session.Url
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear sesión de compra: {ex.Message}");
                    throw;
                }
            }

            public async Task<bool> VerificarPagoCompletado(string sessionId)
            {
                try
                {
                    var service = new SessionService();
                    var session = await service.GetAsync(sessionId);

                    return session.PaymentStatus == "paid";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al verificar pago: {ex.Message}");
                    return false;
                }
            }

            public async Task<string> CrearSuscripcion(string customerId, string nombrePlan, decimal montoPlan, string descripcion)
            {
                try
                {
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

                    return suscripcion.Id;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear suscripción: {ex.Message}");
                    throw;
                }
            }

            public async Task<bool> CancelarSuscripcion(string subscriptionId)
            {
                try
                {
                    var service = new SubscriptionService();
                    await service.CancelAsync(subscriptionId, new SubscriptionCancelOptions
                    {
                        InvoiceNow = true,
                        Prorate = true
                    });

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al cancelar suscripción: {ex.Message}");
                    return false;
                }
            }

            public async Task<bool> ActualizarSuscripcion(string subscriptionId, decimal nuevoCosto)
            {
                try
                {
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

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al actualizar suscripción: {ex.Message}");
                    return false;
                }
            }

            public async Task<bool> AgregarSMSASuscripcion(string subscriptionId, decimal costoSMS)
            {
                try
                {
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

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al agregar SMS a suscripción: {ex.Message}");
                    return false;
                }
            }

            public async Task<bool> QuitarSMSDeSuscripcion(string subscriptionId)
            {
                try
                {
                    // Obtener la suscripción
                    var subscriptionService = new SubscriptionService();
                    var subscription = await subscriptionService.GetAsync(subscriptionId);

                    // Buscar el item de SMS (asumimos que es el segundo item, el primero es el número)
                    if (subscription.Items.Data.Count > 1)
                    {
                        var itemId = subscription.Items.Data[1].Id;
                        var itemService = new SubscriptionItemService();
                        await itemService.DeleteAsync(itemId);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al quitar SMS de suscripción: {ex.Message}");
                    return false;
                }
            }

            public async Task ProcesarEventoWebhook(string json, string signatureHeader)
            {
                try
                {
                    var stripeEvent = EventUtility.ConstructEvent(
                        json,
                    signatureHeader,
                        _webhookSecret
                    );

                    // Manejar eventos relevantes
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
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al procesar webhook: {ex.Message}");
                    throw;
                }
            }

            private async Task ManejarPagoExitoso(Invoice invoice)
            {
                if (invoice == null) return;

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
                }
            }

            private async Task ManejarPagoFallido(Invoice invoice)
            {
                if (invoice == null) return;

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
                }
            }

            private async Task ManejarCancelacionSuscripcion(Subscription subscription)
            {
                if (subscription == null) return;

                var numeroTelefonico = await _context.NumerosTelefonicos
                    .FirstOrDefaultAsync(n => n.StripeSubscriptionId == subscription.Id);

                if (numeroTelefonico != null)
                {
                    // Desactivar el número
                    numeroTelefonico.Activo = false;

                    await _context.SaveChangesAsync();
                }
            }
        }

        public class StripeCheckoutSession
        {
            public string SessionId { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }
    }
}
