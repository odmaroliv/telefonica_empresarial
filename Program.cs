using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using System.Text.Json;
using System.Threading.RateLimiting;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services;
using TelefonicaEmpresaria.Services.BackgroundJobs;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Areas.Identity;
using TelefonicaEmpresarial.Middleware;
using TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Services.BackgroundJobs;

var builder = WebApplication.CreateBuilder(args);

// Configuración de la conexión a la base de datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configuración de Identity (autenticación y usuarios)
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    //Cambiar a true para requerir confirmación de correo cuando le metamos sendgrid
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Configuración para mejorar seguridad
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Tiempo de expiración
    options.ExpireTimeSpan = TimeSpan.FromDays(1);
    options.SlidingExpiration = true;

    // Rutas personalizadas
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// Configuración de HttpClient
builder.Services.AddHttpClient("API", client =>
{
    // Obtener la URL desde la configuración
    var appUrl = builder.Configuration["AppUrl"] ??
                 throw new InvalidOperationException("La URL de la aplicación no está configurada.");

    client.BaseAddress = new Uri(appUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});


//Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database")
    .AddCheck("self", () => HealthCheckResult.Healthy())
    // Agrega otros servicios críticos
    .AddUrlGroup(new Uri("https://api.twilio.com/"), "twilio-api")
    .AddUrlGroup(new Uri("https://status.stripe.com/"), "stripe-status")
    .AddCheck<QuartzJobsHealthCheck>("quartz-jobs")
    .AddCheck<TransaccionesMonitorHealthCheck>("transacciones-monitor-job");


// Configuración de Health Checks UI
builder.Services.AddHealthChecksUI(options =>
{
    options.SetEvaluationTimeInSeconds(60);
    options.MaximumHistoryEntriesPerEndpoint(10);
    options.AddHealthCheckEndpoint("API", "http://localhost:8080/healthz"); // Usamos un endpoint especial para el monitoreo

})
.AddInMemoryStorage();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // Diferencia entre webhooks y peticiones normales
        if (httpContext.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            // Webhooks necesitan más permisividad
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: "webhooks",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 100,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                });
        }

        // Limitar por IP para peticiones normales
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 20,
                QueueLimit = 2,
                Window = TimeSpan.FromSeconds(10)
            });
    });

    // Personalizar respuesta de límite excedido
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429; // Too Many Requests
        context.HttpContext.Response.ContentType = "application/json";

        // Obtener tiempo de espera recomendado (si está disponible)
        TimeSpan? retryAfter = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterMetadata))
        {
            retryAfter = retryAfterMetadata;
        }

        var response = new
        {
            title = "Too many requests",
            status = 429,
            detail = "Request limit exceeded. Please try again later.",
            retryAfter = retryAfter?.TotalSeconds ?? 5 // Default 5 segundos si no hay metadata
        };

        await context.HttpContext.Response.WriteAsJsonAsync(response, token);
    };
});



// Configuración de páginas Razor y Blazor Server
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<ApplicationUser>>();

builder.Services.AddScoped<ITwilioService, TwilioService>();
builder.Services.AddScoped<ITelefonicaService, TelefonicaService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<ISaldoService, SaldoService>();
builder.Services.AddScoped<IRequisitosRegulatoriosService, RequisitosRegulatoriosService>();
builder.Services.AddScoped<IValidationService, ValidationService>();
builder.Services.AddScoped<ILlamadasService, LlamadasService>();
builder.Services.AddScoped<NavMenuService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAdminLogService, AdminLogService>();
builder.Services.AddScoped<ITransaccionMonitorService, TransaccionMonitorService>();



// Configuración de CORS si es necesario
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebhooks", policy =>
    {
        //policy.WithOrigins(
        //    "https://api.stripe.com",
        //    "https://api.twilio.com")
        policy.AllowAnyOrigin()
       .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

builder.Services.AddQuartz(q =>
{
    // Mantén la configuración existente
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Aquí están tus jobs existentes (no modificar)
    var limpiezaJobKey = new JobKey("LimpiezaDatos");
    q.AddJob<LimpiezaDatosJob>(opts => opts.WithIdentity(limpiezaJobKey));
    q.AddTrigger(opts => opts
        .ForJob(limpiezaJobKey)
        .WithIdentity("LimpiezaDatos-Trigger")
        .WithCronSchedule("0 0 3 * * ?")); // Ejecutar a las 3 AM todos los días

    var renovacionJobKey = new JobKey("RenovacionNumeros");
    q.AddJob<RenovacionNumerosJob>(opts => opts.WithIdentity(renovacionJobKey));
    q.AddTrigger(opts => opts
        .ForJob(renovacionJobKey)
        .WithIdentity("RenovacionNumeros-Trigger")
        .WithCronSchedule("0 0 2 * * ?")); // Ejecutar a las 2 AM todos los días

    var llamadasMonitorJobKey = new JobKey("LlamadasMonitorJob");
    q.AddJob<LlamadasMonitorJob>(opts => opts.WithIdentity(llamadasMonitorJobKey));
    q.AddTrigger(opts => opts
        .ForJob(llamadasMonitorJobKey)
        .WithIdentity("LlamadasMonitorJob-Trigger")
        .WithSimpleSchedule(x => x
            .WithIntervalInSeconds(60)
            .RepeatForever()));
    q.AddTrigger(opts => opts
        .ForJob(llamadasMonitorJobKey)
        .WithIdentity("VerificarSaldoLlamadasActivas-Trigger")
        .WithSimpleSchedule(x => x
            .WithIntervalInSeconds(30)
            .RepeatForever()));

    // AÑADIR AQUÍ: Configuración para el nuevo job de monitoreo de transacciones
    var transaccionesMonitorJobKey = new JobKey("TransaccionesMonitorJob");
    q.AddJob<TransaccionesMonitorJob>(opts => opts.WithIdentity(transaccionesMonitorJobKey));
    q.AddTrigger(opts => opts
        .ForJob(transaccionesMonitorJobKey)
        .WithIdentity("TransaccionesMonitor-Trigger")
        .WithCronSchedule("0 0/30 * * * ?")); // Ejecutar cada 30 minutos
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
// Protección antiforgery y otras medidas de seguridad
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "CSRF-TOKEN";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.WebHost.UseUrls("http://*:8080");

// Crear una política de autorización para health checks
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

var app = builder.Build();
// Cerca del inicio de Configure, justo después de app.Build()
app.Use(async (context, next) =>
{
    app.Logger.LogInformation($"Solicitud recibida: {context.Request.Method} {context.Request.Path} - User Agent: {context.Request.Headers["User-Agent"]}");
    await next();
    app.Logger.LogInformation($"Respuesta enviada: {context.Response.StatusCode}");
});

// Configure el pipeline de solicitud HTTP.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseAntiforgery();
app.UseGlobalExceptionHandler();

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseRouting();

// Uso de CORS antes de la autenticación
app.UseCors("AllowWebhooks");

app.UseAuthentication();
app.UseAuthorization();

// Mapeo de controladores para webhooks y APIs
app.MapControllers();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// Seed inicial de datos (si es necesario)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        // Crear rol de administrador si no existe
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error al realizar la migración o seed de la base de datos.");
    }
}

// 1. Endpoint público básico - sin autenticación, para verificación básica (load balancers, etc.)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // No ejecutar checks, solo verificar que la app responde
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = "alive" }));
    }
}).AllowAnonymous();

// 2. Endpoint para la UI de Health Checks (autenticado con JSON específico)
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    AllowCachingResponses = false
}).AllowAnonymous(); // La UI accede a este sin autenticación

// 3. Endpoint detallado para administradores
app.MapHealthChecks("/health/details", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    AllowCachingResponses = false
}).RequireAuthorization("AdminOnly");


// 4. Health Checks UI - protegido para administradores
app.MapHealthChecksUI(options =>
{
    options.UIPath = "/health-ui";
    options.ApiPath = "/health-api"; // Esta es la forma correcta de configurarlo
}).RequireAuthorization("AdminOnly");


app.Run();