<#
.SYNOPSIS
 Obtém a lista de nomes de pacotes do PDQ Deploy usando o comando 'GetPackageNames'.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$PDQExePath
)

Write-Verbose "DEBUG: Iniciando get_packages.ps1"
Write-Verbose "DEBUG: Usando PDQ Exe Path: $PDQExePath"

if (-not (Test-Path $PDQExePath -PathType Leaf)) {
    Write-Error "ERRO CRÍTICO: PDQDeploy.exe não encontrado em '$PDQExePath'"
    exit 1
}

try {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $PDQExePath
    $startInfo.Arguments = "GetPackageNames"
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $process.Start() | Out-Null

    $stdOutOutput = $process.StandardOutput.ReadToEnd()
    $stdErrOutput = $process.StandardError.ReadToEnd()

    $process.WaitForExit()
    $exitCode = $process.ExitCode

    if ($exitCode -ne 0) {
        Write-Error "ERRO: O comando '$PDQExePath GetPackageNames' falhou com código de saída: $exitCode"
        if (-not [string]::IsNullOrWhiteSpace($stdErrOutput)) {
            Write-Error "STDERR do PDQDeploy: $($stdErrOutput.Trim())"
        }
        exit $exitCode
    }

    if (-not [string]::IsNullOrWhiteSpace($stdOutOutput)) {
        $outputString = ($stdOutOutput.Split("`n", [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() }) -join "`n"
        Write-Output $outputString
        exit 0
    } else {
        Write-Verbose "AVISO: Nenhum pacote encontrado."
        exit 0
    }
}
catch {
    Write-Error "ERRO CATASTRÓFICO: $($_.Exception.Message)" 
    exit 1
}