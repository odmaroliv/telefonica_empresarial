window.redirectTo = function (url) {
    window.location.href = url;
};
// Función para descargar contenido como archivo CSV
window.downloadCSV = function (filename, content) {
    var blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });

    // Navegadores modernos
    if (navigator.msSaveBlob) { // IE 10+
        navigator.msSaveBlob(blob, filename);
    } else {
        var link = document.createElement('a');
        if (link.download !== undefined) { // Navegadores modernos
            var url = URL.createObjectURL(blob);
            link.setAttribute('href', url);
            link.setAttribute('download', filename);
            link.style.visibility = 'hidden';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        }
    }
};

// Función para inicializar los tooltips de Bootstrap
window.initializeTooltips = function () {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });
};

// Función para inicializar los tabs de Bootstrap
window.initializeTabs = function () {
    var triggerTabList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tab"]'));
    triggerTabList.forEach(function (triggerEl) {
        var tabTrigger = new bootstrap.Tab(triggerEl);
        triggerEl.addEventListener('click', function (event) {
            event.preventDefault();
            tabTrigger.show();
        });
    });
};

// Agregar esto a tu archivo JavaScript existente
window.showNotification = function (message, type) {
    // Determinar la clase CSS según el tipo
    let cssClass = "bg-gray-700";

    if (type === "success") {
        cssClass = "bg-green-600";
    } else if (type === "warning") {
        cssClass = "bg-yellow-600";
    } else if (type === "error") {
        cssClass = "bg-red-600";
    } else if (type === "info") {
        cssClass = "bg-blue-600";
    }

    // Crear el elemento de notificación
    const notification = document.createElement("div");
    notification.className = `fixed top-4 right-4 px-5 py-3 rounded-lg shadow-lg text-white ${cssClass} transform transition-transform duration-300 ease-in-out z-50`;
    notification.innerHTML = message;

    // Agregar al DOM
    document.body.appendChild(notification);

    // Animación de entrada
    setTimeout(() => {
        notification.style.transform = "translateY(0)";
    }, 100);

    // Remover después de un tiempo
    setTimeout(() => {
        notification.style.transform = "translateY(-20px)";
        notification.style.opacity = "0";

        setTimeout(() => {
            document.body.removeChild(notification);
        }, 300);
    }, 4000);
};
window.showAlert = function (title, text, icon) {
    Swal.fire({
        title: title,
        text: text,
        icon: icon, // 'success', 'error', 'warning', 'info'
        confirmButtonText: 'Aceptar'
    });
};