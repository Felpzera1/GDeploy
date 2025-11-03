// Pages/Logout.cshtml.cs - Lógica para processar o logout
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging; // Para ILogger
using System.Threading.Tasks; // Para Task

namespace GtopPdqNet.Pages
{
    public class LogoutModel : PageModel
    {
        private readonly ILogger<LogoutModel> _logger;

        public LogoutModel(ILogger<LogoutModel> logger)
        {
            _logger = logger;
        }

        // Chamado via POST do formulário de logout (recomendado)
        public async Task<IActionResult> OnPost(string? returnUrl = null)
        {
            _logger.LogInformation("Usuário {UserName} saindo...", User.Identity?.Name); // User.Identity.Name pega o nome do cookie
            // Desautentica o usuário (remove o cookie de autenticação)
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("Usuário saiu com sucesso.");

            if (returnUrl != null)
            {
                return LocalRedirect(returnUrl); // Redireciona para a URL especificada
            }
            else
            {
                return RedirectToPage("/Index"); // Redireciona para a página inicial padrão
            }
        }

        // Chamado via GET (se alguém acessar /Logout diretamente) - Opcional
        public async Task<IActionResult> OnGet(string? returnUrl = null)
        {
             _logger.LogWarning("Tentativa de logout via GET.");
             return await OnPost(returnUrl); // Reusa a lógica do POST
        }
    }
}