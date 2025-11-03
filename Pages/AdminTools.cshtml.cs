using GtopPdqNet.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace GtopPdqNet.Pages
{
    public class AdminToolsModel : PageModel
    {
        private readonly IPowerShellService _powerShellService;
        private readonly ILogger<AdminToolsModel> _logger;

        [TempData]
        public string? StatusMessage { get; set; }

        public AdminToolsModel(IPowerShellService powerShellService, ILogger<AdminToolsModel> logger)
        {
            _powerShellService = powerShellService;
            _logger = logger;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostRefreshPackagesAsync()
        {
            _logger.LogInformation("Solicitação para atualizar cache de pacotes PDQ recebida.");
            try
            {
                await _powerShellService.RefreshPdqPackagesCacheAsync();
                StatusMessage = "Cache de pacotes PDQ atualizado com sucesso!";
                _logger.LogInformation("Cache de pacotes PDQ atualizado com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao tentar atualizar o cache de pacotes PDQ.");
                StatusMessage = "Erro ao atualizar o cache de pacotes PDQ.";
            }
            return RedirectToPage(); // Redireciona para a mesma página (OnGet)
        }
    }
}
