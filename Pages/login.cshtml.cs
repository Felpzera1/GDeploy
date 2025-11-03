// Arquivo: Pages/Login.cshtml.cs (Modificado)
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // <<< ADICIONAR PARA IOptions
using System; // <<< ADICIONAR PARA StringComparer
using System.Linq; // <<< ADICIONAR PARA .Contains

// Substitua GtopPdqNet.Services pelo namespace correto do seu AuthService e LdapSettings
// using GtopPdqNet.Services;
// using GtopPdqNet.Models; // Se LdapSettings estiver em Models

namespace GtopPdqNet.Pages // Certifique-se que este namespace está correto
{
    public class LoginModel : PageModel
    {
        private readonly AuthService _authService; // Certifique-se que AuthService está acessível (usando correto)
        private readonly ILogger<LoginModel> _logger;
        private readonly LdapSettings _ldapSettings; // Para pegar o AllowedGroupCn

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public string? ReturnUrl { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "O nome de usuário é obrigatório.")]
            [Display(Name = "Usuário")]
            public string Username { get; set; } = string.Empty;

            [Required(ErrorMessage = "A senha é obrigatória.")]
            [DataType(DataType.Password)]
            [Display(Name = "Senha")]
            public string Password { get; set; } = string.Empty;
        }

        // Injete AuthService, ILogger e IOptions<LdapSettings>
        public LoginModel(AuthService authService, ILogger<LoginModel> logger, IOptions<LdapSettings> ldapSettings)
        {
            _authService = authService;
            _logger = logger;
            _ldapSettings = ldapSettings.Value ?? throw new ArgumentNullException(nameof(ldapSettings), "Configurações LDAP (LdapSettings) não podem ser nulas.");
        }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Login POST: ModelState inválido.");
                return Page();
            }

            _logger.LogInformation("Login POST: Tentando autenticar usuário: {Username}", Input.Username);

            var (isAuthenticated, userPrincipal, userGroups, authErrorMessage) = 
                await _authService.AuthenticateUser(Input.Username, Input.Password);

            if (isAuthenticated && userPrincipal != null && userGroups != null)
            {
                _logger.LogInformation("Login POST: Usuário {UserPrincipal} autenticado com sucesso. Grupos encontrados: {UserGroupsCount}", userPrincipal, userGroups.Count);

                // >>> INÍCIO DA VALIDAÇÃO DE GRUPO <<<
                if (!string.IsNullOrWhiteSpace(_ldapSettings.AllowedGroupCn))
                {
                    _logger.LogInformation("Verificando pertença ao grupo obrigatório: '{AllowedGroupCn}'", _ldapSettings.AllowedGroupCn);
                    if (!userGroups.Contains(_ldapSettings.AllowedGroupCn, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Usuário {UserPrincipal} autenticado, mas NÃO pertence ao grupo permitido '{AllowedGroupCn}'. Acesso negado.", 
                                         userPrincipal, _ldapSettings.AllowedGroupCn);
                        ErrorMessage = $"Usuário autenticado, mas não tem permissão para acessar esta aplicação (não pertence ao grupo '{_ldapSettings.AllowedGroupCn}').";
                        // Opcional: Deslogar explicitamente se um cookie parcial foi criado, embora não deva ser o caso aqui.
                        // await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return Page();
                    }
                    _logger.LogInformation("Usuário {UserPrincipal} PERTENCE ao grupo permitido '{AllowedGroupCn}'.", userPrincipal, _ldapSettings.AllowedGroupCn);
                }
                else
                {
                    _logger.LogInformation("Nenhum grupo específico (AllowedGroupCn) configurado em appsettings.json. Permitindo acesso para qualquer usuário autenticado via LDAP.");
                }
                // >>> FIM DA VALIDAÇÃO DE GRUPO <<<

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userPrincipal), // Usar NameIdentifier para ID único
                    new Claim(ClaimTypes.Name, userPrincipal), // Usar Name para nome de exibição/login
                    // Adicionar outras claims se necessário, como email, nome completo, etc., se retornadas pelo LDAP
                };

                // Adicionar cada grupo do LDAP como uma claim de Papel (Role)
                _logger.LogInformation("Adicionando {GroupCount} claims de papel (role) para o usuário {UserPrincipal}...", userGroups.Count, userPrincipal);
                foreach (var groupName in userGroups)
                {
                    claims.Add(new Claim(ClaimTypes.Role, groupName));
                    _logger.LogDebug(" - Claim de Papel Adicionada: {GroupName}", groupName);
                }

                var claimsIdentity = new ClaimsIdentity(
                    claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true, 
                    IsPersistent = false, // Mude para true se quiser "Lembrar-me"
                    // ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60) // Definido globalmente nas opções do cookie
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                _logger.LogInformation("Login POST: Cookie de autenticação criado para {UserPrincipal}. Redirecionando para {ReturnUrl}", userPrincipal, ReturnUrl);
                return LocalRedirect(ReturnUrl);
            }
            else
            {
                _logger.LogWarning("Login POST: Falha na autenticação para o usuário {Username}. Erro: {AuthErrorMessage}", Input.Username, authErrorMessage);
                ErrorMessage = authErrorMessage ?? "Usuário ou senha inválidos, ou falha ao obter informações do usuário.";
                return Page();
            }
        }
    } 
}

