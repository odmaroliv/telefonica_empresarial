using TelefonicaEmpresaria.Models;

public class SuscripcionRecargaAutomatica
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public string StripeSubscriptionId { get; set; }
    public decimal MontoMensual { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime ProximaRecarga { get; set; }
    public bool Activa { get; set; }

    // Relación con el usuario
    public ApplicationUser Usuario { get; set; }
}