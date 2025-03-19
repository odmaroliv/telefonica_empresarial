using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelefonicaEmpresaria.Models;

public class SMSPoolNumero
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; }

    [ForeignKey("UserId")]
    public virtual ApplicationUser Usuario { get; set; }

    [Required]
    public int ServicioId { get; set; }

    [ForeignKey("ServicioId")]
    public virtual SMSPoolServicio Servicio { get; set; }

    [Required]
    public string Numero { get; set; } // Número telefónico

    [Required]
    public string OrderId { get; set; } // ID de orden en SMSPool

    [Required]
    public string Pais { get; set; } // Código de país (MX, US, etc.)

    [Required]
    public DateTime FechaCompra { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime FechaExpiracion { get; set; }

    [Required]
    public string Estado { get; set; } = "Activo"; // Activo, Expirado, Cancelado

    [Required]
    public decimal CostoPagado { get; set; } // Costo pagado en MXN

    public bool SMSRecibido { get; set; } = false; // Si se ha recibido un SMS

    public DateTime? FechaUltimaComprobacion { get; set; } // Última vez que se verificó el estado

    public string CodigoRecibido { get; set; } // Código de verificación recibido

    public bool VerificacionExitosa { get; set; } = false; // Si la verificación fue exitosa

    // Relaciones
    public virtual ICollection<SMSPoolVerificacion> Verificaciones { get; set; }
}