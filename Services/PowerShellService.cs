// Arquivo: Services/PowerShellService.cs
using GtopPdqNet.Interfaces; // Para usar a interface
using Microsoft.AspNetCore.Hosting; // Para IWebHostEnvironment
using Microsoft.Extensions.Configuration; // Para IConfiguration
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Para Collection<PSObject>
using System.IO;                   // Para Path, File
using System.Management.Automation; // Para PowerShell
using System.Management.Automation.Runspaces; // Para Runspace, InitialSessionState
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics; // Para Process e ProcessStartInfo
using Microsoft.Extensions.Caching.Memory; // <<< ADICIONAR ESTE USING PARA IMEMORYCACHE

namespace GtopPdqNet.Services // Namespace correto
{
    public class PowerShellService : IPowerShellService
    {
        private readonly ILogger<PowerShellService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _contentRootPath;
        private readonly string? _pdqDeployExePath;
        private readonly string? _getPackagesScriptFullPath;
        private readonly string? _deployScriptFullPath;
        private readonly IMemoryCache _cache; // <<< ADICIONAR CAMPO PARA O CACHE
        private const string CacheKeyPdqPackages = "PdqPackagesList"; // <<< CHAVE PARA O CACHE

        // Modificar o construtor para receber IMemoryCache
        public PowerShellService(ILogger<PowerShellService> logger, 
                                 IConfiguration configuration, 
                                 IWebHostEnvironment environment,
                                 IMemoryCache memoryCache) // <<< ADICIONAR IMEMORYCACHE AQUI
        {
            _logger = logger;
            _configuration = configuration;
            _contentRootPath = environment.ContentRootPath;
            _cache = memoryCache; // <<< ATRIBUIR O CACHE

            _logger.LogInformation("Content Root Path: {ContentRootPath}", _contentRootPath);

            _pdqDeployExePath = GetAndValidatePath("PdqSettings:DeployExePath", false);
            _getPackagesScriptFullPath = GetAndValidatePath("PdqSettings:GetPackagesScriptPath", true);
            _deployScriptFullPath = GetAndValidatePath("PdqSettings:DeployScriptPath", true);
        }

        private string? GetAndValidatePath(string configKey, bool isRelative)
        {
            string? configuredPath = _configuration[configKey];
            if (string.IsNullOrEmpty(configuredPath)) {
                _logger.LogError("Configuração não encontrada: {ConfigKey}", configKey);
                return null;
            }
            string fullPath = isRelative ? Path.Combine(_contentRootPath, configuredPath) : configuredPath;
            if (!File.Exists(fullPath)) {
                _logger.LogError("Arquivo não encontrado para {ConfigKey}: {FullPath}", configKey, fullPath);
                return null;
            }
             _logger.LogInformation("Caminho validado para {ConfigKey}: {FullPath}", configKey, fullPath);
             return fullPath;
         }

        // --- Chamar o script get_packages.ps1 com CACHE ---
        public async Task<List<string>> GetPdqPackagesAsync()
        {
            _logger.LogInformation("Tentando buscar pacotes PDQ...");

            // Tenta pegar do cache primeiro
            if (_cache.TryGetValue(CacheKeyPdqPackages, out List<string>? cachedPackages) && cachedPackages != null)
            {
                _logger.LogInformation("Pacotes PDQ encontrados no cache.");
                return cachedPackages;
            }

            _logger.LogInformation("Cache de pacotes PDQ não encontrado ou inválido. Buscando via script PowerShell...");
            var packages = new List<string>();

            if (string.IsNullOrEmpty(_getPackagesScriptFullPath) || string.IsNullOrEmpty(_pdqDeployExePath)) {
                 _logger.LogError("Não é possível buscar pacotes: Caminho inválido/não configurado para Script ou PDQDeploy.exe.");
                return packages; // Retorna lista vazia
            }

             try {
                 using (PowerShell ps = PowerShell.Create(InitialSessionState.CreateDefault()))
                 {
                     _logger.LogInformation("Executando script: {ScriptPath}", _getPackagesScriptFullPath);
                     ps.AddCommand(_getPackagesScriptFullPath)
                         .AddParameter("PDQExePath", _pdqDeployExePath);

                     ps.Streams.Verbose.DataAdded += (sender, e) => _logger.LogDebug("PS Verbose: {Message}", ps.Streams.Verbose[e.Index].ToString());
                     ps.Streams.Warning.DataAdded += (sender, e) => _logger.LogWarning("PS Warning: {Message}", ps.Streams.Warning[e.Index].ToString());
                     ps.Streams.Error.DataAdded += (sender, e) => _logger.LogError("PS Error: {ErrorRecord}", ps.Streams.Error[e.Index].ToString());

                     _logger.LogInformation("Invocando script PowerShell de forma assíncrona...");
                     Collection<PSObject> results = await Task.Run(() => ps.Invoke());
                     _logger.LogInformation("Execução do script PowerShell (GetPackages) concluída.");

                     if (ps.HadErrors && ps.Streams.Error.Count > 0) {
                         _logger.LogError("O script PowerShell GetPackages relatou erros graves (ver logs PS Error acima).");
                     }
                     else {
                        StringBuilder rawOutput = new StringBuilder();
                        foreach (PSObject result in results) {
                            rawOutput.AppendLine(result?.ToString() ?? string.Empty);
                        }
                        string fullOutputString = rawOutput.ToString();
                        _logger.LogDebug("--- Saída Bruta Combinada do Script (GetPackages) ---:\n{RawOutput}", fullOutputString.Trim());

                        string[] lines = fullOutputString.Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                         _logger.LogInformation("Saída bruta dividida em {LineCount} linhas. Adicionando à lista...", lines.Length);

                         foreach (string line in lines) {
                             string packageName = line.Trim();
                             if (!string.IsNullOrEmpty(packageName)) {
                                 packages.Add(packageName);
                             }
                         }
                         _logger.LogInformation("Total final de {Count} pacotes adicionados após processamento no C#.", packages.Count);

                        // Adiciona ao cache
                        if (packages.Count > 0)
                        {
                            var cacheEntryOptions = new MemoryCacheEntryOptions()
                                .SetSlidingExpiration(TimeSpan.FromHours(1)) // Ex: Expira se não acessado por 1 hora
                                .SetAbsoluteExpiration(TimeSpan.FromHours(6)); // Ex: Expira absolutamente após 6 horas
                            _cache.Set(CacheKeyPdqPackages, packages, cacheEntryOptions);
                            _logger.LogInformation("Pacotes PDQ adicionados ao cache.");
                        }
                     }
                 }
             }
             catch (Exception ex) {
                 _logger.LogError(ex, "Exceção do C# ao invocar o script PowerShell GetPackages.");
             }
             return packages;
         }

        // --- NOVO MÉTODO PARA ATUALIZAR O CACHE ---
        public async Task<List<string>> RefreshPdqPackagesCacheAsync()
        {
            _logger.LogInformation("Removendo pacotes PDQ do cache para atualização...");
            _cache.Remove(CacheKeyPdqPackages);
            _logger.LogInformation("Cache de pacotes PDQ removido. Buscando nova lista...");
            // Chama o método original que agora vai buscar e popular o cache novamente
            return await GetPdqPackagesAsync(); 
        }

        // --- Chamar o script deploy.ps1 (sem alterações de cache aqui, a menos que necessário) ---
        public async Task<(bool success, string output)> ExecutePdqDeployAsync(string hostname, string packageName)
         {
            // ... (código original do ExecutePdqDeployAsync permanece o mesmo)
            _logger.LogInformation("Tentando executar deploy via script PS para Host: {Hostname}, Pacote: {PackageName}", hostname, packageName);

             if (string.IsNullOrEmpty(_deployScriptFullPath) || string.IsNullOrEmpty(_pdqDeployExePath)) {
                 _logger.LogError("Não é possível executar deploy: Caminho inválido/não configurado para Script ou PDQDeploy.exe.");
                 return (false, "Erro interno: Configuração inválida.");
             }

             try {
                   using (PowerShell ps = PowerShell.Create(InitialSessionState.CreateDefault())) {
                       _logger.LogInformation("Executando script: {ScriptPath}", _deployScriptFullPath);
                       ps.AddCommand(_deployScriptFullPath)
                           .AddParameter("PDQExePath", _pdqDeployExePath)
                           .AddParameter("hostname", hostname)
                           .AddParameter("package", packageName);

                        ps.Streams.Verbose.DataAdded += (sender, e) => _logger.LogDebug("Deploy PS Verbose: {Message}", ps.Streams.Verbose[e.Index].ToString());
                        ps.Streams.Warning.DataAdded += (sender, e) => _logger.LogWarning("Deploy PS Warning: {Message}", ps.Streams.Warning[e.Index].ToString());
                        ps.Streams.Error.DataAdded += (sender, e) => _logger.LogError("Deploy PS Error: {ErrorRecord}", ps.Streams.Error[e.Index].ToString());

                       _logger.LogInformation("Invocando script de Deploy PowerShell...");
                       Collection<PSObject> results = await Task.Run(() => ps.Invoke());
                       _logger.LogInformation("Execução do script de Deploy concluída.");

                       StringBuilder combinedOutput = new StringBuilder();
                       foreach(var result in results) {
                           combinedOutput.AppendLine(result?.ToString() ?? string.Empty);
                       }
                       string finalOutput = combinedOutput.ToString().Trim();

                        if (ps.HadErrors && ps.Streams.Error.Count > 0) {
                             _logger.LogError("O script PowerShell Deploy relatou erros graves.");
                            return (false, $"Falha crítica no script PowerShell.\n\nLog Completo:\n{finalOutput}");
                        } else {
                             _logger.LogInformation("Script PowerShell de deploy executado. Saída:\n{Output}", finalOutput);
                             return (true, finalOutput);
                        }
                   }
              }
              catch (Exception ex) {
                   _logger.LogError(ex, "Exceção do C# ao invocar o script PowerShell Deploy para {Hostname}/{PackageName}", hostname, packageName);
                   return (false, $"Erro interno do C#: {ex.Message}");
              }
          }

    } // Fim da classe PowerShellService
} // Fim do namespace GtopPdqNet.Services
