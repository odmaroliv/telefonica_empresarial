using Microsoft.EntityFrameworkCore;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;

namespace TelefonicaEmpresarial.Services
{
    public interface IRequisitosRegulatoriosService
    {
        Task<RequisitosRegulatorios?> ObtenerRequisitosPorPais(string codigoPais);
        Task<List<RequisitosRegulatorios>> ObtenerTodosRequisitos();
        Task<bool> VerificarSiCumpleRequisitos(string userId, string codigoPais);
        Task<DocumentacionUsuario?> ObtenerDocumentacionUsuario(string userId, string codigoPais);
        Task<bool> GuardarDocumentacion(DocumentacionUsuario documentacion);
        Task<bool> VerificarDocumentacion(int documentacionId, bool aprobada, string? motivoRechazo = null);
    }

    public class RequisitosRegulatoriosService : IRequisitosRegulatoriosService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RequisitosRegulatoriosService> _logger;

        public RequisitosRegulatoriosService(
            ApplicationDbContext context,
            ILogger<RequisitosRegulatoriosService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<RequisitosRegulatorios?> ObtenerRequisitosPorPais(string codigoPais)
        {
            try
            {
                // Corrección: usar la entidad tipada directamente
                return await _context.RequisitosRegulatorios
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.CodigoPais == codigoPais && r.Activo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener requisitos para país {codigoPais}");
                return null;
            }
        }

        public async Task<List<RequisitosRegulatorios>> ObtenerTodosRequisitos()
        {
            try
            {
                // Corrección: usar la entidad tipada directamente
                return await _context.RequisitosRegulatorios
                    .AsNoTracking()
                    .Where(r => r.Activo)
                    .OrderBy(r => r.Nombre)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener todos los requisitos regulatorios");
                return new List<RequisitosRegulatorios>();
            }
        }

        public async Task<bool> VerificarSiCumpleRequisitos(string userId, string codigoPais)
        {
            try
            {
                // Obtener requisitos para el país
                var requisitos = await ObtenerRequisitosPorPais(codigoPais);

                // Si no hay requisitos específicos, se asume que cumple
                if (requisitos == null ||
                    (!requisitos.RequiereIdentificacion &&
                     !requisitos.RequiereComprobanteDomicilio &&
                     !requisitos.RequiereDocumentoFiscal &&
                     !requisitos.RequiereFormularioRegulatorio))
                {
                    return true;
                }

                // Verificar si el usuario ya tiene documentación aprobada
                // Corrección: usar la entidad tipada directamente
                var documentacion = await _context.DocumentacionUsuarios
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.UserId == userId &&
                                             d.CodigoPais == codigoPais &&
                                             d.EstadoVerificacion == "Aprobado");

                return documentacion != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar requisitos para usuario {userId} en país {codigoPais}");
                return false;
            }
        }

        public async Task<DocumentacionUsuario?> ObtenerDocumentacionUsuario(string userId, string codigoPais)
        {
            try
            {
                // Corrección: usar la entidad tipada directamente
                return await _context.DocumentacionUsuarios
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.UserId == userId && d.CodigoPais == codigoPais);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener documentación para usuario {userId} en país {codigoPais}");
                return null;
            }
        }

        public async Task<bool> GuardarDocumentacion(DocumentacionUsuario documentacion)
        {
            try
            {
                // Verificar si ya existe documentación para actualizar
                // Corrección: usar la entidad tipada directamente
                var docExistente = await _context.DocumentacionUsuarios
                    .FirstOrDefaultAsync(d => d.UserId == documentacion.UserId &&
                                             d.CodigoPais == documentacion.CodigoPais);

                if (docExistente != null)
                {
                    // Actualizar campos existentes sin perder los que no se modifican
                    if (!string.IsNullOrEmpty(documentacion.IdentificacionUrl))
                    {
                        docExistente.IdentificacionUrl = documentacion.IdentificacionUrl;
                        docExistente.FechaSubidaIdentificacion = DateTime.UtcNow;
                    }

                    if (!string.IsNullOrEmpty(documentacion.ComprobanteDomicilioUrl))
                    {
                        docExistente.ComprobanteDomicilioUrl = documentacion.ComprobanteDomicilioUrl;
                        docExistente.FechaSubidaComprobanteDomicilio = DateTime.UtcNow;
                    }

                    if (!string.IsNullOrEmpty(documentacion.DocumentoFiscalUrl))
                    {
                        docExistente.DocumentoFiscalUrl = documentacion.DocumentoFiscalUrl;
                        docExistente.FechaSubidaDocumentoFiscal = DateTime.UtcNow;
                    }

                    if (!string.IsNullOrEmpty(documentacion.FormularioRegulatorioUrl))
                    {
                        docExistente.FormularioRegulatorioUrl = documentacion.FormularioRegulatorioUrl;
                        docExistente.FechaSubidaFormularioRegulatorio = DateTime.UtcNow;
                    }

                    // Cambiar estado a pendiente cuando se actualiza documentación
                    docExistente.EstadoVerificacion = "Pendiente";
                    docExistente.MotivoRechazo = null;
                }
                else
                {
                    // Crear nuevo registro
                    _context.DocumentacionUsuarios.Add(documentacion);
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al guardar documentación para usuario {documentacion.UserId}");
                return false;
            }
        }

        public async Task<bool> VerificarDocumentacion(int documentacionId, bool aprobada, string? motivoRechazo = null)
        {
            try
            {
                // Corrección: usar la entidad tipada directamente
                var documentacion = await _context.DocumentacionUsuarios.FindAsync(documentacionId);

                if (documentacion == null)
                {
                    _logger.LogWarning($"No se encontró documentación con ID {documentacionId}");
                    return false;
                }

                documentacion.EstadoVerificacion = aprobada ? "Aprobado" : "Rechazado";
                documentacion.FechaVerificacion = DateTime.UtcNow;

                if (!aprobada && !string.IsNullOrEmpty(motivoRechazo))
                {
                    documentacion.MotivoRechazo = motivoRechazo;
                }
                else
                {
                    documentacion.MotivoRechazo = null;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar documentación {documentacionId}");
                return false;
            }
        }
    }
}