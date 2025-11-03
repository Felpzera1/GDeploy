using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using GtopPdqNet.Hubs;
using System.Linq;

namespace GtopPdqNet.Services
{
    public class LiveLogService
    {
        private readonly IHubContext<LogHub> _hubContext;

        public LiveLogService(IHubContext<LogHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public void StartMonitoring(string path)
        {
            var watcher = new FileSystemWatcher(path, "*.gz");
            watcher.Created += async (s, e) =>
            {
                await Task.Delay(1500);
                var log = ReadGzContent(e.FullPath);
                var fileName = Path.GetFileNameWithoutExtension(e.Name);
                var step = fileName.Split('.').ElementAtOrDefault(1) ?? "?";
                await _hubContext.Clients.All.SendAsync("ReceiveLog", fileName, step, log);
            };
            watcher.EnableRaisingEvents = true;
        }

        private string ReadGzContent(string path)
        {
            using var fs = File.OpenRead(path);
            using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return reader.ReadToEnd();
        }
    }
}
