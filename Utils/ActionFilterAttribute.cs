using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TelefonicaEmpresaria.Utils
{
    public class ValidateAntiForgeryTokenAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var antiforgery = context.HttpContext.RequestServices.GetService<IAntiforgery>();

            // Verifica el token solo para métodos no GET
            if (context.HttpContext.Request.Method != "GET" &&
                context.HttpContext.Request.Method != "HEAD" &&
                context.HttpContext.Request.Method != "OPTIONS" &&
                context.HttpContext.Request.Method != "TRACE")
            {
                try
                {
                    antiforgery.ValidateRequestAsync(context.HttpContext).GetAwaiter().GetResult();
                }
                catch (AntiforgeryValidationException)
                {
                    context.Result = new BadRequestObjectResult("CSRF token validation failed");
                    return;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
