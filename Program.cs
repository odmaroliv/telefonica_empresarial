using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Areas.Identity;
using TelefonicaEmpresarial.Services;

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
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

// Configuración de páginas Razor y Blazor Server
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<ApplicationUser>>();

builder.Services.AddScoped<ITwilioService, TwilioService>();
builder.Services.AddScoped<ITelefonicaService, TelefonicaService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<ISaldoService, SaldoService>();

// Configuración de CORS si es necesario
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

// Protección antiforgery y otras medidas de seguridad
builder.Services.AddAntiforgery();

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

app.UseHttpsRedirection();

app.UseStaticFiles();

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

app.Run();