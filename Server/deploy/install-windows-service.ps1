# Установка сервера облачной базы как службы Windows.
# Запускать в PowerShell от имени администратора из каталога, где лежат raab-server.exe, e_sqlite3.dll и appsettings.json.
#   .\install-windows-service.ps1 -InstallDir C:\raab
param(
    [string]$InstallDir = 'C:\raab'
)

$ErrorActionPreference = 'Stop'
$serviceName = 'RemoteAccessAddressBook'

New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item -Force .\raab-server.exe, .\e_sqlite3.dll, .\appsettings.json $InstallDir

# Каталог данных доступен только администраторам и службе (LocalSystem).
$data = Join-Path $InstallDir 'data'
New-Item -ItemType Directory -Force $data | Out-Null
# SID вместо имён: на русской Windows группы называются «Администраторы» и «СИСТЕМА».
icacls $data /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Не удалось ограничить доступ к каталогу $data (icacls, код $LASTEXITCODE)." }

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName
    sc.exe delete $serviceName | Out-Null
    Start-Sleep 2
}

New-Service -Name $serviceName `
    -DisplayName 'Remote Access - Address Book server' `
    -BinaryPathName (Join-Path $InstallDir 'raab-server.exe') `
    -StartupType Automatic | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
Start-Service $serviceName

Write-Host "Служба $serviceName запущена. Проверка: Invoke-RestMethod http://127.0.0.1:5080/api/health"
Write-Host "Создайте администратора: & '$InstallDir\raab-server.exe' user add admin --admin"
