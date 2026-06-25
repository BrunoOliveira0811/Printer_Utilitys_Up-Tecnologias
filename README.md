# Utilitário de Impressoras - UP Tecnologias

**Versão 2.0** · Windows 10/11 · .NET 8 WPF · MVVM

Aplicativo desktop para descoberta automática de impressoras em rede, identificação por SNMP e portas, gerenciamento de nomes, instalação de drivers e relatórios de inventário.

---

## Sumário

- [O que há de novo na v2.0](#o-que-há-de-novo-na-v20)
- [Funcionalidades](#funcionalidades)
- [Arquitetura de distribuição](#arquitetura-de-distribuição)
- [Protocolos de identificação](#protocolos-de-identificação)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Pré-requisitos](#pré-requisitos)
- [Como compilar e publicar](#como-compilar-e-publicar)
- [Como usar](#como-usar)
- [Configurações e persistência](#configurações-e-persistência)
- [Colunas da grade de resultados](#colunas-da-grade-de-resultados)
- [Dependências](#dependências)
- [Privacidade e dados](#privacidade-e-dados)

---

## O que há de novo na v2.0

| # | Novidade |
|---|---|
| 1 | **Modo escuro** — alternância instantânea entre tema claro e escuro, preferência salva entre sessões |
| 2 | **Nomes persistentes por MAC** — nomes editados são salvos localmente e restaurados automaticamente em varreduras futuras, mesmo que o IP mude |
| 3 | **Renomeação automática no Windows** — ao editar o nome no app, a impressora instalada localmente (mesma porta IP) é renomeada via PowerShell |
| 4 | **Salvar relatório em .txt** — exporta todo o inventário para arquivo de texto formatado, útil para documentar a rede antes de uma troca |
| 5 | **Seleção de interface de rede** — soluciona falha silenciosa ao usar VPN; o usuário escolhe qual interface varrer |
| 6 | **Instalação de driver embutida** — driver incluído diretamente no executável; um clique inicia o instalador com elevação |
| 7 | **Controle de Impressoras** — atalho direto para o painel de impressoras do Windows |
| 8 | **Launcher com verificação de .NET 8** — `PrinterScanner.exe` auto-suficiente verifica o runtime antes de lançar o app; se ausente, oferece link para download |
| 9 | **Sem dependências externas** — arquitetura framework-dependent reduz o executável principal para ~2 MB |
| 10 | **Removido**: exportação CSV e XLSX |

---

## Funcionalidades

| Funcionalidade | Descrição |
|---|---|
| **Varredura — Sub-rede atual** | Detecta automaticamente a sub-rede da interface selecionada e varre todos os hosts |
| **Varredura — Faixa manual** | Varre um intervalo definido (ex: `192.168.1.10` → `192.168.1.50`) |
| **Varredura — CIDR** | Varre uma rede em notação CIDR (ex: `192.168.0.0/24`) |
| **Seleção de interface** | ComboBox lista todas as interfaces ativas; essencial quando há VPN conectada |
| **Identificação de modelo** | SNMP, HTTP e ESC/POS para obter o modelo exato do dispositivo |
| **Gateway, máscara e DHCP** | Leitura via SNMP padrão e OIDs vendor-specific (HP, Epson, Brother, Lexmark, Kyocera) |
| **Endereço MAC** | Capturado via tabela ARP local |
| **Nomes editáveis e persistentes** | Duplo clique para editar; nome salvo por MAC em `device-names.json` |
| **Renomear impressora Windows** | Ao salvar um nome, a impressora instalada na porta correspondente é renomeada automaticamente |
| **Alterar IP** | Reconfigura o IP da impressora diretamente via SNMP |
| **Instalar driver** | Extrai e executa o instalador embutido com elevação de administrador |
| **Controle de Impressoras** | Abre `shell:::{A8A91A66...}` — painel de impressoras do Windows |
| **Salvar relatório .txt** | Gera arquivo de texto com todo o inventário (IP, nome, MAC, máscara, gateway, DHCP, portas) |
| **Filtro em tempo real** | Pesquisa por IP, nome, MAC ou descrição enquanto digita |
| **Cancelamento** | Interrompe a varredura a qualquer momento |
| **Modo escuro / claro** | Alterna o tema instantaneamente; preferência salva em `settings.json` |
| **Atualizar redes** | Recarrega a lista de interfaces sem reiniciar o app (útil ao conectar/desconectar VPN) |

---

## Arquitetura de distribuição

O aplicativo é distribuído como **dois executáveis**:

```
UTILITÁRIO DE IMPRESSORAS\
├── PrinterScanner.exe        (~12 MB) — Launcher auto-suficiente
└── PrinterScanner.App.exe    (~2 MB)  — Aplicativo principal
```

### `PrinterScanner.exe` — Launcher

- **Auto-suficiente** (self-contained, trimmed) — não precisa de .NET instalado
- Verifica se o **.NET 8 Desktop Runtime** está presente em `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.*`
- Se encontrado: lança `PrinterScanner.App.exe` silenciosamente
- Se ausente: exibe mensagem e oferece abrir a página de download da Microsoft

### `PrinterScanner.App.exe` — Aplicativo principal

- **Framework-dependent** — requer .NET 8 Desktop Runtime
- Todas as DLLs gerenciadas embutidas em arquivo único (`PublishSingleFile=true`)
- Driver de impressora embutido como recurso (`EmbeddedResource`)

> O usuário final sempre executa `PrinterScanner.exe`. O launcher é transparente quando o runtime está instalado.

---

## Protocolos de identificação

### Modelo do dispositivo (em ordem de prioridade)

```
1. SNMP — hrDeviceDescr          (OID 1.3.6.1.2.1.25.3.2.1.3.1)
2. SNMP — prtGeneralPrinterName  (OID 1.3.6.1.2.1.43.5.1.1.16.1)
3. HTTP  — título da página de gerenciamento web (porta 80)
4. ESC/POS — comandos GS I via porta 9100 (impressoras térmicas)
5. SNMP — sysDescr curto         (OID 1.3.6.1.2.1.1.1.0, até 64 chars)
6. SNMP — sysName                (OID 1.3.6.1.2.1.1.5.0)
```

### Configurações de rede via SNMP

| Campo | OID | Descrição |
|---|---|---|
| Máscara de sub-rede | `1.3.6.1.2.1.4.20.1.3.{ip}` | `ipAdEntNetMask` — RFC 1213 |
| Gateway padrão | `1.3.6.1.2.1.4.21.1.7.0.0.0.0` | `ipRouteNextHop` rota 0.0.0.0 |
| DHCP (HP) | `1.3.6.1.4.1.11.2.4.3.5.13.0` | 1=Fixo, 3/4=DHCP |
| DHCP (Epson) | `1.3.6.1.4.1.1248.1.2.2.1.1.3.1` | 0=Fixo, 1=DHCP |
| DHCP (Brother) | `1.3.6.1.4.1.2435.2.3.9.4.2.1.4.3.1.0` | 0=DHCP, 1=Fixo |
| DHCP (Lexmark) | `1.3.6.1.4.1.641.2.1.2.1.3.1` | 1=Fixo, 3=DHCP |
| DHCP (Kyocera) | `1.3.6.1.4.1.1602.1.2.1.6.0` | 0=Fixo, 1=DHCP |

### Camadas SNMP

1. **SNMPv2c** — GET com todos os OIDs em uma única requisição
2. **SNMPv1** — fallback para dispositivos mais antigos
3. **Queries individuais** — um OID por vez para dispositivos que rejeitam GET multi-OID

### Portas de impressão verificadas

| Porta | Protocolo | Uso |
|---|---|---|
| `9100` | RAW / JetDirect | Principal indicador de impressora |
| `515` | LPD/LPR | Line Printer Daemon |
| `631` | IPP | Internet Printing Protocol |
| `161/UDP` | SNMP | Consulta de informações |

---

## Estrutura do projeto

```
PROJETOS/
├── README.md
└── src/
    ├── PrinterScanner.Launcher/          # Projeto do launcher
    │   ├── PrinterScanner.Launcher.csproj
    │   └── Program.cs                    # Verificação de runtime + lançamento
    └── PrinterScanner.App/               # Projeto principal WPF
        ├── Drivers/
        │   └── Instalar_Impressora_0.9.2.exe  # Driver embutido (EmbeddedResource)
        ├── Infrastructure/
        │   ├── ObservableObject.cs
        │   └── RelayCommand.cs
        ├── Models/
        │   ├── AppSettings.cs            # Configurações persistidas (JSON)
        │   ├── NetworkInterfaceInfo.cs   # Dados de interface de rede
        │   ├── PrinterDevice.cs          # Modelo de dados da impressora
        │   ├── ScanMode.cs               # Enum: SubredeAtual | FaixaManual | Cidr
        │   ├── ScanProgress.cs           # Progresso em tempo real
        │   └── ScanRequest.cs            # Parâmetros de uma varredura
        ├── Resources/
        │   ├── logo.png
        │   ├── logo.ico
        │   └── Themes/
        │       ├── Light.xaml            # Paleta de cores — modo claro
        │       └── Dark.xaml             # Paleta de cores — modo escuro
        ├── Services/
        │   ├── DeviceNameService.cs      # Persistência de nomes por MAC
        │   ├── DriverService.cs          # Extração e execução do driver embutido
        │   ├── FileLogService.cs         # Log local com rotação
        │   ├── IpRangeService.cs         # Expansão de faixas, sub-redes e CIDR
        │   ├── NetworkScannerService.cs  # Motor de varredura e identificação
        │   ├── PrinterIpService.cs       # Reconfiguração de IP via SNMP
        │   ├── PrinterWindowsService.cs  # Renomeação de impressoras no Windows
        │   └── SettingsService.cs        # Persistência de configurações (JSON)
        ├── ViewModels/
        │   ├── ChangeIpViewModel.cs
        │   └── MainViewModel.cs
        ├── Views/
        │   └── ChangeIpDialog.xaml
        ├── App.xaml / App.xaml.cs
        ├── MainWindow.xaml
        └── MainWindow.xaml.cs
```

---

## Pré-requisitos

**Para compilar:**
- Windows 10/11 64-bit
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

**Para executar o distribuível:**
- Windows 10/11 64-bit
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (o launcher avisa e oferece o link caso não esteja instalado)

---

## Como compilar e publicar

### Compilar (desenvolvimento)

```powershell
dotnet build src\PrinterScanner.App\PrinterScanner.App.csproj
```

### Publicar — Launcher (auto-suficiente, ~12 MB)

```powershell
dotnet publish src\PrinterScanner.Launcher\PrinterScanner.Launcher.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o ".\publish"
```

### Publicar — App principal (framework-dependent, ~2 MB)

```powershell
dotnet publish src\PrinterScanner.App\PrinterScanner.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -o ".\publish"
```

Os dois comandos devem apontar para a mesma pasta de saída. O resultado final:

```
publish\
├── PrinterScanner.exe       ← usuário executa este
└── PrinterScanner.App.exe   ← lançado automaticamente pelo launcher
```

---

## Como usar

### 1. Selecionar interface de rede

No campo **Interface de rede**, escolha a placa de rede que está conectada à rede das impressoras.  
Se houver VPN ativa, selecione a interface correta para evitar varredura na rede errada.  
Use **Atualizar redes** para recarregar a lista após conectar ou desconectar.

### 2. Selecionar modo de varredura

| Modo | Quando usar |
|---|---|
| **Sub-rede atual** | Varre toda a sub-rede da interface selecionada automaticamente |
| **Faixa manual** | Informe IP inicial e final (ex: `192.168.1.10` → `192.168.1.100`) |
| **CIDR** | Informe a rede em notação CIDR (ex: `192.168.0.0/24`) |

### 3. Configurar SNMP

No campo **SNMP**, informe as comunidades separadas por `;` ou `,`.  
Padrão: `public; private`

### 4. Iniciar e acompanhar

Clique em **Iniciar**. A barra de progresso e o status mostram o andamento em tempo real.

### 5. Editar e salvar nomes

Dê **duplo clique** na coluna **Nome** para editar o modelo da impressora e pressione **Enter**.  
O nome é salvo automaticamente vinculado ao MAC — em próximas varreduras será restaurado mesmo que o IP mude.  
Se houver uma impressora com aquela porta IP instalada no Windows, ela também será renomeada.

### 6. Salvar relatório

Clique em **Salvar Relatorio** para exportar o inventário completo em `.txt`.  
Útil para documentar a rede do cliente antes de uma troca ou manutenção.

### 7. Instalar driver

Clique em **Instalar Driver** para executar o instalador embutido no aplicativo.  
O instalador solicitará permissão de administrador automaticamente.

### 8. Controle de Impressoras

Abre diretamente o painel de impressoras do Windows para gerenciamento rápido.

### 9. Alterar IP

Selecione uma impressora na grade e clique em **Alterar IP** para reconfigurar o endereço via SNMP.

### 10. Modo escuro

Clique em **Modo Escuro** para alternar o tema. A preferência é salva e restaurada ao reabrir o app.

---

## Configurações e persistência

Dados salvos em `%AppData%\PrinterScannerApp\`:

| Arquivo | Conteúdo |
|---|---|
| `settings.json` | Comunidades SNMP, timeout, concorrência, preferência de tema |
| `device-names.json` | Mapeamento MAC → nome personalizado da impressora |
| `startup-error.log` | Log de erros de inicialização (diagnóstico) |

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `SnmpCommunities` | `public`, `private` | Comunidades SNMP tentadas em cada host |
| `TimeoutMilliseconds` | `1500` | Tempo máximo de espera por host (ms) |
| `MaxConcurrentScans` | `0` (auto) | Hosts em paralelo — `0` = `ProcessorCount × 8`, entre 8 e 64 |
| `DarkModeEnabled` | `false` | Tema escuro ativado |

---

## Colunas da grade de resultados

| Coluna | Fonte | Editável |
|---|---|---|
| **IP** | Varredura de rede | Não |
| **Nome** | SNMP / ESC/POS / HTTP / Manual | **Sim** (duplo clique — salvo por MAC) |
| **MAC** | Tabela ARP local | Não |
| **Máscara** | SNMP `ipAdEntNetMask` | Não |
| **Gateway** | SNMP `ipRouteNextHop` | Não |
| **DHCP** | SNMP vendor-specific | Não |
| **SNMP** | Respondeu ao GET SNMP? | Não |
| **9100** | Porta TCP 9100 aberta? | Não |
| **515** | Porta TCP 515 aberta? | Não |
| **631** | Porta TCP 631 aberta? | Não |
| **Descrição** | SNMP `sysDescr` | Não |

---

## Dependências

| Pacote | Versão | Uso |
|---|---|---|
| `Lextm.SharpSnmpLib` | 12.5.7 | Consultas SNMP v1/v2c |

> `DocumentFormat.OpenXml` (exportação XLSX) foi removido na v2.0.

---

## Privacidade e dados

- Nomes de dispositivos e preferências são armazenados localmente em `%AppData%\PrinterScannerApp`
- O aplicativo **não realiza nenhuma comunicação com servidores externos**
- A varredura ocorre exclusivamente na rede local definida pelo usuário
- Logs de erro são gravados apenas localmente para diagnóstico

---

*Desenvolvido por UP Tecnologias · v2.0*
