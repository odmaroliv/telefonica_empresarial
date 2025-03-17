using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services;

namespace TelefonicaEmpresarial.Controllers
{
    [ApiController]
    [Route("api/llamadas")]
    [Authorize]
    public class LlamadasController : ControllerBase
    {
        private readonly ILlamadasService _llamadasService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LlamadasController> _logger;

        public LlamadasController(
            ILlamadasService llamadasService,
            UserManager<ApplicationUser> userManager,
            ILogger<LlamadasController> logger)
        {
            _llamadasService = llamadasService;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        /// Inicia una llamada telefónica desde un número del usuario
        /// </summary>
        [HttpPost("iniciar")]
        public async Task<IActionResult> IniciarLlamada([FromBody] IniciarLlamadaRequest request)
        {
            try
            {
                // Obtener el usuario autenticado
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("Usuario no autenticado");
                }

                // Validar los datos de entrada
                if (request == null || request.NumeroTelefonicoId <= 0 || string.IsNullOrEmpty(request.NumeroDestino))
                {
                    return BadRequest("Datos de entrada inválidos");
                }

                // Iniciar la llamada
                var (llamada, error) = await _llamadasService.IniciarLlamada(
                    request.NumeroTelefonicoId,
                    request.NumeroDestino,
                    userId
                );

                if (!string.IsNullOrEmpty(error))
                {
                    return BadRequest(error);
                }

                return Ok(new
                {
                    llamadaId = llamada.Id,
                    estado = llamada.Estado,
                    fechaInicio = llamada.FechaInicio,
                    numeroDestino = llamada.NumeroDestino
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al iniciar llamada");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }

        /// <summary>
        /// Obtiene el estado actual de una llamada
        /// </summary>
        [HttpGet("{llamadaId}")]
        public async Task<IActionResult> ObtenerEstadoLlamada(int llamadaId)
        {
            try
            {
                // Obtener el usuario autenticado
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("Usuario no autenticado");
                }

                // Obtener el estado de la llamada
                var (llamada, error) = await _llamadasService.ObtenerEstadoLlamada(llamadaId, userId);

                if (!string.IsNullOrEmpty(error))
                {
                    return BadRequest(error);
                }

                return Ok(new
                {
                    llamadaId = llamada.Id,
                    estado = llamada.Estado,
                    fechaInicio = llamada.FechaInicio,
                    fechaFin = llamada.FechaFin,
                    duracion = llamada.Duracion,
                    costo = llamada.Costo,
                    numeroDestino = llamada.NumeroDestino
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener estado de llamada {llamadaId}");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }

        /// <summary>
        /// Finaliza una llamada en curso
        /// </summary>
        [HttpPost("{llamadaId}/finalizar")]
        public async Task<IActionResult> FinalizarLlamada(int llamadaId)
        {
            try
            {
                // Obtener el usuario autenticado
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("Usuario no autenticado");
                }

                // Finalizar la llamada
                bool resultado = await _llamadasService.FinalizarLlamada(llamadaId, userId);

                if (!resultado)
                {
                    return BadRequest("No se pudo finalizar la llamada");
                }

                return Ok(new { mensaje = "Llamada finalizada correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al finalizar llamada {llamadaId}");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }

        /// <summary>
        /// Obtiene el historial de llamadas del usuario
        /// </summary>
        [HttpGet("historial")]
        public async Task<IActionResult> ObtenerHistorialLlamadas([FromQuery] int limite = 50)
        {
            try
            {
                // Obtener el usuario autenticado
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("Usuario no autenticado");
                }

                // Obtener el historial de llamadas
                var llamadas = await _llamadasService.ObtenerHistorialLlamadas(userId, limite);

                // Mapear a un DTO para la respuesta
                var resultado = llamadas.Select(l => new
                {
                    llamadaId = l.Id,
                    numeroDestino = l.NumeroDestino,
                    fechaInicio = l.FechaInicio,
                    fechaFin = l.FechaFin,
                    duracion = l.Duracion,
                    estado = l.Estado,
                    costo = l.Costo
                });

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener historial de llamadas");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }

        /// <summary>
        /// Calcula el costo estimado de una llamada
        /// </summary>
        [HttpPost("estimar-costo")]
        public async Task<IActionResult> EstimarCostoLlamada([FromBody] EstimarCostoRequest request)
        {
            try
            {
                // Validar los datos de entrada
                if (request == null || request.DuracionEstimadaMinutos <= 0 ||
                    string.IsNullOrEmpty(request.NumeroOrigen) || string.IsNullOrEmpty(request.NumeroDestino))
                {
                    return BadRequest("Datos de entrada inválidos");
                }

                // Calcular el costo estimado
                decimal costoEstimado = await _llamadasService.CalcularCostoEstimadoLlamada(
                    request.NumeroOrigen,
                    request.NumeroDestino,
                    request.DuracionEstimadaMinutos
                );

                return Ok(new { costoEstimado });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al estimar costo de llamada");
                return StatusCode(500, "Error al procesar la solicitud");
            }
        }
    }

    public class IniciarLlamadaRequest
    {
        public int NumeroTelefonicoId { get; set; }
        public string NumeroDestino { get; set; } = string.Empty;
    }

    public class EstimarCostoRequest
    {
        public string NumeroOrigen { get; set; } = string.Empty;
        public string NumeroDestino { get; set; } = string.Empty;
        public int DuracionEstimadaMinutos { get; set; } = 1;
    }
}