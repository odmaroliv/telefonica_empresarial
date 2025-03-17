// Script para manejar las llamadas salientes
let verificacionInterval = null;
let dotNetHelper = null;
let llamadaId = null;

// Iniciar verificación periódica del estado de una llamada
export function iniciarVerificacionEstado(dotNetRef, idLlamada) {
    // Guardar referencia para invocar métodos dotnet
    dotNetHelper = dotNetRef;
    llamadaId = idLlamada;

    // Verificar inmediatamente y luego cada 5 segundos
    verificarEstadoLlamada();
    verificacionInterval = setInterval(verificarEstadoLlamada, 5000);
}

// Detener la verificación periódica
export function detenerVerificacionEstado() {
    if (verificacionInterval) {
        clearInterval(verificacionInterval);
        verificacionInterval = null;
    }
}

// Verificar el estado actual de la llamada
async function verificarEstadoLlamada() {
    if (!llamadaId) return;

    try {
        const response = await fetch(`/api/llamadas/${llamadaId}`);
        if (response.ok) {
            const data = await response.json();

            // Verificar si la llamada ha finalizado
            const estadosFinalizados = ['completada', 'fallida', 'cancelada'];
            const finalizada = estadosFinalizados.includes(data.estado);

            // Actualizar el componente con el nuevo estado
            if (dotNetHelper) {
                await dotNetHelper.invokeMethodAsync('ActualizarEstadoLlamada', data.estado, finalizada);

                // Si la llamada finalizó, detener la verificación
                if (finalizada) {
                    detenerVerificacionEstado();
                }
            }
        } else {
            console.error('Error al verificar estado de llamada:', await response.text());
        }
    } catch (error) {
        console.error('Error al verificar estado de llamada:', error);
    }
}

// Formatear número telefónico para visualización
export function formatearNumero(numero) {
    if (!numero) return '';

    // Limpiar el número
    numero = numero.replace(/[\s\-\(\)]/g, '');

    // Aplicar formato según el país
    if (numero.startsWith('+52') && numero.length >= 12) {
        // Formato México: +52 (XXX) XXX-XXXX
        return `+52 (${numero.substring(3, 6)}) ${numero.substring(6, 9)}-${numero.substring(9)}`;
    } else if (numero.startsWith('+1') && numero.length >= 11) {
        // Formato EE.UU./Canadá: +1 (XXX) XXX-XXXX
        return `+1 (${numero.substring(2, 5)}) ${numero.substring(5, 8)}-${numero.substring(8)}`;
    }

    return numero;
}

// Estimar duración de la llamada basado en el saldo disponible y el costo por minuto
export function estimarDuracionMaxima(saldoDisponible, costoPorMinuto) {
    if (!costoPorMinuto || costoPorMinuto <= 0) return 0;

    const minutosEstimados = Math.floor(saldoDisponible / costoPorMinuto);
    return minutosEstimados;
}

// Reproducir sonido de llamada
let sonidoLlamada = null;

export function reproducirSonidoLlamada() {
    detenerSonidoLlamada();

    sonidoLlamada = new Audio('/sounds/ring.mp3');
    sonidoLlamada.loop = true;
    sonidoLlamada.play().catch(e => console.log('Error al reproducir sonido:', e));
}

export function detenerSonidoLlamada() {
    if (sonidoLlamada) {
        sonidoLlamada.pause();
        sonidoLlamada.currentTime = 0;
        sonidoLlamada = null;
    }
}