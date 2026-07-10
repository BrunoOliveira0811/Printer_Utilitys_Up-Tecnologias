param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectRoot     = Split-Path -Parent $PSScriptRoot
$AppProject      = Join-Path $ProjectRoot "src\PrinterScanner.App\PrinterScanner.App.csproj"
$LauncherProject = Join-Path $ProjectRoot "src\PrinterScanner.Launcher\PrinterScanner.Launcher.csproj"
$EmbeddedDir     = Join-Path $ProjectRoot "src\PrinterScanner.Launcher\embedded-app"
$PublishDir      = Join-Path $ProjectRoot "publish\$Runtime-portable"

# Remove publicação anterior (ignora erros se o exe estiver em uso)
if (Test-Path $PublishDir) {
    try { Remove-Item $PublishDir -Recurse -Force -ErrorAction Stop }
    catch { Write-Warning "Nao foi possivel limpar '$PublishDir' (arquivo em uso?). Publicando sobre o existente." }
}
New-Item -ItemType Directory -Force $PublishDir | Out-Null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "dotnet SDK nao encontrado. Instalando via winget..."
    winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements --silent
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path","User")
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw "Nao foi possivel localizar o dotnet SDK apos a instalacao. Reinicie o terminal e tente novamente." }
}

# Etapa 1: publicar o WPF App como framework-dependent (pequeno, ~2-3MB)
Write-Host "Etapa 1/2: publicando PrinterScanner.App (framework-dependent)..."
& $dotnet.Source publish $AppProject `
    -c Release `
    -r $Runtime `
    --self-contained false `
    /p:PublishSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:PublishReadyToRun=false `
    ("/p:PublishDir=" + $EmbeddedDir)

if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar PrinterScanner.App (codigo $LASTEXITCODE)" }

# Move o exe publicado para o nome esperado pelo Launcher
# (Remove destino antes — Rename-Item não sobrescreve destino existente)
$publishedApp = Get-ChildItem $EmbeddedDir -Filter "*.exe" |
    Where-Object { $_.Name -ne "PrinterScanner.App.exe" } |
    Select-Object -First 1
if ($publishedApp) {
    $target = Join-Path $EmbeddedDir "PrinterScanner.App.exe"
    if (Test-Path $target) { Remove-Item $target -Force }
    Rename-Item $publishedApp.FullName "PrinterScanner.App.exe"
}

Write-Host "App embutido pronto: $(Join-Path $EmbeddedDir 'PrinterScanner.App.exe')"
Write-Host ""

# Etapa 2: publicar o Launcher como self-contained + trimmed (~14MB)
Write-Host "Etapa 2/2: publicando PrinterScanner.Launcher (self-contained + trimmed)..."
& $dotnet.Source publish $LauncherProject `
    -c Release `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:PublishReadyToRun=false `
    ("/p:PublishDir=" + $PublishDir)

if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar PrinterScanner.Launcher (codigo $LASTEXITCODE)" }

Write-Host ""
$exe = Get-ChildItem $PublishDir -Filter "*.exe" | Select-Object -First 1
$size = if ($exe) { [Math]::Round($exe.Length / 1MB, 1) } else { "?" }
Write-Host "Publicacao concluida: $PublishDir\$($exe.Name) ($size MB)"
