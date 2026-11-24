# Сборка Pconnect в exe (требуется PowerShell 5.1+ и доступ в интернет для установки ps2exe)
param(
    [string]$Output = "Pconnect.exe"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not (Get-Module -ListAvailable -Name ps2exe)) {
    Write-Host "Устанавливаю модуль ps2exe..."
    Install-Module -Name ps2exe -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
}
Import-Module ps2exe

$params = @{
    InputFile   = Join-Path $root 'Pconnect.ps1'
    OutputFile  = Join-Path $root $Output
    IconFile    = Join-Path $root 'assets\Pconnect.ico'
    RequireAdmin = $true
    NoConsole    = $true
    Title       = 'Pconnect'
    Description = 'Pconnect — универсальный помощник VPN'
    Product     = 'Pconnect'
    Version     = '1.0.0'
}
Invoke-ps2exe @params

Write-Host "Готово: $(Join-Path $root $Output)"
