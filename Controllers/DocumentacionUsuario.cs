using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/documentacion")]
    [Authorize]
    public class DocumentacionController : ControllerBase
    {
        private readonly IRequisitosRegulatoriosService _requisitosService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<DocumentacionController> _logger;
        private readonly IWebHostEnvironment _environment;

        public DocumentacionController(
            IRequisitosRegulatoriosService requisitosService,
            UserManager<ApplicationUser> userManager,
            ILogger<DocumentacionController> logger,
            IWebHostEnvironment environment)
        {
            _requisitosService = requisitosService;
            _userManager = userManager;
            _logger = logger;
            _environment = environment;
        }

        [HttpGet("requisitos/{codigoPais}")]
        public async Task<ActionResult<RequisitosRegulatorios>> ObtenerRequisitos(string codigoPais)
        {
            var requisitos = await _requisitosService.ObtenerRequisitosPorPais(codigoPais);

            if (requisitos == null)
            {
                return NotFound($"No se encontraron requisitos para el país {codigoPais}");
            }

            return requisitos;
        }

        [HttpGet("estado/{codigoPais}")]
        public async Task<ActionResult<DocumentacionUsuario>> ObtenerEstadoDocumentacion(string codigoPais)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Usuario no autenticado");
            }

            var documentacion = await _requisitosService.ObtenerDocumentacionUsuario(userId, codigoPais);

            if (documentacion == null)
            {
                return NotFound($"No se encontró documentación para {codigoPais}");
            }

            return documentacion;
        }

        [HttpPost("subir/{codigoPais}/{tipoDocumento}")]
        public async Task<IActionResult> SubirDocumento(string codigoPais, string tipoDocumento)
        {
            try
            {
                var userId = _userManager.GetUserId(User);

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("Usuario no autenticado");
                }

                // Validar el tipo de documento
                if (!new[] { "identificacion", "comprobanteDomicilio", "documentoFiscal", "formularioRegulatorio" }
                    .Contains(tipoDocumento.ToLower()))
                {
                    return BadRequest("Tipo de documento no válido");
                }

                // Verificar que existe el archivo
                if (Request.Form.Files.Count == 0)
                {
                    return BadRequest("No se recibió ningún archivo");
                }

                var archivo = Request.Form.Files[0];

                // Validar tipo de archivo
                var extensionPermitida = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(archivo.FileName).ToLower();

                if (!extensionPermitida.Contains(extension))
                {
                    return BadRequest("Tipo de archivo no permitido. Solo se aceptan PDF, JPG y PNG");
                }

                // Validar tamaño (máximo 5MB)
                if (archivo.Length > 5 * 1024 * 1024)
                {
                    return BadRequest("El archivo excede el tamaño máximo permitido (5MB)");
                }

                // Crear directorio si no existe
                var directorio = Path.Combine(_environment.WebRootPath, "documentos", userId);
                if (!Directory.Exists(directorio))
                {
                    Directory.CreateDirectory(directorio);
                }

                // Generar nombre único para el archivo
                var nombreArchivo = $"{tipoDocumento}_{codigoPais}_{Guid.NewGuid()}{extension}";
                var rutaArchivo = Path.Combine(directorio, nombreArchivo);

                // Guardar archivo
                using (var stream = new FileStream(rutaArchivo, FileMode.Create))
                {
                    await archivo.CopyToAsync(stream);
                }

                // Obtener documentación actual o crear nueva
                var documentacion = await _requisitosService.ObtenerDocumentacionUsuario(userId, codigoPais) ??
                                    new DocumentacionUsuario
                                    {
                                        UserId = userId,
                                        CodigoPais = codigoPais,
                                        EstadoVerificacion = "Pendiente"
                                    };

                // Actualizar la URL del documento según el tipo
                switch (tipoDocumento.ToLower())
                {
                    case "identificacion":
                        documentacion.IdentificacionUrl = $"/documentos/{userId}/{nombreArchivo}";
                        documentacion.FechaSubidaIdentificacion = DateTime.UtcNow;
                        break;

                    case "comprobantedomicilio":
                        documentacion.ComprobanteDomicilioUrl = $"/documentos/{userId}/{nombreArchivo}";
                        documentacion.FechaSubidaComprobanteDomicilio = DateTime.UtcNow;
                        break;

                    case "documentofiscal":
                        documentacion.DocumentoFiscalUrl = $"/documentos/{userId}/{nombreArchivo}";
                        documentacion.FechaSubidaDocumentoFiscal = DateTime.UtcNow;
                        break;

                    case "formularioregulatorio":
                        documentacion.FormularioRegulatorioUrl = $"/documentos/{userId}/{nombreArchivo}";
                        documentacion.FechaSubidaFormularioRegulatorio = DateTime.UtcNow;
                        break;
                }

                // Guardar en base de datos
                var resultado = await _requisitosService.GuardarDocumentacion(documentacion);

                if (!resultado)
                {
                    return StatusCode(500, "No se pudo guardar la información del documento");
                }

                return Ok(new { url = $"/documentos/{userId}/{nombreArchivo}", mensaje = "Documento subido correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al subir documento {tipoDocumento} para país {codigoPais}");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }

        [HttpGet("verificar/{codigoPais}")]
        public async Task<IActionResult> VerificarDocumentacion(string codigoPais)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("Usuario no autenticado");
            }

            var cumpleRequisitos = await _requisitosService.VerificarSiCumpleRequisitos(userId, codigoPais);

            return Ok(new { cumpleRequisitos });
        }

        [HttpPost("admin/verificar/{documentacionId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> VerificarDocumentacionAdmin(int documentacionId, [FromBody] VerificacionRequest request)
        {
            var resultado = await _requisitosService.VerificarDocumentacion(
                documentacionId,
                request.Aprobada,
                request.MotivoRechazo);

            if (!resultado)
            {
                return StatusCode(500, "No se pudo procesar la verificación");
            }

            return Ok(new { mensaje = request.Aprobada ? "Documentación aprobada" : "Documentación rechazada" });
        }
    }

    public class VerificacionRequest
    {
        public bool Aprobada { get; set; }
        public string? MotivoRechazo { get; set; }
    }
}