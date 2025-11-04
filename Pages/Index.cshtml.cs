// Conteúdo completo do Index.cshtml.cs com a correção do jobId = (int?)null
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering; // Para SelectList
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq; // Para .Any()
using System;
using GtopPdqNet.Interfaces; 
using GtopPdqNet.Services; // Para AuditService
using System.Net.NetworkInformation; 
using System.Text;
using Microsoft.AspNetCore.Http;


namespace GtopPdqNet.Pages 
{
    [Authorize] 
    public class IndexModel : PageModel 
    {
        private readonly IPowerShellService _psService;
        private readonly IAWXService _awxService;
        private readonly ILogger<IndexModel> _logger;
        private readonly AuditService _auditService;

        [BindProperty]
        [Required(ErrorMessage = "O Hostname é obrigatório.")]
        [Display(Name = "Hostname")]
        [RegularExpression(@"^(?i)(CN|TOP|PDV|RDS)[^;]*(;(CN|TOP|PDV|RDS)[^;]*){0,4}$", 
        ErrorMessage = "Formato inválido. Use até 5 computadores com prefixos CN, TOP, PDV ou RDS, separados por ponto e vírgula (;).")]
        public string Hostname { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Selecione um pacote.")]
        [Display(Name = "Pacotes")]
        public string SelectedPackage { get; set; } = string.Empty;

        public SelectList? PackageOptions { get; set; }

        // >>> ADICIONAR PARA MENSAGEM DE STATUS DO REFRESH <<<
        [TempData]
        public string? StatusMessagePackages { get; set; }

        public IndexModel(IPowerShellService psService, IAWXService awxService, ILogger<IndexModel> logger, AuditService auditService)
        {
            _psService = psService;
            _awxService = awxService;
            _logger = logger;
            _auditService = auditService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("IndexModel.OnGetAsync: Carregando pacotes PDQ...");
            try
            {
                var packages = await _psService.GetPdqPackagesAsync(); // Isso agora usará o cache!
                if (packages != null && packages.Any())
                {
                     PackageOptions = new SelectList(packages);
                     _logger.LogInformation("IndexModel.OnGetAsync: Carregados {Count} pacotes.", packages.Count);
                }
                else
                {
                     _logger.LogWarning("IndexModel.OnGetAsync: Nenhum pacote PDQ retornado.");
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
                 _logger.LogWarning("IndexModel.OnPostDeployAsync: Parâmetros inválidos.");
                 return new JsonResult(new { success = false, log = "ERRO: Hostname e Pacote são obrigatórios." });
            }

            // Validar o prefixo do hostname
            if (!hostname.ToUpper().StartsWith("CN") && 
                !hostname.ToUpper().StartsWith("TOP") && 
                !hostname.ToUpper().StartsWith("PDV")) {
                _logger.LogWarning("IndexModel.OnPostDeployAsync: Prefixo de hostname inválido: {Hostname}", hostname);
                return new JsonResult(new { success = false, log = $"ERRO: O hostname \'{hostname}\' não tem um prefixo válido (CN, TOP, PDV)." });
            }

            var logBuilder = new StringBuilder();
            var username = User?.Identity?.Name ?? "Usuário Desconhecido"; // Declarar apenas uma vez

            try {
                 _logger.LogInformation("IndexModel.OnPostDeployAsync: Verificando conectividade com: {Hostname}", hostname);
                 logBuilder.AppendLine($"-> Verificando conectividade com \'{hostname}\'...");
                using (var pingSender = new Ping()) {
                    PingReply reply;
                    try {
                        reply = await pingSender.SendPingAsync(hostname, 5000); 
                    } catch (PingException PEx) when (PEx.InnerException is System.Net.Sockets.SocketException sockEx && sockEx.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound) {
                        _logger.LogWarning(PEx, "IndexModel.OnPostDeployAsync: Ping para {Hostname} - Host Não Encontrado (DNS?)", hostname);
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host \'{hostname}\' não resolvido (erro DNS).");
                        
                        // Salvar log de auditoria mesmo em caso de falha
                        await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                        
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }

                    if (reply.Status == IPStatus.Success) {
                        _logger.LogInformation("IndexModel.OnPostDeployAsync: Ping OK para {Hostname}. IP: {IPAddress}, Tempo: {RoundtripTime}ms", hostname, reply.Address, reply.RoundtripTime);
                        logBuilder.AppendLine($"   SUCESSO: Host \'{hostname}\' ({reply.Address}) respondeu em {reply.RoundtripTime}ms.");
                        logBuilder.AppendLine("---------------------------------------");
                        logBuilder.AppendLine("-> Iniciando o comando de deploy PDQ...");
                        logBuilder.AppendLine();
                    } else {
                        _logger.LogWarning("IndexModel.OnPostDeployAsync: Ping FALHOU para {Hostname}. Status: {Status}", hostname, reply.Status);
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host \'{hostname}\' NÃO respondeu ao ping (Status: {reply.Status}). Deploy cancelado.");
                        
                        // Salvar log de auditoria mesmo em caso de falha
                        await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                        
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }
                }

                var (deploySuccess, deployOutput) = await _psService.ExecutePdqDeployAsync(hostname, selectedPackage);
                _logger.LogInformation("IndexModel.OnPostDeployAsync: Resultado do script deploy: Success={Success}", deploySuccess);
                
                logBuilder.AppendLine("--- Saída do Script PowerShell ---");
                logBuilder.Append(deployOutput ?? "[Nenhuma saída do script]");

                if (!deploySuccess) {
                     _logger.LogWarning("IndexModel.OnPostDeployAsync: Script de deploy retornou falha.");
                     logBuilder.AppendLine("\nFALHA: O processo de deploy não foi concluído com sucesso. Verifique os logs acima.");
                } else {
                    logBuilder.AppendLine("\nSUCESSO: Comando de deploy enviado/concluído com sucesso pelo script.");
                }

                // --- SALVAR O LOG COMPLETO NA AUDITORIA ---
                var fullLog = logBuilder.ToString();
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, deploySuccess, fullLog);
                // -----------------------------------------------------------------------

                 return new JsonResult(new { success = deploySuccess, log = fullLog });

            } catch (PingException pingEx) {
                _logger.LogError(pingEx, "IndexModel.OnPostDeployAsync: Erro PING inesperado para {Hostname}", hostname);
                logBuilder.AppendLine($"ERRO PING INESPERADO: {pingEx.Message}");
                
                // Salvar log de auditoria mesmo em caso de erro
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                
                return new JsonResult(new { success = false, log = logBuilder.ToString() });
            } catch (Exception ex) {
                 _logger.LogError(ex, "IndexModel.OnPostDeployAsync: Erro INESPERADO para {Hostname}/{Package}", hostname, selectedPackage);
                 logBuilder.AppendLine($"\n******************\nERRO INESPERADO NO SERVIDOR:\n{ex.Message}\n******************");
                
                // Salvar log de auditoria mesmo em caso de erro
                await _auditService.LogDeployAsync(username, hostname, selectedPackage, false, logBuilder.ToString());
                
                return new JsonResult(new { success = false, log = logBuilder.ToString() });
            }
        }

        // >>> ADICIONAR ESTE MÉTODO PARA O BOTÃO DE ATUALIZAR PACOTES <<<
        public async Task<IActionResult> OnPostRefreshPackagesAsync()
        {
            _logger.LogInformation("IndexModel.OnPostRefreshPackagesAsync: Solicitando atualização do cache de pacotes PDQ.");
            try
            {
                await _psService.RefreshPdqPackagesCacheAsync(); // Isso vai limpar e recarregar o cache
                StatusMessagePackages = "Cache de pacotes PDQ atualizado com sucesso!";
                _logger.LogInformation("IndexModel.OnPostRefreshPackagesAsync: Cache de pacotes PDQ atualizado.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnPostRefreshPackagesAsync: Erro ao tentar atualizar o cache de pacotes PDQ.");
                StatusMessagePackages = "Erro ao atualizar o cache de pacotes PDQ. Verifique os logs do servidor.";
            }
            // Redirecionar para a própria página (OnGet) para recarregar a lista de pacotes do cache atualizado
            return RedirectToPage(); 
        }

        // ============================================
        // NOVOS HANDLERS PARA AWX (LINUX)
        // ============================================

        /// <summary>
        /// Handler para obter a lista de Job Templates disponíveis no AWX.
        /// </summary>
        public async Task<JsonResult> OnGetGetAWXTemplatesAsync()
        {
            _logger.LogInformation("IndexModel.OnGetGetAWXTemplatesAsync: Obtendo Job Templates do AWX.");
            try
            {
                var templates = await _awxService.GetJobTemplatesAsync();
                _logger.LogInformation("IndexModel.OnGetGetAWXTemplatesAsync: Obtidos {Count} templates.", templates.Count);
                return new JsonResult(new { templates = templates });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnGetGetAWXTemplatesAsync: Erro ao obter templates do AWX.");
                return new JsonResult(new { templates = new List<string>(), error = ex.Message });
            }
        }

        /// <summary>
        /// Handler para disparar um Job Template no AWX.
        /// </summary>
        public async Task<JsonResult> OnPostLaunchAWXAsync(string hostname, string templateName)
        {
            _logger.LogInformation("IndexModel.OnPostLaunchAWXAsync: Host={Hostname}, Template={Template}", hostname, templateName);

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(templateName))
            {
                _logger.LogWarning("IndexModel.OnPostLaunchAWXAsync: Parâmetros inválidos.");
                return new JsonResult(new { success = false, log = "ERRO: Hostname e Template são obrigatórios.", jobId = (int?)null });
            }

            var logBuilder = new StringBuilder();
            var username = User?.Identity?.Name ?? "Usuário Desconhecido";

            try
            {
                // --- NOVA VALIDAÇÃO DE HOST NO AWX ---
                bool hostExists = await _awxService.HostExistsInInventoryAsync(hostname);
                if (!hostExists)
                {
                    string errorMessage = $"ERRO: O host '{hostname}' não foi encontrado em nenhum inventário do AWX. Deploy cancelado.";
                    _logger.LogWarning("IndexModel.OnPostLaunchAWXAsync: {Error}", errorMessage);
                    logBuilder.AppendLine(errorMessage);
                    return new JsonResult(new { success = false, log = logBuilder.ToString(), jobId = (int?)null });
                }
                // --- FIM DA VALIDAÇÃO ---

                _logger.LogInformation("IndexModel.OnPostLaunchAWXAsync: Disparando Job Template no AWX para: {Hostname}", hostname);
                logBuilder.AppendLine($"-> Criando inventario ad-hoc para \'{hostname}\'...");

                // --- CRIAR INVENTARIO AD-HOC ---
                // Gerar um nome unico para o inventario temporario
                string tempInventoryName = $"deploy_temp_{hostname}_{DateTime.Now:yyyyMMddHHmmss}";
                int tempInventoryId = await _awxService.CreateTemporaryInventoryAsync(tempInventoryName, 1); // organizationId = 1 (padrao)
                
                if (tempInventoryId == 0)
                {
                    string errorMessage = $"ERRO: Falha ao criar inventario temporario para \'{hostname}\'. Deploy cancelado.";
                    _logger.LogError("IndexModel.OnPostLaunchAWXAsync: {Error}", errorMessage);
                    logBuilder.AppendLine(errorMessage);
                    return new JsonResult(new { success = false, log = logBuilder.ToString(), jobId = (int?)null });
                }

                logBuilder.AppendLine($"OK Inventario ad-hoc criado com sucesso (ID: {tempInventoryId})");
                _logger.LogInformation("IndexModel.OnPostLaunchAWXAsync: Inventario ad-hoc criado. ID: {InventoryId}", tempInventoryId);

                // --- ADICIONAR HOST AO INVENTARIO ---
                logBuilder.AppendLine($"-> Adicionando host \'{hostname}\' ao inventario...");
                bool hostAdded = await _awxService.AddHostToInventoryAsync(tempInventoryId, hostname);
                
                if (!hostAdded)
                {
                    string errorMessage = $"ERRO: Falha ao adicionar host \'{hostname}\' ao inventario temporario. Deploy cancelado.";
                    _logger.LogError("IndexModel.OnPostLaunchAWXAsync: {Error}", errorMessage);
                    logBuilder.AppendLine(errorMessage);
                    
                    // Tentar deletar o inventario criado
                    await _awxService.DeleteInventoryAsync(tempInventoryId);
                    return new JsonResult(new { success = false, log = logBuilder.ToString(), jobId = (int?)null });
                }

                logBuilder.AppendLine($"OK Host adicionado com sucesso ao inventario");
                _logger.LogInformation("IndexModel.OnPostLaunchAWXAsync: Host adicionado ao inventario. Host: {Host}, InventoryId: {InventoryId}", hostname, tempInventoryId);

                // --- DISPARAR JOB TEMPLATE COM INVENTARIO AD-HOC ---
                logBuilder.AppendLine($"-> Disparando Job Template \'{templateName}\' com inventario ad-hoc...");
                int jobId = await _awxService.LaunchJobTemplateAsync(hostname, templateName, tempInventoryId);
                _logger.LogInformation("IndexModel.OnPostLaunchAWXAsync: Job disparado com sucesso. Job ID: {JobId}, InventoryId: {InventoryId}", jobId, tempInventoryId);
                
                logBuilder.AppendLine($"SUCESSO: Job Template disparado com sucesso!");
                logBuilder.AppendLine($"Job ID: {jobId}");
                logBuilder.AppendLine("Monitorando execucao...");

                var fullLog = logBuilder.ToString();
                // NAO salvar log de auditoria aqui - sera salvo quando o Job terminar
                // await _auditService.LogAWXDeployAsync(username, hostname, templateName, true, fullLog);

                // Persistir o Job ID e o Inventory ID em Session para recuperacao apos navegacao
                HttpContext.Session.SetInt32("CurrentAWXJobId", jobId);
                HttpContext.Session.SetString("CurrentAWXHostname", hostname);
                HttpContext.Session.SetString("CurrentAWXTemplateName", templateName);
                HttpContext.Session.SetString("CurrentAWXJobStartTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                HttpContext.Session.SetInt32("CurrentAWXTempInventoryId", tempInventoryId); // Armazenar para limpeza posterior

                return new JsonResult(new { success = true, log = fullLog, jobId = jobId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnPostLaunchAWXAsync: Erro ao disparar Job Template no AWX para {Hostname}", hostname);
                logBuilder.AppendLine($"\nERRO: {ex.Message}");

                var fullLog = logBuilder.ToString();
                // Salvar log de auditoria mesmo em caso de erro
                await _auditService.LogAWXDeployAsync(username, hostname, templateName, false, fullLog);
                
                // Limpar Session em caso de erro
                HttpContext.Session.Remove("CurrentAWXJobId");
                HttpContext.Session.Remove("CurrentAWXHostname");
                HttpContext.Session.Remove("CurrentAWXTemplateName");
                HttpContext.Session.Remove("CurrentAWXJobStartTime");
                
                return new JsonResult(new { success = false, log = fullLog, jobId = (int?)null });
            }
        }

        /// <summary>
        /// Handler para obter o status e o output de um Job do AWX.
        /// Este handler é chamado periodicamente pelo JavaScript para monitorar o progresso.
        /// </summary>
        public async Task<JsonResult> OnGetGetAWXJobStatusAsync(int jobId)
        {
            _logger.LogInformation("IndexModel.OnGetGetAWXJobStatusAsync: Obtendo status do Job ID: {JobId}", jobId);

            try
            {
                var (status, output) = await _awxService.GetJobStatusAndOutputAsync(jobId);
                _logger.LogInformation("IndexModel.OnGetGetAWXJobStatusAsync: Status do Job {JobId}: {Status}", jobId, status);
                
                return new JsonResult(new { status = status, output = output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnGetGetAWXJobStatusAsync: Erro ao obter status do Job {JobId}.", jobId);
                return new JsonResult(new { status = "error", output = $"ERRO: {ex.Message}" });
            }
        }

        public async Task<JsonResult> OnGetFinalizeAWXJobAsync(int jobId, string finalStatus, string output)
        {
            _logger.LogInformation("IndexModel.OnGetFinalizeAWXJobAsync: Finalizando Job ID: {JobId}, Status: {Status}", jobId, finalStatus);

            try
            {
                var username = User?.Identity?.Name ?? "Usuario Desconhecido";
                var hostname = HttpContext.Session.GetString("CurrentAWXHostname") ?? "Desconhecido";
                var templateName = HttpContext.Session.GetString("CurrentAWXTemplateName") ?? "Desconhecido";
                var tempInventoryIdObj = HttpContext.Session.GetInt32("CurrentAWXTempInventoryId");

                bool success = finalStatus == "successful";

                await _auditService.LogAWXDeployAsync(username, hostname, templateName, success, output);
                _logger.LogInformation("IndexModel.OnGetFinalizeAWXJobAsync: Log de auditoria salvo para Job {JobId} com sucesso: {Success}", jobId, success);

                // Deletar inventario temporario
                if (tempInventoryIdObj.HasValue)
                {
                    int tempInventoryId = tempInventoryIdObj.Value;
                    _logger.LogInformation("IndexModel.OnGetFinalizeAWXJobAsync: Deletando inventario temporario {InventoryId}.", tempInventoryId);
                    
                    bool inventoryDeleted = await _awxService.DeleteInventoryAsync(tempInventoryId);
                    if (inventoryDeleted)
                    {
                        _logger.LogInformation("IndexModel.OnGetFinalizeAWXJobAsync: Inventario temporario {InventoryId} deletado com sucesso.", tempInventoryId);
                    }
                    else
                    {
                        _logger.LogWarning("IndexModel.OnGetFinalizeAWXJobAsync: Falha ao deletar inventario temporario {InventoryId}.", tempInventoryId);
                    }
                }

                HttpContext.Session.Remove("CurrentAWXJobId");
                HttpContext.Session.Remove("CurrentAWXHostname");
                HttpContext.Session.Remove("CurrentAWXTemplateName");
                HttpContext.Session.Remove("CurrentAWXJobStartTime");
                HttpContext.Session.Remove("CurrentAWXTempInventoryId");

                return new JsonResult(new { success = true, message = "Job finalizado, auditoria salva e inventario temporario deletado." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnGetFinalizeAWXJobAsync: Erro ao finalizar Job {JobId}.", jobId);
                return new JsonResult(new { success = false, message = $"ERRO: {ex.Message}" });
            }
        }
    }
}
