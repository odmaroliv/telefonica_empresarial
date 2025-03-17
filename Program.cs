using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quartz;
using System.Threading.RateLimiting;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Areas.Identity;
using TelefonicaEmpresarial.Middleware;
using TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Services.BackgroundJobs;

var builder = WebApplication.CreateBuilder(args);

// Configuraci�n de la conexi�n a la base de datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se encontr� la cadena de conexi�n 'DefaultConnection'.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configuraci�n de Identity (autenticaci�n y usuarios)
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

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

// Configuraci�n de p�ginas Razor y Blazor Server
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<ApplicationUser>>();

builder.Services.AddScoped<ITwilioService, TwilioService>();
builder.Services.AddScoped<ITelefonicaService, TelefonicaService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<ISaldoService, SaldoService>();
builder.Services.AddScoped<IRequisitosRegulatoriosService, RequisitosRegulatoriosService>();
builder.Services.AddScoped<IValidationService, ValidationService>();

// Configuraci�n de CORS si es necesario
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebhooks", policy =>
    {
        policy.WithOrigins(
            "https://api.stripe.com",
            "https://api.twilio.com")
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

builder.Services.AddQuartz(q =>
{
    // Configuración base
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Configurar LimpiezaDatosJob
    var limpiezaJobKey = new JobKey("LimpiezaDatos");
    q.AddJob<LimpiezaDatosJob>(opts => opts.WithIdentity(limpiezaJobKey));
    q.AddTrigger(opts => opts
        .ForJob(limpiezaJobKey)
        .WithIdentity("LimpiezaDatos-Trigger")
        .WithCronSchedule("0 0 3 * * ?")); // Ejecutar a las 3 AM todos los días

    // Configurar RenovacionNumerosJob
    var renovacionJobKey = new JobKey("RenovacionNumeros");
    q.AddJob<RenovacionNumerosJob>(opts => opts.WithIdentity(renovacionJobKey));
    q.AddTrigger(opts => opts
        .ForJob(renovacionJobKey)
        .WithIdentity("RenovacionNumeros-Trigger")
        .WithCronSchedule("0 0 2 * * ?")); // Ejecutar a las 2 AM todos los días
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
// Protecci�n antiforgery y otras medidas de seguridad
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "CSRF-TOKEN";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

var app = builder.Build();

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

app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseRouting();

// Uso de CORS antes de la autenticaci�n
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
        logger.LogError(ex, "Error al realizar la migraci�n o seed de la base de datos.");
    }
}

app.Run();