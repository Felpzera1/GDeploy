using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging; // Para ILogger<>
using System.Threading.Tasks;      // Para Task<>
using System.Collections.Generic;  // Para List<>
using Microsoft.AspNetCore.Hosting; // Para IWebHostEnvironmens

[Authorize] // Garante que so usuarios logados acessem
public class ResultadoModel : PageModel
{
    // Propriedades para exibir os dados recebidos via TempData
    public bool DeploySuccess { get; private set; } = false;
    public string? LogCompleto { get; private set; }
    public string StatusTitulo { get; private set; } = "Resultado Indefinido";
    public string MensagemResumo { get; private set; } = "Nenhuma informação de deploy recebida.";

    private readonly ILogger<ResultadoModel> _logger;

    public ResultadoModel(ILogger<ResultadoModel> logger)
    {
        _logger = logger;
    }

    // OnGet e chamado quando a pagina e carregada (apos o redirect do Index)
    public IActionResult OnGet()
    {
         _logger.LogInformation("Carregando pagina de Resultado.");

        // Tenta recuperar os dados do TempData
        if (TempData.TryGetValue("DeploySuccess", out var successObj) && successObj is bool success)
        {
            DeploySuccess = success;

            // Recupera as outras informacoes
            string? output = TempData["DeployOutput"] as string;
            string? hostname = TempData["DeployedHostname"] as string;
            string? packageName = TempData["DeployedPackage"] as string;

             LogCompleto = output ?? "Nenhum log detalhado disponivel.";

            if (DeploySuccess)
            {
                StatusTitulo = "Comando Executado";
                MensagemResumo = $"O script para implantar '{packageName ?? "N/A"}' em '{hostname ?? "N/A"}' foi executado. Verifique o log abaixo e o console PDQ.";
                 _logger.LogInformation("Exibindo resultado de deploy bem-sucedido para {Hostname}", hostname);
            }
            else
            {
                StatusTitulo = "Erro na Execucao";
                MensagemResumo = $"Falha ao executar o script para '{packageName ?? "N/A"}' em '{hostname ?? "N/A"}'. Verifique os detalhes no log.";
                _logger.LogWarning("Exibindo resultado de deploy com falha para {Hostname}", hostname);
            }
        }
        else
        {
            // Caso os dados nao estejam no TempData (acesso direto a URL, por exemplo)
             _logger.LogWarning("TempData['DeploySuccess'] nao encontrado ou tipo invalido na pagina Resultado.");
            // Define mensagens padrao de erro/aviso
            DeploySuccess = false;
            StatusTitulo = "Informacao Indisponivel";
            MensagemResumo = "Nao foi possivel carregar os detalhes do ultimo deploy.";
            LogCompleto = null; // Ou uma mensagem indicando que nao ha log
            // Pode ser util redirecionar para o Index se nao houver dados
            // return RedirectToPage("/Index");
        }

        // Mantem os dados no TempData por mais uma requisicao caso o usuario atualize a pagina
        // Remova se nao quiser esse comportamento
        // TempData.Keep(); 

        return Page(); // Renderiza a pagina Resultado.cshtml...
    }
}