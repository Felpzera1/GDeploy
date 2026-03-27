using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using GtopPdqNet.Interfaces; 
using GtopPdqNet.Services;
using System.Net.NetworkInformation; 
using System.Text;
using Microsoft.AspNetCore.Http;

namespace GtopPdqNet.Pages 
{
    [Authorize] 
    public class IndexModel : PageModel 
    {
        private readonly IPowerShellService _psService;
        private readonly ILogger<IndexModel> _logger;
        private readonly AuditService _auditService;

        // Lista de Bloqueio (Blacklist) de Hosts e IPs Críticos
        private readonly List<string> _blacklistedHosts = new List<string> 
        { 
            "addc.redetop.com.br", 
            "ad002.redetop.com.br",
            "10.12.6.248", 
            "10.1.151.235"
        };

        [BindProperty]
        [Required(ErrorMessage = "O Hostname ou IP é obrigatório.")]
        [Display(Name = "Hostname / IP")]
        // Regex atualizada para aceitar prefixos (CN, TOP, PDV, RDS) OU IPs (v4), separados por ponto e vírgula (;)
        [RegularExpression(@"^(?i)((CN|TOP|PDV|RDS)[^;]*|(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}))(;(CN|TOP|PDV|RDS)[^;]*|;(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})){0,4}$", 
        ErrorMessage = "Formato inválido. Use até 5 computadores (prefixos CN, TOP, PDV, RDS ou IP), separados por (;).")]
        public string Hostname { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Selecione um pacote.")]
        [Display(Name = "Pacotes")]
        public string SelectedPackage { get; set; } = string.Empty;

        public SelectList? PackageOptions { get; set; }

        [TempData]
        public string? StatusMessagePackages { get; set; }

        public IndexModel(IPowerShellService psService, ILogger<IndexModel> logger, AuditService auditService)
        {
            _psService = psService;
            _logger = logger;
            _auditService = auditService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("IndexModel.OnGetAsync: Carregando pacotes PDQ...");
            try
            {
                var packages = await _psService.GetPdqPackagesAsync();
                if (packages != null && packages.Any())
                {
                     PackageOptions = new SelectList(packages);
                }
                else
                {
                     PackageOptions = new SelectList(new List<string>());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnGetAsync: Erro ao carregar pacotes PDQ.");
                 PackageOptions = new SelectList(new List<string>());
                 ViewData["ErrorMessage"] = "Erro ao carregar pacotes.";
            }
            return Page();
        }

        public async Task<JsonResult> OnPostDeployAsync(string hostname, string selectedPackage)
        {
            _logger.LogInformation("IndexModel.OnPostDeployAsync: Host={Hostname}, Pacote={Package}", hostname, selectedPackage);

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(selectedPackage)) {
                 return new JsonResult(new { success = false, log = "ERRO: Hostname/IP e Pacote são obrigatórios." });
            }

            // --- VALIDAÇÃO DE SEGURANÇA (BLACKLIST) ---
            if (_blacklistedHosts.Any(bh => bh.Equals(hostname.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("IndexModel.OnPostDeployAsync: TENTATIVA DE DEPLOY EM HOST BLOQUEADO: {Hostname}", hostname);
                return new JsonResult(new { 
                    success = false, 
                    log = $"ALERTA DE SEGURANÇA: O host/IP '{hostname}' está na lista de bloqueio e não pode receber deploys via GDeploy. Entre em contato com o administrador." 
                });
            }

            var logBuilder = new StringBuilder();
            var username = User?.Identity?.Name ?? "Usuário Desconhecido";

            try {
                 _logger.LogInformation("IndexModel.OnPostDeployAsync: Verificando conectividade com: {Hostname}", hostname);
                 logBuilder.AppendLine($"-> Verificando conectividade com \'{hostname}\'...");
                using (var pingSender = new Ping()) {
                    PingReply reply;
                    try {
                        reply = await pingSender.SendPingAsync(hostname, 5000); 
                    } catch (PingException PEx) {
                        _logger.LogWarning(PEx, "IndexModel.OnPostDeployAsync: Ping para {Hostname} falhou.", hostname);
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host \'{hostname}\' não resolvido ou inacessível.");
                        await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }

                    if (reply.Status == IPStatus.Success) {
                        logBuilder.AppendLine($"   SUCESSO: Host \'{hostname}\' ({reply.Address}) respondeu em {reply.RoundtripTime}ms.");
                        logBuilder.AppendLine("---------------------------------------");
                        logBuilder.AppendLine("-> Iniciando o comando de deploy PDQ...");
                    } else {
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host \'{hostname}\' NÃO respondeu ao ping (Status: {reply.Status}). Deploy cancelado.");
                        await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }
                }

                var (deploySuccess, deployOutput) = await _psService.ExecutePdqDeployAsync(hostname, selectedPackage);
                
                logBuilder.AppendLine("--- Saída do Script PowerShell ---");
                logBuilder.Append(deployOutput ?? "[Nenhuma saída do script]");

                if (!deploySuccess) {
                     logBuilder.AppendLine("\nFALHA: O processo de deploy não foi concluído com sucesso.");
                } else {
                    logBuilder.AppendLine("\nSUCESSO: Comando de deploy enviado/concluído com sucesso.");
                }

                var fullLog = logBuilder.ToString();
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, deploySuccess, fullLog);

                return new JsonResult(new { success = deploySuccess, log = fullLog });

            } catch (Exception ex) {
                 _logger.LogError(ex, "IndexModel.OnPostDeployAsync: Erro INESPERADO para {Hostname}", hostname);
                 logBuilder.AppendLine($"\nERRO INESPERADO NO SERVIDOR: {ex.Message}");
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                return new JsonResult(new { success = false, log = logBuilder.ToString() });
            }
        }

        public async Task<IActionResult> OnPostRefreshPackagesAsync()
        {
            try
            {
                await _psService.RefreshPdqPackagesCacheAsync();
                StatusMessagePackages = "Cache de pacotes PDQ atualizado com sucesso!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnPostRefreshPackagesAsync: Erro ao atualizar cache.");
                StatusMessagePackages = "Erro ao atualizar o cache de pacotes PDQ.";
            }
            return RedirectToPage(); 
        }
    }
}
