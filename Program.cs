using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using GtopPdqNet.Hubs;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using GtopPdqNet.Services;
using GtopPdqNet.Interfaces; // Adicionado para IAWXService
using System.Net.Http; // Adicionado para AddHttpClient
using System.Net.Sockets; 
using System.Text; 
using Microsoft.AspNetCore.Builder;
using System.DirectoryServices.Protocols; 
using System.Security.Claims; 
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http; 

public class LdapSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } 
    public string SearchBase { get; set; } = string.Empty; 
    public string SearchFilter { get; set; } = string.Empty; 
    public string AllowedGroupCn { get; set; } = string.Empty; 
}


// --- AuthService --- 
public class AuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly LdapSettings _ldapSettings;

    public AuthService(ILogger<AuthService> logger, IOptions<LdapSettings> ldapSettings)
    {
        _logger = logger;
        _ldapSettings = ldapSettings.Value ?? throw new ArgumentNullException(nameof(ldapSettings), "Configurações LDAP (LdapSettings) não podem ser nulas.");
        if (string.IsNullOrWhiteSpace(_ldapSettings.Server) || 
            string.IsNullOrWhiteSpace(_ldapSettings.SearchBase) || 
            string.IsNullOrWhiteSpace(_ldapSettings.SearchFilter))
        {
            _logger.LogError("Configurações LDAP essenciais (Server, SearchBase, SearchFilter) estão faltando.");
            throw new InvalidOperationException("Configurações LDAP essenciais (Server, SearchBase, SearchFilter) não foram fornecidas no appsettings.json.");
        }
    }

    public async Task<(bool IsAuthenticated, string? UserPrincipalName, List<string>? UserGroups, string? ErrorMessage)> AuthenticateUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Tentativa de autenticação com usuário ou senha em branco.");
            return (false, null, null, "Usuário ou senha não podem estar em branco.");
        }
        return await Task.Run(() => AuthenticateAndGetGroups(username, password));
    }

    private (bool IsAuthenticated, string? UserPrincipalName, List<string>? UserGroups, string? ErrorMessage) AuthenticateAndGetGroups(string username, string password)
    {
        _logger.LogInformation($"--- Iniciando Autenticação e Busca de Grupos S.DS.P ---");
        _logger.LogInformation($"Servidor: {_ldapSettings.Server}:{_ldapSettings.Port}, UseSsl: {_ldapSettings.UseSsl}, SearchBase: {_ldapSettings.SearchBase}");

        LdapConnection? connection = null;
        try
        {
            var ldapIdentifier = new LdapDirectoryIdentifier(_ldapSettings.Server, _ldapSettings.Port);
            connection = new LdapConnection(ldapIdentifier);
            connection.SessionOptions.ProtocolVersion = 3;

            if (_ldapSettings.UseSsl)
            {
                _logger.LogInformation("Tentando conexão LDAPS (SecureSocketLayer = true).");
                connection.SessionOptions.SecureSocketLayer = true;
            }

            var credential = new NetworkCredential(username, password);
            _logger.LogInformation($"Tentando Bind com usuário: {username}");
            connection.Bind(credential);
            _logger.LogInformation("Autenticação (Bind) bem-sucedida para: {username}", username);

            
            string userIdentifierForFilter;
            if (_ldapSettings.SearchFilter.ToLower().Contains("samaccountname") && username.Contains("@"))
            {
                userIdentifierForFilter = username.Split("@")[0];
                _logger.LogInformation($"SearchFilter parece ser para sAMAccountName, usando 	userIdentifierForFilter do username 	username.");
            }
            else
            {
                userIdentifierForFilter = username; 
                _logger.LogInformation($"Usando identificador completo 	userIdentifierForFilter para o filtro LDAP.");
            }
            string userFilter = string.Format(_ldapSettings.SearchFilter, userIdentifierForFilter);
            _logger.LogInformation($"Buscando usuário com filtro: {userFilter} em SearchBase: {_ldapSettings.SearchBase}");

            var searchRequest = new SearchRequest(
                _ldapSettings.SearchBase, 
                userFilter,             
                SearchScope.Subtree,    
                new string[] { "memberOf", "distinguishedName", "userPrincipalName" } 
            );

            SearchResponse? searchResponse = connection.SendRequest(searchRequest) as SearchResponse;
            if (searchResponse == null || searchResponse.Entries.Count == 0)
            {
                _logger.LogWarning($"Usuário {username} autenticado, mas não encontrado na busca LDAP com filtro {userFilter} para obter grupos.");
                return (true, username, new List<string>(), "Usuário autenticado, mas detalhes não puderam ser recuperados do diretório.");
            }

            SearchResultEntry userEntry = searchResponse.Entries[0];
            string? userPrincipalNameFromSearch = username; 
            if (userEntry.Attributes.Contains("userPrincipalName"))
            {
                userPrincipalNameFromSearch = userEntry.Attributes["userPrincipalName"][0] as string ?? username;
            }

            
            var groups = new List<string>();
            if (userEntry.Attributes.Contains("memberOf"))
            {
                _logger.LogInformation($"Atributo 'memberOf' encontrado. Processando {userEntry.Attributes["memberOf"].Count} entradas.");
                foreach (object value in userEntry.Attributes["memberOf"]) 
                {
                    string groupDn;
                    if (value is byte[] bytesValue)
                    {
                        groupDn = System.Text.Encoding.UTF8.GetString(bytesValue);
                        _logger.LogInformation($"Valor 'memberOf' era byte[], convertido para string: {groupDn}");
                    }
                    else if (value is string stringValue)
                    {
                        groupDn = stringValue;
                    }
                    else
                    {
                        _logger.LogWarning($"Valor 'memberOf' não é string nem byte[]: {value?.GetType().FullName}. Ignorando.");
                        continue;
                    }

                    try
                    {
                        string[] dnParts = groupDn.Split(',');
                        if (dnParts.Length > 0)
                        {
                            string[] cnPart = dnParts[0].Split('=');
                            if (cnPart.Length > 1)
                            {
                                string cn = cnPart[1];
                                groups.Add(cn);
                                _logger.LogInformation($"Usuário {username} é membro do grupo (CN): {cn} (DN original: {groupDn})");
                            }
                            else
                            {
                                _logger.LogWarning($"Não foi possível extrair o CN do primeiro componente de: {groupDn} (não contém '=')");
                            }
                        }
                        else
                        {
                             _logger.LogWarning($"DN do grupo está vazio ou inválido: {groupDn}");
                        }
                    }
                    catch (Exception exCn)
                    {
                        _logger.LogWarning(exCn, $"Não foi possível processar/extrair o CN do DN do grupo: {groupDn}");
                    }
                }
            }
            else
            {
                _logger.LogInformation($"Usuário {username} não possui o atributo 'memberOf' ou não é membro de nenhum grupo listado.");
            }
        

            return (true, userPrincipalNameFromSearch, groups, null);
        }
        catch (LdapException ldapEx)
        {
            string detailedErrorMessage = ldapEx.ServerErrorMessage ?? ldapEx.Message;
            _logger.LogError(ldapEx, $"Erro LDAP durante autenticação/busca de grupos para {username}: {detailedErrorMessage}", username); 
            return (false, null, null, $"Erro de autenticação ou comunicação com o servidor LDAP. Detalhe: {detailedErrorMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erro Geral durante autenticação/busca de grupos para {username}: {ex.Message}", username); 
            return (false, null, null, $"Ocorreu um erro inesperado durante a autenticação. Detalhe: {ex.Message}");
        }
        finally
        {
            connection?.Dispose(); 
            _logger.LogInformation("--- FINALIZANDO Autenticação e Busca de Grupos S.DS.P ---");
        }
    }
}


public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);



        builder.Services.AddRazorPages(
            options =>
            {
                options.Conventions.AuthorizeFolder("/"); 
                options.Conventions.AllowAnonymousToPage("/Login"); 
                options.Conventions.AllowAnonymousToPage("/Error"); 
                options.Conventions.AllowAnonymousToPage("/AccessDenied"); 
            }
        );

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Login"; 
                options.LogoutPath = "/Logout"; 
                options.AccessDeniedPath = "/AccessDenied"; 
                options.ExpireTimeSpan = TimeSpan.FromMinutes(60); 
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always; 
            });
        
        builder.Services.AddAuthorization();
        builder.Services.AddRazorPages();
        builder.Services.AddSingleton<LiveLogService>();
        builder.Services.AddSignalR();
        builder.Services.Configure<LdapSettings>(builder.Configuration.GetSection("LdapSettings"));
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<GtopPdqNet.Interfaces.IPowerShellService, GtopPdqNet.Services.PowerShellService>();
        builder.Services.AddHttpClient<IAWXService, GtopPdqNet.Services.AWXService>(); // ADICIONADO: Registro do IAWXService com HttpClient
        builder.Services.AddScoped<GtopPdqNet.Services.AuditService>();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo("C:\\projetopdq\\keys")) 
            .SetApplicationName("PdqWebApp");

        builder.Services.AddMemoryCache();

        var app = builder.Build();
        var logService = app.Services.GetRequiredService<LiveLogService>();
        logService.StartMonitoring("C:\\ProgramData\\Admin Arsenal\\PDQ Deploy\\Deployment Output");

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }
        else
        {
            app.UseDeveloperExceptionPage();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        
        app.UseAuthentication(); 
        app.UseAuthorization();  
        app.MapHub<LogHub>("/loghub");
        app.MapRazorPages();

        app.Run();
    }
}
