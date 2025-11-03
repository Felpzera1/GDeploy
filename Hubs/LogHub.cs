using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace GtopPdqNet.Hubs
{
    [Authorize]
    public class LogHub : Hub
    {
        /// <summary>
        /// Envia log apenas para o usuário que iniciou o deploy (separação por usuário).
        /// </summary>
        public async Task SendLog(string computer, string step, string content)
        {
            // Obter o ID da conexão do usuário atual
            var userId = Context.User?.Identity?.Name ?? "Unknown";
            
            // Enviar log apenas para o usuário que iniciou o deploy
            // Usar Clients.Caller para enviar apenas para a conexão atual
            await Clients.Caller.SendAsync("ReceiveLog", computer, step, content);
        }
    }
}
