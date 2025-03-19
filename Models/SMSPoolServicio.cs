// Modelo para representar servicios disponibles en SMSPool
using System.ComponentModel.DataAnnotations;

public class SMSPoolServicio
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string ServiceId { get; set; } // ID del servicio en SMSPool

    [Required]
    public string Nombre { get; set; } // Nombre amigable del servicio (WhatsApp, Facebook, etc.)

    public string Descripcion { get; set; } // Descripción del servicio

    public string IconoUrl { get; set; } // URL del icono del servicio

    [Required]
    public bool Activo { get; set; } = true; // Si el servicio está activo

    [Required]
    public decimal CostoBase { get; set; } // Costo base en USD

    [Required]
    public decimal PrecioVenta { get; set; } // Precio de venta en MXN (con margen)
    public decimal PrecioAlto { get; set; }

    public int TiempoEstimadoMinutos { get; set; } = 20; // Tiempo estimado para recibir SMS

    public string PaisesDisponibles { get; set; } // Lista de países disponibles (JSON)

    public decimal TasaExito { get; set; } // Porcentaje de éxito de verificación (0-100)

    public DateTime UltimaActualizacion { get; set; } = DateTime.UtcNow;

    // Relaciones
    public virtual ICollection<SMSPoolNumero> Numeros { get; set; }
}