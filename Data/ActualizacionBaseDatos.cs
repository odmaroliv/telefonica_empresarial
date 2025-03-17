using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Models;

namespace TelefonicaEmpresarial.Services
{
    /// <summary>
    /// Extensiones para actualizar la base de datos para soportar llamadas salientes
    /// </summary>
    public static class ConfiguracionBaseDatosTelefonia
    {
        /// <summary>
        /// Agrega la tabla de llamadas salientes a la base de datos
        /// </summary>
        public static void ConfigurarModeloLlamadasSalientes(this ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LlamadaSaliente>(entity =>
            {
                entity.ToTable("LlamadasSalientes");

                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Usuario)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.NumeroTelefonico)
                      .WithMany()
                      .HasForeignKey(e => e.NumeroTelefonicoId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.NumeroDestino)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(e => e.Estado)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(e => e.TwilioCallSid)
                      .HasMaxLength(100);

                entity.Property(e => e.Costo)
                      .HasColumnType("decimal(10, 2)");

                entity.Property(e => e.Detalles)
                      .HasMaxLength(1000);
            });
        }
    }
}

