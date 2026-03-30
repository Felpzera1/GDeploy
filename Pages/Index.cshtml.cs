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
using System.Net;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace GtopPdqNet.Pages 
{
    [Authorize] 
    public class IndexModel : PageModel 
    {
        private readonly IPowerShellService _psService;
        private readonly ILogger<IndexModel> _logger;
        private readonly AuditService _auditService;
        private readonly IWebHostEnvironment _environment;

        // 1. Lista de Bloqueio (Blacklist) de Hosts e IPs individuais
        private readonly List<string> _blacklistedHosts = new List<string> 
        { 
            "addc.redetop.com.br", 
            "ad002.redetop.com.br",
            "apps01.redetop.com.br",
            "apps02.redetop.com.br",
            "appsdfa.redetop.com.br",
            "appsnr.redetop.com.br",
            "bi.redetop.com.br",
            "crmwin2.redetop.com.br",
            "crmwin.redetop.com.br",
            "dhcp-mtz.redetop.com.br",
            "fs01.redetop.com.br",
            "grogu.redetop.com.br",
            "iis02.redetop.com.br",
            "mhades.redetop.com.br",
            "mid02.redetop.com.br",
            "pdqweb.redetop.com.br",
            "printserver.redetop.com.br",
            "skywalker.redetop.com.br",
            "xwing.redetop.com.br",
            "rds01.redetop.com.br",
            "rds02.redetop.com.br",
            "rds03.redetop.com.br",
            "rds04.redetop.com.br",
            "rds05.redetop.com.br",
            "rds06.redetop.com.br",
            "10.12.6.248", 
            "10.1.151.235"
        };

        // 2. Prefixos de Hostnames Bloqueados (Ex: Servidores UNloja e ADloja)
        private readonly List<string> _blacklistedPrefixes = new List<string> 
        { 
            "UN", 
            "AD" 
        };

        // 3. Sub-redes (VLANs) Bloqueadas no formato CIDR
        private readonly List<string> _blacklistedSubnets = new List<string>
        {
            "10.12.6.0/24",
            "10.1.154.0/24",
            "10.1.151.0/24"
        };

        [BindProperty]
        [Required(ErrorMessage = "O Hostname ou IP é obrigatório.")]
        [Display(Name = "Hostname / IP")]
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

        public IndexModel(IPowerShellService psService, ILogger<IndexModel> logger, AuditService auditService, IWebHostEnvironment environment)
        {
            _psService = psService;
            _logger = logger;
            _auditService = auditService;
            _environment = environment;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var packages = await _psService.GetPdqPackagesAsync();
                PackageOptions = packages != null && packages.Any() ? new SelectList(packages) : new SelectList(new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao carregar pacotes.");
                PackageOptions = new SelectList(new List<string>());
                ViewData["ErrorMessage"] = "Erro ao carregar pacotes.";
            }
            return Page();
        }

        public async Task<JsonResult> OnPostDeployAsync(string hostname, string selectedPackage)
        {
            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(selectedPackage)) {
                 return new JsonResult(new { success = false, log = "ERRO: Hostname/IP e Pacote são obrigatórios." });
            }

            var trimmedHostname = hostname.Trim();
            
            // --- VALIDAÇÃO DE SEGURANÇA (BLACKLIST) ---
            if (_blacklistedHosts.Any(bh => bh.Equals(trimmedHostname, StringComparison.OrdinalIgnoreCase)))
                return SecurityBlockResponse(hostname, "está na lista de bloqueio individual");

            if (_blacklistedPrefixes.Any(bp => trimmedHostname.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
                return SecurityBlockResponse(hostname, "possui um prefixo restrito (UN/AD)");

            if (IPAddress.TryParse(trimmedHostname, out var ipAddress))
            {
                foreach (var subnet in _blacklistedSubnets)
                {
                    if (IsIpInSubnet(ipAddress, subnet))
                        return SecurityBlockResponse(hostname, $"pertence a uma sub-rede bloqueada ({subnet})");
                }
            }

            var logBuilder = new StringBuilder();
            var username = User?.Identity?.Name ?? "Usuário Desconhecido";

            try {
                logBuilder.AppendLine($"-> Verificando conectividade com \'{hostname}\'...");
                using (var pingSender = new Ping()) {
                    try {
                        var reply = await pingSender.SendPingAsync(hostname, 3000);
                        if (reply.Status == IPStatus.Success) {
                            logBuilder.AppendLine($"   SUCESSO: Host \'{hostname}\' ({reply.Address}) respondeu em {reply.RoundtripTime}ms.");
                            
                            if (IPAddress.TryParse(reply.Address.ToString(), out var resolvedIp))
                            {
                                foreach (var subnet in _blacklistedSubnets)
                                {
                                    if (IsIpInSubnet(resolvedIp, subnet))
                                        return SecurityBlockResponse(hostname, $"resolve para o IP {resolvedIp}, que pertence a uma sub-rede bloqueada ({subnet})");
                                }
                            }

                            logBuilder.AppendLine("---------------------------------------");
                            logBuilder.AppendLine("-> Iniciando o comando de deploy PDQ...");
                        } else {
                            logBuilder.AppendLine($"FALHA CONEXÃO: Host \'{hostname}\' NÃO respondeu ao ping (Status: {reply.Status}).");
                            await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                            return new JsonResult(new { success = false, log = logBuilder.ToString() });
                        }
                    } catch (Exception ex) {
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host \'{hostname}\' inacessível ou erro de DNS: {ex.Message}");
                        await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }
                }

                var (deploySuccess, deployOutput) = await _psService.ExecutePdqDeployAsync(hostname, selectedPackage);
                logBuilder.AppendLine("--- Saída do Script PowerShell ---");
                logBuilder.Append(deployOutput ?? "[Nenhuma saída]");
                
                var fullLog = logBuilder.ToString();
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, deploySuccess, fullLog);
                return new JsonResult(new { success = deploySuccess, log = fullLog });

            } catch (Exception ex) {
                logBuilder.AppendLine($"\nERRO INESPERADO: {ex.Message}");
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                return new JsonResult(new { success = false, log = logBuilder.ToString() });
            }
        }

        public IActionResult OnGetGetLatestLogs()
        {
            try
            {
                string logPath = Path.Combine(_environment.ContentRootPath, "scripts", "deploy_stdout.log");
                if (!System.IO.File.Exists(logPath)) return new JsonResult(new { log = "" });

                // Abre o arquivo permitindo leitura mesmo se outro processo estiver escrevendo
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string content = sr.ReadToEnd();
                    return new JsonResult(new { log = content });
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { log = $"Erro ao ler log: {ex.Message}" });
            }
        }

        private JsonResult SecurityBlockResponse(string hostname, string reason)
        {
            _logger.LogWarning("SEGURANÇA: Bloqueio de deploy para {Hostname} - Motivo: {Reason}", hostname, reason);
            return new JsonResult(new { 
                success = false, 
                log = $"ALERTA DE SEGURANÇA: O host '{hostname}' {reason} e não pode receber deploys via GDeploy." 
            });
        }

        private bool IsIpInSubnet(IPAddress ip, string cidrSubnet)
        {
            try
            {
                var parts = cidrSubnet.Split('/');
                if (parts.Length != 2) return false;
                var subnetAddress = IPAddress.Parse(parts[0]);
                var maskLength = int.Parse(parts[1]);
                if (ip.AddressFamily != subnetAddress.AddressFamily) return false;
                byte[] ipBytes = ip.GetAddressBytes();
                byte[] subnetBytes = subnetAddress.GetAddressBytes();
                byte[] maskBytes = new byte[ipBytes.Length];
                for (int i = 0; i < maskBytes.Length; i++)
                {
                    if (maskLength >= 8) { maskBytes[i] = 0xFF; maskLength -= 8; }
                    else if (maskLength > 0) { maskBytes[i] = (byte)(0xFF << (8 - maskLength)); maskLength = 0; }
                    else { maskBytes[i] = 0x00; }
                }
                for (int i = 0; i < ipBytes.Length; i++)
                {
                    if ((ipBytes[i] & maskBytes[i]) != (subnetBytes[i] & maskBytes[i])) return false;
                }
                return true;
            }
            catch { return false; }
        }

        public async Task<IActionResult> OnPostRefreshPackagesAsync()
        {
            try { await _psService.RefreshPdqPackagesCacheAsync(); StatusMessagePackages = "Cache atualizado!"; }
            catch { StatusMessagePackages = "Erro ao atualizar cache."; }
            return RedirectToPage(); 
        }
    }
}
