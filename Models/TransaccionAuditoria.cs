public class TransaccionAuditoria
{
    public int Id { get; set; }
    public string TipoOperacion { get; set; } // "RecargaSaldo", "CompraNumero", etc.
    public string ReferenciaExterna { get; set; } // sessionId de Stripe
    public string UserId { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } // "Iniciada", "ProcesadaPorWebhook", "ProcesadaPorUI", "Completada", "Fallida"
    public string DetalleError { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
    public string DatosRequest { get; set; } // Almacenar datos JSON del request para debugging
}