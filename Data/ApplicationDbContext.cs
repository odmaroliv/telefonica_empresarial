namespace TelefonicaEmpresaria.Data
{
    using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore;
    using TelefonicaEmpresaria.Models;

    namespace TelefonicaEmpresarial.Data
    {
        public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
        {
            public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
                : base(options)
            {
            }

            public DbSet<NumeroTelefonico> NumerosTelefonicos { get; set; }
            public DbSet<Transaccion> Transacciones { get; set; }
            public DbSet<LogLlamada> LogsLlamadas { get; set; }
            public DbSet<LogSMS> LogsSMS { get; set; }
            public DbSet<ConfiguracionSistema> ConfiguracionesSistema { get; set; }

            protected override void OnModelCreating(ModelBuilder builder)
            {
                base.OnModelCreating(builder);

                // Configuraciones adicionales
                builder.Entity<NumeroTelefonico>()
                    .HasOne(n => n.Usuario)
                    .WithMany(u => u.NumerosTelefonicos)
                    .HasForeignKey(n => n.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.Entity<Transaccion>()
                    .HasOne(t => t.Usuario)
                    .WithMany(u => u.Transacciones)
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.Entity<LogLlamada>()
                    .HasOne(l => l.NumeroTelefonico)
                    .WithMany(n => n.LogsLlamadas)
                    .HasForeignKey(l => l.NumeroTelefonicoId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.Entity<LogSMS>()
                    .HasOne(s => s.NumeroTelefonico)
                    .WithMany(n => n.LogsSMS)
                    .HasForeignKey(s => s.NumeroTelefonicoId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Seed inicial para configuraciones del sistema
                builder.Entity<ConfiguracionSistema>().HasData(
                    new ConfiguracionSistema
                    {
                        Id = 1,
                        Clave = "MargenGanancia",
                        Valor = "0.8",
                        Descripcion = "Margen de ganancia aplicado sobre el costo del proveedor (0.8 = 80%)"
                    },
                    new ConfiguracionSistema
                    {
                        Id = 2,
                        Clave = "MargenGananciaSMS",
                        Valor = "0.85",
                        Descripcion = "Margen de ganancia para el servicio de SMS (0.85 = 85%)"
                    },
                    new ConfiguracionSistema
                    {
                        Id = 3,
                        Clave = "IVA",
                        Valor = "0.16",
                        Descripcion = "Impuesto al Valor Agregado"
                    }
                );
            }
        }
    }
}
