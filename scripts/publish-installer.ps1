param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$UtilitariosSource = "C:\Users\Admin\Downloads\UTILITARIOS"
)

$ErrorActionPreference = "Stop"

$ProjectRoot      = Split-Path -Parent $PSScriptRoot
$AppProject       = Join-Path $ProjectRoot "src\PrinterScanner.App\PrinterScanner.App.csproj"
$InstallerProject = Join-Path $ProjectRoot "src\PrinterScanner.Installer\PrinterScanner.Installer.csproj"
$EmbeddedDir      = Join-Path $ProjectRoot "src\PrinterScanner.Installer\embedded-app"
$PublishDir       = Join-Path $ProjectRoot "publish\$Runtime-installer"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "O SDK do .NET 8 nao foi encontrado no PATH. Instale o SDK e execute este script novamente."
}

# Remove publicação anterior
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force $EmbeddedDir | Out-Null
New-Item -ItemType Directory -Force $PublishDir  | Out-Null

# ------------------------------------------------------------------
# Etapa 1: Publicar PrinterScanner.App como framework-dependent (~2 MB)
# ------------------------------------------------------------------
Write-Host "Etapa 1/3: publicando PrinterScanner.App (framework-dependent)..."

$publishDirArg = "/p:PublishDir=" + $EmbeddedDir

& $dotnet.Source publish $AppProject `
    -c Release -r $Runtime --self-contained false `
    /p:PublishSingleFile=true `
    /p:DebugType=None /p:DebugSymbols=false /p:PublishReadyToRun=false `
    $publishDirArg

if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar PrinterScanner.App (codigo $LASTEXITCODE)" }

# Garante o nome PrinterScanner.App.exe esperado pelo instalador
$publishedApp = Get-ChildItem $EmbeddedDir -Filter "*.exe" |
    Where-Object { $_.Name -ne "PrinterScanner.App.exe" } |
    Select-Object -First 1
if ($publishedApp) {
    $target = Join-Path $EmbeddedDir "PrinterScanner.App.exe"
    if (Test-Path $target) { Remove-Item $target -Force }
    Rename-Item $publishedApp.FullName "PrinterScanner.App.exe" -Force
}

$appSize = [Math]::Round((Get-Item (Join-Path $EmbeddedDir "PrinterScanner.App.exe")).Length / 1MB, 1)
Write-Host "  App embutido: $appSize MB"
Write-Host ""

# ------------------------------------------------------------------
# Etapa 2: Empacotar utilitarios em Utilitarios.zip
# ------------------------------------------------------------------
Write-Host "Etapa 2/3: empacotando utilitarios..."

$zipPath = Join-Path $EmbeddedDir "Utilitarios.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

if (Test-Path $UtilitariosSource) {
    Compress-Archive -Path (Join-Path $UtilitariosSource "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $zipSize = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    $count   = (Get-ChildItem $UtilitariosSource -Directory).Count
    Write-Host "  $count utilitario(s) empacotados: $zipSize MB (comprimido)"
} else {
    Write-Warning "Pasta de utilitarios nao encontrada: $UtilitariosSource"
    Write-Warning "Instalador sera gerado sem utilitarios."
}
Write-Host ""

# ------------------------------------------------------------------
# Etapa 3: Publicar PrinterScanner.Installer (self-contained, embute tudo)
# ------------------------------------------------------------------
Write-Host "Etapa 3/3: publicando PrinterScanner.Installer (self-contained)..."

$installerPublishDirArg = "/p:PublishDir=" + $PublishDir

& $dotnet.Source publish $InstallerProject `
    -c Release -r $Runtime --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None /p:DebugSymbols=false /p:PublishReadyToRun=false `
    $installerPublishDirArg

if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o instalador (codigo $LASTEXITCODE)" }

Write-Host ""
Write-Host "==========================================="
$setupExe = Get-ChildItem $PublishDir -Filter "*.exe" | Select-Object -First 1
if ($setupExe) {
    $setupSize = [Math]::Round($setupExe.Length / 1MB, 1)
    Write-Host "Instalador gerado : $($setupExe.FullName)"
    Write-Host "Tamanho           : $setupSize MB"
} else {
    Write-Host "Publicacao concluida em: $PublishDir"
}
Write-Host "==========================================="
