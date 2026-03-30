<#
.SYNOPSIS
 Inicia e monitora um deploy no PDQ Deploy usando a CLI para capturar steps em tempo real.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$PDQExePath,

    [Parameter(Mandatory=$true)]
    [string]$hostname,

    [Parameter(Mandatory=$true)]
    [string]$package
)

$stdoutPath = "$PSScriptRoot\deploy_stdout.log"
$stderrPath = "$PSScriptRoot\deploy_stderr.log"

# Limpar logs anteriores
if (Test-Path $stdoutPath) { Remove-Item $stdoutPath -Force }
if (Test-Path $stderrPath) { Remove-Item $stderrPath -Force }

function Write-Log($msg) {
    $msg | Out-File -FilePath $stdoutPath -Append -Encoding utf8
    Write-Host $msg
}

try {
    Write-Log "-> Verificando conectividade com '$hostname'..."
    $ping = Test-Connection -ComputerName $hostname -Count 1 -Quiet -ErrorAction SilentlyContinue
    if (-not $ping) {
        throw "Host '$hostname' não respondeu ao ping."
    }
    Write-Log "   SUCESSO: Host online."

    Write-Log "-> Iniciando deploy do pacote '$package'..."
    
    # Inicia o deploy e captura a saída inicial para pegar o ID
    # Usando o caminho completo do executável PDQ
    $initResult = & $PDQExePath Deploy -Package "$package" -Targets $hostname 2>&1
    Write-Log ($initResult -join "`n")

    # Tenta extrair o ID do deploy (Ex: "Deployment Started ID: 1234")
    $deployId = $null
    foreach ($line in $initResult) {
        if ($line -match "ID\s*:\s*(\d+)") {
            $deployId = $matches[1]
            break
        }
    }

    if ($deployId) {
        Write-Log "-> Monitorando Steps (ID: $deployId)..."
        
        $isFinished = $false
        $lastStatus = ""
        
        while (-not $isFinished) {
            Start-Sleep -Seconds 2
            
            # Busca o status detalhado usando o caminho completo do executável PDQ
            $statusResult = & $PDQExePath GetDeployment -ID $deployId 2>&1
            $statusText = $statusResult -join "`n"
            
            # Se o status mudou, atualiza o log
            if ($statusText -ne $lastStatus) {
                # Limpa e escreve o status atual (opcional: ou apenas anexa)
                # Para logs em tempo real, anexar costuma ser melhor para histórico, 
                # mas o GetDeployment traz o status ATUAL completo.
                # Vamos sobrescrever para mostrar sempre o estado mais recente dos steps.
                $statusText | Out-File -FilePath $stdoutPath -Append -Encoding utf8
                $lastStatus = $statusText
            }

            # Verifica se terminou (Status: Finished, Success, Failed, etc.)
            if ($statusText -match "Finished" -or $statusText -match "Success" -or $statusText -match "Failed" -or $statusText -match "Error") {
                $isFinished = $true
            }
        }
    } else {
        Write-Log "AVISO: Não foi possível capturar o ID do deploy para monitoramento em tempo real."
    }

    Write-Host "SUCESSO: Processo de deploy finalizado para '$hostname'."
    exit 0
}
catch {
    $errorMsg = "ERRO: $($_.Exception.Message)"
    $errorMsg | Out-File -FilePath $stderrPath -Append -Encoding utf8
    Write-Error $errorMsg
    exit 1
}
