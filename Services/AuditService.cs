using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace GtopPdqNet.Services
{
    public class AuditService
    {
        private readonly ILogger<AuditService> _logger;
        private readonly string _auditLogPath;
        private readonly string _detailedLogPath;

        public AuditService(ILogger<AuditService> logger, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            _auditLogPath = Path.Combine(webHostEnvironment.ContentRootPath, "audit_logs");
            _detailedLogPath = Path.Combine(_auditLogPath, "detailed");
            
            // Criar diretórios se não existirem
            Directory.CreateDirectory(_auditLogPath);
            Directory.CreateDirectory(_detailedLogPath);
        }

        public async Task LogDeployAsync(string username, string hostname, string packageName, bool success, string output)
        {
            try
            {
                var logEntry = new
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Username = username,
                    Hostname = hostname,
                    PackageName = packageName,
                    Success = success,
                    Output = output
                };

                // Salvar log de auditoria principal
                await SaveAuditLogAsync(logEntry);

                // Salvar log detalhado
                await SaveDetailedLogAsync(logEntry, output);

                _logger.LogInformation("Log de auditoria salvo: {Username} executou deploy de {PackageName} em {Hostname} - Sucesso: {Success}", 
                    username, packageName, hostname, success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar log de auditoria para deploy de {Username}", username);
            }
        }

        private async Task SaveAuditLogAsync(object logEntry)
        {
            try
            {
                var today = DateTime.Now.ToString("yyyyMM-dd");
                var fileName = $"deploy_audit_{today}.json";
                var filePath = Path.Combine(_auditLogPath, fileName);

                List<object> logs;

                if (File.Exists(filePath))
                {
                    var existingContent = await File.ReadAllTextAsync(filePath);
                    if (!string.IsNullOrWhiteSpace(existingContent))
                    {
                        try
                        {
                            logs = JsonSerializer.Deserialize<List<object>>(existingContent) ?? new List<object>();
                        }
                        catch (JsonException)
                        {
                            try
                            {
                                var singleLog = JsonSerializer.Deserialize<object>(existingContent);
                                logs = new List<object> { singleLog };
                            }
                            catch (JsonException)
                            {
                                logs = new List<object>();
                            }
                        }
                    }
                    else
                    {
                        logs = new List<object>();
                    }
                }
                else
                {
                    logs = new List<object>();
                }

                logs.Add(logEntry);

                var jsonContent = JsonSerializer.Serialize(logs, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar log de auditoria principal");
            }
        }

        private async Task SaveDetailedLogAsync(object logEntry, string detailedOutput)
        {
            try
            {
                var timestamp = DateTime.Now;
                var timestampStr = timestamp.ToString("yyyyMMdd_HHmmss");
                
                var logJson = JsonSerializer.Serialize(logEntry);
                var logData = JsonSerializer.Deserialize<JsonElement>(logJson);
                
                var hostname = logData.GetProperty("Hostname").GetString();
                var username = logData.GetProperty("Username").GetString()?.Replace("@", "_").Replace(".", "_");
                
                var fileName = $"deploy_detailed_{timestampStr}_{hostname}_{username}.json";
                var filePath = Path.Combine(_detailedLogPath, fileName);

                var detailedLog = new
                {
                    Timestamp = logData.GetProperty("Timestamp").GetString(),
                    Username = logData.GetProperty("Username").GetString(),
                    Hostname = logData.GetProperty("Hostname").GetString(),
                    PackageName = logData.GetProperty("PackageName").GetString(),
                    Success = logData.GetProperty("Success").GetBoolean(),
                    Output = logData.GetProperty("Output").GetString(),
                    DetailedOutput = detailedOutput,
                    LogFile = fileName,
                    CreatedAt = timestamp.ToString("yyyy-MM-dd HH:mm:ss")
                };

                var jsonContent = JsonSerializer.Serialize(detailedLog, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar log detalhado");
            }
        }

        public async Task<string> GetAuditLogsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var allLogs = new List<object>();
                var pdqFiles = Directory.GetFiles(_auditLogPath, "deploy_audit_*.json");

                foreach (var file in pdqFiles)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var logs = JsonSerializer.Deserialize<List<object>>(content);
                            if (logs != null)
                            {
                                allLogs.AddRange(logs);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Erro ao processar arquivo de log: {FileName}", file);
                    }
                }

                allLogs = allLogs.OrderByDescending(log => 
                {
                    if (log is JsonElement element && element.TryGetProperty("Timestamp", out var timestampProp))
                    {
                        if (DateTime.TryParse(timestampProp.GetString(), out var timestamp))
                        {
                            return timestamp;
                        }
                    }
                    return DateTime.MinValue;
                }).ToList();

                return JsonSerializer.Serialize(allLogs, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao recuperar logs de auditoria");
                return "[]";
            }
        }

        public async Task<string?> GetDetailedLogAsync(string timestamp, string hostname, string username)
        {
            try
            {
                var searchPattern = $"*_{hostname}_{username.Replace("@", "_").Replace(".", "_")}.json";
                var files = Directory.GetFiles(_detailedLogPath, searchPattern);

                foreach (var file in files)
                {
                    var content = await File.ReadAllTextAsync(file);
                    var log = JsonSerializer.Deserialize<JsonElement>(content);
                    
                    if (log.TryGetProperty("Timestamp", out var logTimestamp) && 
                        logTimestamp.GetString() == timestamp)
                    {
                        return content;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao recuperar log detalhado");
                return null;
            }
        }
    }
}
