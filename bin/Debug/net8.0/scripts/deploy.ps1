<#
.SYNOPSIS
 Inicia um deploy no PDQ Deploy usando a CLI.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$PDQExePath,

    [Parameter(Mandatory=$true)]
    [string]$hostname,

    [Parameter(Mandatory=$true)]
    [string]$package
)

if (-not (Test-Path $PDQExePath -PathType Leaf)) {
    Write-Error "ERRO CRÍTICO: PDQDeploy.exe não encontrado em '$PDQExePath'"
    exit 1
}

if (-not $hostname) {
    Write-Error "ERRO CRÍTICO: Hostname não pode ser vazio."
    exit 1
}

if (-not $package) {
    Write-Error "ERRO CRÍTICO: Nome do pacote não pode ser vazio."
    exit 1
}

try {
    $isHostReachable = Test-Connection -ComputerName $hostname -Count 1 -Quiet -ErrorAction SilentlyContinue

    if (-not $isHostReachable) {
        Write-Error "ERRO: Host '$hostname' não foi encontrado ou não respondeu."
        exit 1
    }

    $startProcessArgs = @{
        FilePath = $PDQExePath
        ArgumentList = @("Deploy", "-Package", "`"$package`"", "-Targets", $hostname)
        NoNewWindow = $true
        PassThru = $true
        RedirectStandardOutput = "$PSScriptRoot\deploy_stdout.log"
        RedirectStandardError = "$PSScriptRoot\deploy_stderr.log"
    }

    $process = Start-Process @startProcessArgs -Wait

    $stdOutContent = Get-Content "$PSScriptRoot\deploy_stdout.log" -Raw
    $stdErrContent = Get-Content "$PSScriptRoot\deploy_stderr.log" -Raw

    if ($process.ExitCode -ne 0) {
        Write-Error "ERRO PDQ: Código de saída $($process.ExitCode). STDERR: $stdErrContent"
        exit 1
    } else {
        Write-Host "SUCESSO: Deploy iniciado com sucesso para '$hostname'!"
        exit 0
    }
}
catch {
    Write-Error "ERRO CATASTRÓFICO: $($_.Exception.Message)"
    exit 1
}