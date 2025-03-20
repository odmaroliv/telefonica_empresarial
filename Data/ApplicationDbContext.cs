namespace TelefonicaEmpresaria.Data
{
    using global::TelefonicaEmpresarial.Services;
    using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore;
    using TelefonicaEmpresaria.Models;
    using TelefonicaEmpresaria.Utils;

    namespace TelefonicaEmpresarial.Data
    {
        public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
        {
            private readonly ICurrentUserService _currentUserService;
            public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserService currentUserService)
                : base(options)
            {
                _currentUserService = currentUserService;
            }


            public DbSet<NumeroTelefonico> NumerosTelefonicos { get; set; }
            public DbSet<Transaccion> Transacciones { get; set; }
            public DbSet<LogLlamada> LogsLlamadas { get; set; }
            public DbSet<LogSMS> LogsSMS { get; set; }
            public DbSet<ConfiguracionSistema> ConfiguracionesSistema { get; set; }
            public DbSet<SaldoCuenta> SaldosCuenta { get; set; }
            public DbSet<MovimientoSaldo> MovimientosSaldo { get; set; }
            public DbSet<EventoWebhook> EventosWebhook { get; set; }
            public DbSet<RequisitosRegulatorios> RequisitosRegulatorios { get; set; }
            public DbSet<DocumentacionUsuario> DocumentacionUsuarios { get; set; }
            public DbSet<LlamadaSaliente> LlamadasSalientes { get; set; }
            public DbSet<AdminLog> AdminLogs { get; set; }
            public DbSet<TransaccionAuditoria> TransaccionesAuditoria { get; set; }
            public DbSet<SMSPoolServicio> SMSPoolServicios { get; set; }
            public DbSet<SMSPoolNumero> SMSPoolNumeros { get; set; }
            public DbSet<SMSPoolVerificacion> SMSPoolVerificaciones { get; set; }
            public DbSet<SMSPoolConfiguracion> SMSPoolConfiguraciones { get; set; }
            public DbSet<SuscripcionRecargaAutomatica> SuscripcionesRecarga { get; set; }


            protected override void OnModelCreating(ModelBuilder builder)
            {
                base.OnModelCreating(builder);

                // ==============================
                // Filtros globales para entidades propias del usuario
                // ==============================
                builder.Entity<NumeroTelefonico>()
                    .HasQueryFilter(n => _currentUserService.IsAdmin || n.UserId == _currentUserService.UserId);

                builder.Entity<Transaccion>()
                    .HasQueryFilter(t => _currentUserService.IsAdmin || t.UserId == _currentUserService.UserId);

                builder.Entity<MovimientoSaldo>()
                    .HasQueryFilter(m => _currentUserService.IsAdmin || m.UserId == _currentUserService.UserId);

                builder.Entity<DocumentacionUsuario>()
                    .HasQueryFilter(d => _currentUserService.IsAdmin || d.UserId == _currentUserService.UserId);

                builder.Entity<SaldoCuenta>()
                    .HasQueryFilter(s => _currentUserService.IsAdmin || s.UserId == _currentUserService.UserId);

                builder.Entity<LlamadaSaliente>()
                    .HasQueryFilter(l => _currentUserService.IsAdmin || l.UserId == _currentUserService.UserId);

                // ==============================
                // Filtros para logs que se relacionan a registros de usuario
                // (se accede vía la relación con NumeroTelefonico)
                // ==============================
                builder.Entity<LogLlamada>()
                    .HasQueryFilter(l => _currentUserService.IsAdmin
                                        || (l.NumeroTelefonico != null && l.NumeroTelefonico.UserId == _currentUserService.UserId));

                builder.Entity<LogSMS>()
                    .HasQueryFilter(s => _currentUserService.IsAdmin
                                        || (s.NumeroTelefonico != null && s.NumeroTelefonico.UserId == _currentUserService.UserId));

                // ==============================
                // Filtros para entidades de configuración y sistema:
                // Solo los administradores
                // ==============================
                //builder.Entity<ConfiguracionSistema>()
                //    .HasQueryFilter(c => _currentUserService.IsAdmin);

                //builder.Entity<RequisitosRegulatorios>()
                //    .HasQueryFilter(r => _currentUserService.IsAdmin);

                //builder.Entity<AdminLog>()
                //    .HasQueryFilter(a => _currentUserService.IsAdmin);

                //builder.Entity<TransaccionAuditoria>()
                //    .HasQueryFilter(t => _currentUserService.IsAdmin);

                //builder.Entity<EventoWebhook>()
                //    .HasQueryFilter(e => _currentUserService.IsAdmin);

                // ==============================
                // Relaciones y configuraciones adicionales
                // ==============================
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

                builder.Entity<SaldoCuenta>()
                    .HasOne(s => s.Usuario)
                    .WithMany()
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.Entity<MovimientoSaldo>()
                    .HasOne(m => m.Usuario)
                    .WithMany()
                    .HasForeignKey(m => m.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.Entity<DocumentacionUsuario>()
               .HasIndex(d => new { d.UserId, d.CodigoPais })
               .IsUnique();

                builder.Entity<RequisitosRegulatorios>()
                    .HasIndex(r => r.CodigoPais)
                    .IsUnique();
                // Configuración para SMSPoolServicio
                builder.Entity<SMSPoolServicio>()
                    .HasIndex(s => s.ServiceId)
                    .IsUnique();

                // Configuración para SMSPoolNumero
                builder.Entity<SMSPoolNumero>()
                    .HasIndex(n => n.OrderId)
                    .IsUnique();

                // Relación entre SMSPoolNumero y SMSPoolVerificacion
                builder.Entity<SMSPoolVerificacion>()
                    .HasOne(v => v.Numero)
                    .WithMany(n => n.Verificaciones)
                    .HasForeignKey(v => v.NumeroId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Relación entre SMSPoolServicio y SMSPoolNumero
                builder.Entity<SMSPoolNumero>()
                    .HasOne(n => n.Servicio)
                    .WithMany(s => s.Numeros)
                    .HasForeignKey(n => n.ServicioId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Configuración para SMSPoolConfiguracion
                builder.Entity<SMSPoolConfiguracion>()
                    .HasIndex(c => c.Clave)
                    .IsUnique();

                builder.Entity<ConfiguracionSistema>().HasData(
      new ConfiguracionSistema
      {
          Id = 1,
          Clave = "MargenGanancia",
          Valor = "3.0", // Aumentado a 300% 
          Descripcion = "Margen de ganancia aplicado sobre el costo del proveedor (3.0 = 300%)"
      },
      new ConfiguracionSistema
      {
          Id = 2,
          Clave = "MargenGananciaSMS",
          Valor = "3.5", // Aumentado a 350%
          Descripcion = "Margen de ganancia para el servicio de SMS (3.5 = 350%)"
      },
      new ConfiguracionSistema
      {
          Id = 3,
          Clave = "IVA",
          Valor = "0.16",
          Descripcion = "Impuesto al Valor Agregado"
      },
      new ConfiguracionSistema
      {
          Id = 4,
          Clave = "MargenGananciaLlamadas",
          Valor = "4.0", // Aumentado a 400%
          Descripcion = "Margen de ganancia por minuto de llamadas (4.0 = 400%)"
      },
      new ConfiguracionSistema
      {
          Id = 5,
          Clave = "CostoMinimoNumero",
          Valor = "100.0", // Precio mínimo garantizado para números
          Descripcion = "Costo mínimo mensual para números telefónicos (MXN)"
      },
      new ConfiguracionSistema
      {
          Id = 6,
          Clave = "CostoMinimoSMS",
          Valor = "25.0", // Precio mínimo garantizado para servicio SMS
          Descripcion = "Costo mínimo mensual para servicio SMS (MXN)"
      }
  );
                builder.ConfigurarModeloLlamadasSalientes();

            }
        }
    }
}
