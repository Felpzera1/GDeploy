using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using GtopPdqNet.Services;

namespace GtopPdqNet.Pages
{
    [Authorize]
    public class AuditLogsModel : PageModel
    {
        private readonly ILogger<AuditLogsModel> _logger;
        private readonly AuditService _auditService;

        public string? AuditLogs { get; set; }

        public AuditLogsModel(ILogger<AuditLogsModel> logger, AuditService auditService)
        {
            _logger = logger;
            _auditService = auditService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("AuditLogsModel.OnGetAsync: Carregando logs de auditoria...");
            
            try
            {
                AuditLogs = await _auditService.GetAuditLogsAsync();
                _logger.LogInformation("AuditLogsModel.OnGetAsync: Logs de auditoria carregados com sucesso.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AuditLogsModel.OnGetAsync: Erro ao carregar logs de auditoria.");
                AuditLogs = "[]";
                ViewData["ErrorMessage"] = "Erro ao carregar logs de auditoria.";
            }

            return Page();
        }

        public async Task<IActionResult> OnGetDetailedLogAsync(string timestamp, string hostname, string username)
        {
            _logger.LogInformation("AuditLogsModel.OnGetDetailedLogAsync: Carregando log detalhado para {Username} em {Hostname} às {Timestamp}", username, hostname, timestamp);
            
            try
            {
                var detailedLog = await _auditService.GetDetailedLogAsync(timestamp, hostname, username);
                if (detailedLog != null)
                {
                    return new ContentResult
                    {
                        Content = detailedLog,
                        ContentType = "application/json"
                    };
                }
                else
                {
                    return NotFound("Log detalhado não encontrado.");
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AuditLogsModel.OnGetDetailedLogAsync: Erro ao carregar log detalhado.");
                return BadRequest("Erro ao carregar log detalhado.");
            }
        }
    }
}

