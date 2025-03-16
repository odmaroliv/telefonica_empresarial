namespace TelefonicaEmpresarial.Services
{
    public interface IValidationService
    {
        bool IsValidPhoneNumber(string phoneNumber);
        bool IsValidRedirectNumber(string redirectNumber, string countryCode);
        bool IsValidEmail(string email);
        bool IsValidRFC(string rfc);
    }

    public class ValidationService : IValidationService
    {
        private readonly ILogger<ValidationService> _logger;

        public ValidationService(ILogger<ValidationService> logger)
        {
            _logger = logger;
        }

        public bool IsValidPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return false;

            // Patrón básico E.164: + seguido de 7-15 dígitos
            var pattern = @"^\+[1-9]\d{6,14}$";
            var isValid = System.Text.RegularExpressions.Regex.IsMatch(phoneNumber, pattern);

            if (!isValid)
            {
                _logger.LogWarning($"Número telefónico inválido: {phoneNumber}");
            }

            return isValid;
        }

        public bool IsValidRedirectNumber(string redirectNumber, string countryCode)
        {
            if (string.IsNullOrWhiteSpace(redirectNumber))
                return false;

            // Validar formato básico
            if (!IsValidPhoneNumber(redirectNumber))
                return false;

            // Verificar país específico (si es necesario)
            switch (countryCode)
            {
                case "MX":
                    // Validación específica para México: +52 seguido de 10 dígitos
                    return System.Text.RegularExpressions.Regex.IsMatch(redirectNumber, @"^\+52[1-9]\d{9}$");

                case "US":
                    // Validación específica para Estados Unidos: +1 seguido de 10 dígitos
                    return System.Text.RegularExpressions.Regex.IsMatch(redirectNumber, @"^\+1[2-9]\d{9}$");

                default:
                    // Validación genérica para otros países
                    return true;
            }
        }

        public bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                // Usar la validación del framework
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public bool IsValidRFC(string rfc)
        {
            if (string.IsNullOrWhiteSpace(rfc))
                return false;

            // Verificar formato de RFC mexicano (simplificado):
            // Persona moral: 12 caracteres (3 letras + 6 dígitos + 3 caracteres)
            // Persona física: 13 caracteres (4 letras + 6 dígitos + 3 caracteres)
            var pattern = @"^([A-ZÑ&]{3,4})(\d{6})([A-Z0-9]{3})$";
            return System.Text.RegularExpressions.Regex.IsMatch(rfc.ToUpper(), pattern);
        }
    }
}