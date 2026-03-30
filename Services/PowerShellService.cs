// Arquivo: Services/PowerShellService.cs
using GtopPdqNet.Interfaces; 
using Microsoft.AspNetCore.Hosting; 
using Microsoft.Extensions.Configuration; 
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; 
using System.IO;                   
using System.Management.Automation; 
using System.Management.Automation.Runspaces; 
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory; 

namespace GtopPdqNet.Services 
{
    public class PowerShellService : IPowerShellService
    {
        private readonly ILogger<PowerShellService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _contentRootPath;
        private readonly string? _pdqDeployExePath;
        private readonly string? _getPackagesScriptFullPath;
        private readonly string? _deployScriptFullPath;
        private readonly IMemoryCache _cache; 
        private const string CacheKeyPdqPackages = "PdqPackagesList"; 

        public PowerShellService(ILogger<PowerShellService> logger, 
                                 IConfiguration configuration, 
                                 IWebHostEnvironment environment,
                                 IMemoryCache memoryCache) 
        {
            _logger = logger;
            _configuration = configuration;
            _contentRootPath = environment.ContentRootPath;
            _cache = memoryCache; 

            _pdqDeployExePath = GetAndValidatePath("PdqSettings:DeployExePath", false);
            _getPackagesScriptFullPath = GetAndValidatePath("PdqSettings:GetPackagesScriptPath", true);
            _deployScriptFullPath = GetAndValidatePath("PdqSettings:DeployScriptPath", true);
        }

        private string? GetAndValidatePath(string configKey, bool isRelative)
        {
            string? configuredPath = _configuration[configKey];
            if (string.IsNullOrEmpty(configuredPath)) return null;
            string fullPath = isRelative ? Path.Combine(_contentRootPath, configuredPath) : configuredPath;
            if (!File.Exists(fullPath)) return null;
             return fullPath;
         }

        public async Task<List<string>> GetPdqPackagesAsync()
        {
            if (_cache.TryGetValue(CacheKeyPdqPackages, out List<string>? cachedPackages) && cachedPackages != null) return cachedPackages;
            
            var packages = new List<string>();
            if (string.IsNullOrEmpty(_getPackagesScriptFullPath) || string.IsNullOrEmpty(_pdqDeployExePath)) return packages;

             try {
                 using (PowerShell ps = PowerShell.Create(InitialSessionState.CreateDefault()))
                 {
                     ps.AddCommand(_getPackagesScriptFullPath).AddParameter("PDQExePath", _pdqDeployExePath);
                     Collection<PSObject> results = await Task.Run(() => ps.Invoke());
                     
                     if (!ps.HadErrors) {
                        StringBuilder rawOutput = new StringBuilder();
                        foreach (PSObject result in results) rawOutput.AppendLine(result?.ToString() ?? string.Empty);
                        string[] lines = rawOutput.ToString().Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines) {
                            if (!string.IsNullOrEmpty(line.Trim())) packages.Add(line.Trim());
                        }
                        if (packages.Count > 0) _cache.Set(CacheKeyPdqPackages, packages, TimeSpan.FromHours(6));
                     }
                 }
             }
             catch (Exception ex) { _logger.LogError(ex, "Erro GetPackages."); }
             return packages;
         }

        public async Task<List<string>> RefreshPdqPackagesCacheAsync()
        {
            _cache.Remove(CacheKeyPdqPackages);
            return await GetPdqPackagesAsync(); 
        }

        public async Task<(bool success, string output)> ExecutePdqDeployAsync(string hostname, string packageName)
         {
             if (string.IsNullOrEmpty(_deployScriptFullPath) || string.IsNullOrEmpty(_pdqDeployExePath)) {
                 return (false, "Erro: Configuração inválida.");
             }

             try {
                   using (PowerShell ps = PowerShell.Create(InitialSessionState.CreateDefault())) {
                       ps.AddCommand(_deployScriptFullPath)
                           .AddParameter("PDQExePath", _pdqDeployExePath)
                           .AddParameter("hostname", hostname)
                           .AddParameter("package", packageName);

                       Collection<PSObject> results = await Task.Run(() => ps.Invoke());
                       
                       StringBuilder combinedOutput = new StringBuilder();
                       foreach(var result in results) combinedOutput.AppendLine(result?.ToString() ?? string.Empty);
                       
                       string finalOutput = combinedOutput.ToString().Trim();
                       bool success = !ps.HadErrors;

                       return (success, finalOutput);
                   }
              }
              catch (Exception ex) {
                   return (false, $"Erro: {ex.Message}");
              }
          }
    }
}
