// No arquivo IPowerShellService.cs
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GtopPdqNet.Interfaces // Ou o namespace da sua interface
{
    public interface IPowerShellService
    {
        Task<List<string>> GetPdqPackagesAsync();
        Task<(bool success, string output)> ExecutePdqDeployAsync(string hostname, string packageName);
        Task<List<string>> RefreshPdqPackagesCacheAsync(); // <<< CERTIFIQUE-SE QUE O RETORNO É Task<List<string>>
    }
}
