using System.Collections.Generic;
using System.Threading.Tasks;

namespace GtopPdqNet.Interfaces
{
    public interface IAWXService
    {
        // Método para obter a lista de Job Templates disponíveis no AWX
        Task<List<string>> GetJobTemplatesAsync();
        
        // Método para disparar um Job Template no AWX
        // Retorna o ID do Job disparado para monitoramento
        Task<int> LaunchJobTemplateAsync(string hostname, string templateName);
        
        // Método para monitorar o status de um Job
        // Retorna o status (e.g., 'pending', 'running', 'successful', 'failed') e o output do log
        Task<(string status, string output)> GetJobStatusAndOutputAsync(int jobId);
    }
}
