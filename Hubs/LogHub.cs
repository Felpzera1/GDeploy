using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace GtopPdqNet.Hubs
{
    public class LogHub : Hub
    {
        public async Task SendLog(string computer, string step, string content)
        {
            await Clients.All.SendAsync("ReceiveLog", computer, step, content);
        }
    }
}
