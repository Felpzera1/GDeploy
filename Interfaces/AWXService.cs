using GtopPdqNet.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System;

namespace GtopPdqNet.Services
{
    public class AWXService : IAWXService
    {
        private readonly ILogger<AWXService> _logger;
        private readonly HttpClient _httpClient;
        
        // ** NOTA IMPORTANTE: O usuário DEVE configurar estas variáveis no seu ambiente **
        // A URL deve ser apenas o endereço base (ex: http://192.168.1.152:31104)
        private const string AWX_BASE_URL = "http://192.168.1.152:31104"; 
        // O Token de Autenticação (Token de Acesso Pessoal)
        private const string AWX_AUTH_TOKEN = "AFFTduffD8WpxaWHCcQUzcxN094J5B"; 

        public AWXService(ILogger<AWXService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
            // Adiciona o sufixo da API do AWX (v2) ao BaseAddress
            _httpClient.BaseAddress = new System.Uri(AWX_BASE_URL + "/api/v2/");
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AWX_AUTH_TOKEN);
        }

        /// <summary>
        /// Obtém o ID de um Job Template pelo nome.
        /// </summary>
        /// <param name="templateName">O nome do Job Template.</param>
        /// <returns>O ID do Job Template ou 0 se não for encontrado.</returns>
        private async Task<int> GetJobTemplateIdByName(string templateName)
        {
            try
            {
                // Usa o filtro 'name' na API do AWX para buscar o template
                var response = await _httpClient.GetAsync($"job_templates/?name={Uri.EscapeDataString(templateName)}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(content);

                var results = jsonDoc.RootElement.GetProperty("results");
                if (results.GetArrayLength() > 0)
                {
                    // Retorna o ID do primeiro resultado
                    return results[0].GetProperty("id").GetInt32();
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar o ID do Job Template '{templateName}' no AWX.");
                return 0;
            }
        }

        public async Task<List<string>> GetJobTemplatesAsync()
        {
            _logger.LogInformation("AWXService: Obtendo lista de Job Templates.");
            
            try
            {
                // Chamada real para obter a lista de Job Templates
                var response = await _httpClient.GetAsync("job_templates/");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(content);
                
                var templates = new List<string>();
                foreach (var result in jsonDoc.RootElement.GetProperty("results").EnumerateArray())
                {
                    // Adiciona o nome do template à lista
                    templates.Add(result.GetProperty("name").GetString());
                }
                return templates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter Job Templates do AWX.");
                // Retorna lista vazia em caso de erro, para não quebrar a aplicação
                return new List<string>();
            }
        }

        public async Task<int> LaunchJobTemplateAsync(string hostname, string templateName)
        {
            _logger.LogInformation("AWXService: Lançando Job Template '{Template}' para o host '{Host}'.", templateName, hostname);
            
            try
            {
                // 1. Obter o ID do Job Template pelo nome
                int templateId = await GetJobTemplateIdByName(templateName);
                if (templateId == 0)
                {
                    throw new Exception($"Job Template '{templateName}' não encontrado ou ID inválido.");
                }
                
                // 2. Criar o payload para o POST
                // O 'extra_vars' é crucial para passar o hostname (ou qualquer outra variável) para o playbook
                var payload = new 
                {
                    extra_vars = new { target_host = hostname }
                };
                
                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // 3. Disparar o Job
                var response = await _httpClient.PostAsync($"job_templates/{templateId}/launch/", content);
                
                // 4. Log detalhado em caso de falha (para diagnóstico)
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("AWX Launch Failed: Status {Status}. Response: {Response}", response.StatusCode, errorContent);
                    response.EnsureSuccessStatusCode(); // Lança a exceção para o catch
                }
                
                // 5. Processar a resposta
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(responseContent);
                
                // O AWX retorna o ID do Job recém-criado na propriedade "job"
                int jobId = jsonDoc.RootElement.GetProperty("job").GetInt32();
                return jobId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao lançar Job Template no AWX.");
                throw; // Re-lança a exceção para ser tratada pelo controlador
            }
        }

        public async Task<(string status, string output)> GetJobStatusAndOutputAsync(int jobId)
        {
            _logger.LogInformation("AWXService: Obtendo status e output do Job ID: {JobId}", jobId);
            
            try
            {
                // 1. Obter o status do Job
                var statusResponse = await _httpClient.GetAsync($"jobs/{jobId}/");
                statusResponse.EnsureSuccessStatusCode();
                var statusContent = await statusResponse.Content.ReadAsStringAsync();
                var statusJsonDoc = JsonDocument.Parse(statusContent);
                var status = statusJsonDoc.RootElement.GetProperty("status").GetString();
                
                // 2. Obter o stdout (log) do Job
                // O formato 'txt_download' é o mais limpo para exibição.
                var logResponse = await _httpClient.GetAsync($"jobs/{jobId}/stdout/?format=txt_download");
                // A API de stdout pode retornar 404 se o job ainda não tiver logs, então não usamos EnsureSuccessStatusCode()
                string logOutput = "";
                if (logResponse.IsSuccessStatusCode)
                {
                    logOutput = await logResponse.Content.ReadAsStringAsync();
                }
                else
                {
                    logOutput = $"Aguardando logs do AWX (Status HTTP: {logResponse.StatusCode})...";
                }
                
                return (status, logOutput);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter status/output do Job AWX {JobId}.", jobId);
                return ("error", $"ERRO Comunicação AWX: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica se um host existe em algum inventário do AWX.
        /// </summary>
        /// <param name="hostname">O nome do host a ser verificado.</param>
        /// <returns>True se o host for encontrado, False caso contrário.</returns>
        public async Task<bool> HostExistsInInventoryAsync(string hostname)
        {
            _logger.LogInformation("AWXService: Verificando existência do host '{Host}' no AWX.", hostname);

            try
            {
                // Endpoint para listar hosts com filtro pelo nome
                // O filtro 'name' é case-insensitive por padrão no AWX
                var response = await _httpClient.GetAsync($"hosts/?name={Uri.EscapeDataString(hostname)}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(content);

                // O AWX retorna um objeto com a propriedade "count" que indica o número de resultados
                int count = jsonDoc.RootElement.GetProperty("count").GetInt32();

                if (count > 0)
                {
                    _logger.LogInformation("AWXService: Host '{Host}' encontrado no AWX (Count: {Count}).", hostname, count);
                    return true;
                }
                else
                {
                    _logger.LogWarning("AWXService: Host '{Host}' NÃO encontrado no AWX.", hostname);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao verificar a existência do host '{Host}' no AWX.", hostname);
                // Em caso de erro de comunicação, por segurança, retornamos false para evitar um deploy indesejado.
                return false;
            }
        }
    }
}
