# Utilitário de Impressoras - UP Tecnologias

**Versão 3.0** · Windows 10/11 · .NET 8 WPF · MVVM

Solução completa para descoberta, diagnóstico e gerenciamento de impressoras em ambientes corporativos. Ao abrir, já exibe as impressoras instaladas no computador; ao varrer, encontra novas na rede e permite alterar IP, gerenciar filas, instalar drivers e muito mais.

---

## Sumário

- [O que há de novo na v3.0](#o-que-há-de-novo-na-v30)
- [Funcionalidades](#funcionalidades)
- [Alterar IP — Protocolos suportados](#alterar-ip--protocolos-suportados)
- [Arquitetura de distribuição](#arquitetura-de-distribuição)
- [Protocolos de identificação via SNMP](#protocolos-de-identificação-via-snmp)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Pré-requisitos](#pré-requisitos)
- [Como compilar e publicar](#como-compilar-e-publicar)
- [Como usar](#como-usar)
- [Configurações e persistência](#configurações-e-persistência)
- [Colunas da grade de resultados](#colunas-da-grade-de-resultados)
- [Dependências](#dependências)
- [Privacidade e dados](#privacidade-e-dados)

---

## O que há de novo na v3.0

| # | Novidade |
|---|---|
| 1 | **Impressoras instaladas na tela inicial** — USB e rede aparecem automaticamente sem precisar varrer |
| 2 | **Detecção de modo offline** — coluna "Offline" aparece (em vermelho) quando ativa; duplo clique desativa |
| 3 | **Detecção de fila pausada** — coluna "Fila" aparece (em vermelho) quando pausada; duplo clique retoma |
| 4 | **Alterar IP com múltiplos protocolos** — Rongta, Tectoy Q4/i7, ESC/POS GS(N), HTTP e Automático |
| 5 | **Busca Ampliada** — ARP + broadcast UDP; adiciona IPs temporários para alcançar sub-redes adjacentes |
| 6 | **Limpar fila de impressão** — cancela trabalhos pendentes da impressora selecionada ou de todas |
| 7 | **Pausar / Retomar fila** — alterna o estado da fila da impressora instalada |
| 8 | **Reiniciar Spooler** — para o serviço, apaga arquivos travados e reinicia (com UAC) |
| 9 | **Compartilhar impressora na rede** — ativa/remove compartilhamento; avisa quando driver é necessário |
| 10 | **Impressão de teste** — página ESC/POS direta via TCP 9100 (sem driver); fallback via Windows |
| 11 | **Alterar porta USB** — reatribui impressora USB a outra porta sem reinstalar driver |
| 12 | **Descrição editável e persistente** — duplo clique para editar; salva por MAC entre sessões |
| 13 | **Utilitários integrados** — ferramentas de diagnóstico e configuração acessíveis pelo app |
| 14 | **Colunas contextuais** — "Offline" e "Fila" só aparecem quando há problema, mantendo a interface limpa |

---

## Funcionalidades

### Descoberta de Impressoras

| Funcionalidade | Descrição |
|---|---|
| **Impressoras USB instaladas** | Exibidas automaticamente ao abrir o app, sem varredura |
| **Impressoras de rede instaladas** | Impressoras já instaladas no Windows com porta TCP/IP aparecem na lista |
| **Varredura — Sub-rede atual** | Detecta a sub-rede da interface selecionada e varre todos os hosts |
| **Varredura — Faixa manual** | Varre um intervalo definido (ex: `192.168.1.10` → `192.168.1.50`) |
| **Varredura — CIDR** | Varre uma rede em notação CIDR (ex: `192.168.0.0/24`) |
| **Busca Ampliada** | ARP + broadcast UDP; detecta impressoras em sub-redes adjacentes (ex: PC em 192.168.100.x encontra impressora em 192.168.1.x) |
| **IPs temporários automáticos** | Quando sub-redes adjacentes são inacessíveis, adiciona IPs secundários na placa de rede (UAC), aguarda ativação e remove após a varredura |
| **Seleção de interface** | ComboBox com todas as interfaces ativas; essencial em ambientes com VPN |
| **Identificação por SNMP** | Modelo, fabricante, máscara, gateway e status DHCP via SNMP v1/v2c |
| **Endereço MAC** | Capturado via tabela ARP local |
| **Filtro em tempo real** | Pesquisa por IP, nome, MAC ou descrição enquanto digita |
| **Cancelamento** | Interrompe a varredura a qualquer momento |

### Gerenciamento de Status

| Funcionalidade | Descrição |
|---|---|
| **Detecção de modo offline** | Identifica impressoras com "Usar impressora offline" ativo |
| **Desativar modo offline** | Duplo clique na coluna "Offline" ou menu de contexto |
| **Detecção de fila pausada** | Identifica impressoras com fila de impressão pausada |
| **Retomar fila pausada** | Duplo clique na coluna "Fila" ou menu de contexto |
| **Colunas contextuais** | As colunas "Offline" e "Fila" ficam ocultas quando não há problema |

### Configuração de Rede

| Funcionalidade | Descrição |
|---|---|
| **Alterar IP** | Reconfigura IP, máscara e gateway com seleção de protocolo por modelo |
| **Detecção automática de protocolo** | Identifica o protocolo pelo nome do dispositivo (Rongta, Tectoy, i7, Epson...) |
| **Aguarda confirmação** | Verifica ping e porta 9100 no novo IP antes de declarar sucesso |

### Gerenciamento de Fila e Spooler

| Funcionalidade | Descrição |
|---|---|
| **Limpar fila** | Cancela todos os trabalhos da impressora selecionada ou de todas |
| **Pausar / Retomar fila** | Alterna o estado da fila via WMI (`Win32_Printer.Pause/Resume`) |
| **Reiniciar Spooler** | Para o serviço Spooler, apaga `%SystemRoot%\System32\spool\PRINTERS\*.*` e reinicia (requer UAC) |

### Impressão e Driver

| Funcionalidade | Descrição |
|---|---|
| **Impressão de teste** | Página ESC/POS personalizada via TCP 9100, sem driver instalado; fallback via Windows |
| **Instalar driver** | Executa o instalador embutido no próprio executável com elevação de administrador |
| **Compartilhar na rede** | Ativa ou remove compartilhamento da impressora; exibe aviso se driver não instalado |
| **Alterar porta USB** | Reatribui impressora USB a outra porta (USB001, USB002...) sem reinstalar driver |

### Personalização e Relatório

| Funcionalidade | Descrição |
|---|---|
| **Nome editável e persistente** | Duplo clique para editar; salvo por MAC em `device-names.json`; restaurado automaticamente |
| **Descrição editável e persistente** | Duplo clique para editar; salvo por MAC em `device-descriptions.json` |
| **Renomear impressora Windows** | Ao salvar um nome, a impressora instalada na porta correspondente é renomeada automaticamente |
| **Salvar relatório .txt** | Exporta o inventário completo para `C:\impressoras_YYYY-MM-DD_HH-mm.txt` |
| **Modo escuro / claro** | Alternância instantânea; preferência salva entre sessões |
| **Controle de Impressoras** | Abre o painel de impressoras do Windows |
| **Utilitários** | Acesso a ferramentas de diagnóstico e configuração externas |

---

## Alterar IP — Protocolos suportados

O protocolo correto é detectado automaticamente pelo nome do dispositivo e pode ser selecionado manualmente no diálogo de alteração de IP.

| Protocolo | Modelos | Mecanismo |
|---|---|---|
| **Rongta / Jetway JP-800** | Rongta, Jetway JP-800 | TCP 9100 · payload `1F 69 [IP] 1F 25 00 [MASK] 1F 25 01 [GW]` — confirmado via Wireshark |
| **Tectoy Q4 / i7** | Tectoy Q4, i7 | TCP 9100 · payload `1F 1B 1F 22 [IP 4B]` — confirmado via captura de pacotes |
| **ESC/POS GS(N)** | Epson, Elgin, compatíveis | TCP 9100 · comandos `GS ( N` (fn 00h IP, 01h Mask, 02h GW, 10h aplicar) |
| **HTTP** | Modelos com interface web | GET para `/cgi-bin/netconfig.cgi` e `/config` nas portas 80 e 8080 |
| **Automático** | Qualquer modelo | Testa todos em sequência até confirmar o novo IP |

### Detecção automática de protocolo por nome

| Contém no nome / descrição | Protocolo selecionado |
|---|---|
| `tectoy`, `q4`, `i7` | Tectoy Q4 / i7 |
| `jetway`, `jp-800`, `rongta` | Rongta / Jetway JP-800 |
| `epson`, `elgin`, `tm-` | ESC/POS GS(N) |
| outros | Automático |

---

## Arquitetura de distribuição

Distribuído em dois formatos:

### Portátil (`publish\win-x64-portable\`)

```
PrinterScanner.exe   (~11 MB)   — Launcher self-contained com app embutido
```

O Launcher extrai e executa o app internamente. O usuário precisa apenas deste arquivo.

### Instalador (`publish\win-x64-installer\`)

```
PrinterScanner-Setup.exe   (~72 MB)   — Instalador com runtime .NET 8 e utilitários
```

Inclui .NET 8 Desktop Runtime (quando ausente), utilitários de diagnóstico e atalhos no menu Iniciar.

### Scripts de publicação

```powershell
scripts\publish-portable.ps1    # Gera o executável portátil
scripts\publish-installer.ps1   # Gera o instalador completo
```

> **Sempre execute os dois scripts após qualquer alteração de código.**

---

## Protocolos de identificação via SNMP

### Modelo do dispositivo (em ordem de prioridade)

```
1. SNMP — hrDeviceDescr          (OID 1.3.6.1.2.1.25.3.2.1.3.1)
2. SNMP — prtGeneralPrinterName  (OID 1.3.6.1.2.1.43.5.1.1.16.1)
3. HTTP  — título da página de gerenciamento web (porta 80)
4. ESC/POS — comandos GS I via porta 9100 (impressoras térmicas)
5. SNMP — sysDescr               (OID 1.3.6.1.2.1.1.1.0, até 64 chars)
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

---

## Estrutura do projeto

```
PROJETOS/
├── README.md
├── scripts/
│   ├── publish-portable.ps1          # Gera o executável portátil
│   ├── publish-installer.ps1         # Gera o instalador completo
│   └── clean-cache.ps1               # Limpa caches de build
└── src/
    ├── PrinterScanner.Launcher/      # Launcher self-contained
    │   ├── Program.cs                # Extrai e executa o app embutido
    │   └── embedded-app/             # App principal embutido como recurso
    ├── PrinterScanner.App/           # Aplicativo principal WPF
    │   ├── Infrastructure/
    │   │   ├── BindingProxy.cs       # Proxy para bindings em ContextMenu
    │   │   ├── ObservableObject.cs
    │   │   └── RelayCommand.cs
    │   ├── Models/
    │   │   ├── AppSettings.cs
    │   │   ├── NetworkInterfaceInfo.cs
    │   │   ├── PrinterDevice.cs      # IP, MAC, máscara, gateway, status, offline, fila
    │   │   ├── ScanMode.cs
    │   │   ├── ScanProgress.cs
    │   │   └── ScanRequest.cs
    │   ├── Resources/Themes/
    │   │   ├── Light.xaml
    │   │   └── Dark.xaml
    │   ├── Services/
    │   │   ├── DeviceNameService.cs      # Persistência de nomes e descrições por MAC
    │   │   ├── DriverService.cs          # Extração e execução do driver embutido
    │   │   ├── FileLogService.cs         # Log local
    │   │   ├── IpRangeService.cs         # Expansão de faixas, sub-redes e CIDR
    │   │   ├── NetworkScannerService.cs  # Motor de varredura, SNMP e Busca Ampliada
    │   │   ├── PrinterIpService.cs       # Alteração de IP (Rongta/Q4/ESC-POS/HTTP)
    │   │   ├── PrinterWindowsService.cs  # Fila, Spooler, USB, compartilhamento, offline
    │   │   ├── SettingsService.cs
    │   │   └── UtilitariosService.cs
    │   ├── ViewModels/
    │   │   └── MainViewModel.cs
    │   └── Views/
    │       ├── ActionsWindow.xaml        # Painel de ações laterais
    │       ├── ChangeIpDialog.xaml       # Diálogo de alteração de IP
    │       ├── ChangeIpViewModel.cs
    │       ├── ChangeUsbPortDialog.xaml  # Diálogo de troca de porta USB
    │       ├── ChangeUsbPortViewModel.cs
    │       ├── DriverRequiredDialog.xaml # Aviso de driver necessário
    │       └── UtilitariosWindow.xaml    # Janela de utilitários
    └── PrinterScanner.Installer/         # Projeto do instalador
```

---

## Pré-requisitos

**Para compilar:**
- Windows 10/11 64-bit
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

**Para executar o portátil:**
- Windows 10/11 64-bit
- Nenhuma dependência — o launcher é self-contained

**Para executar o app principal diretamente:**
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Como compilar e publicar

### Compilar (desenvolvimento)

```powershell
dotnet build src\PrinterScanner.App\PrinterScanner.App.csproj -c Release
```

### Publicar (ambos os formatos)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
powershell -ExecutionPolicy Bypass -File scripts\publish-installer.ps1
```

Resultados:

```
publish\win-x64-portable\
└── PrinterScanner.exe          ← executável portátil (~11 MB)

publish\win-x64-installer\
└── PrinterScanner-Setup.exe    ← instalador completo (~72 MB)
```

---

## Como usar

### 1. Tela inicial — impressoras já instaladas

Ao abrir, o app lista automaticamente as impressoras USB e de rede já instaladas no Windows. Não é necessário varrer a rede para vê-las.

### 2. Selecionar interface de rede

No campo **Interface de rede**, escolha a placa conectada à rede das impressoras. Use **Atualizar redes** após conectar ou desconectar VPN.

### 3. Selecionar modo de varredura

| Modo | Quando usar |
|---|---|
| **Sub-rede atual** | Varre toda a sub-rede da interface selecionada |
| **Faixa manual** | Informe IP inicial e final |
| **CIDR** | Informe a rede em notação CIDR |

### 4. Busca Ampliada

Clique em **Ações → Busca Ampliada** para localizar impressoras em sub-redes diferentes da sua (ex: PC em `192.168.100.x` encontra impressora em `192.168.1.x`). O app adiciona IPs temporários à placa de rede via UAC, realiza a varredura e os remove automaticamente.

### 5. Editar nome e descrição

Dê **duplo clique** na coluna **Nome** ou **Descrição** para editar. Os valores são salvos por MAC e restaurados automaticamente em varreduras futuras, mesmo que o IP mude.

### 6. Alterar IP

Selecione uma impressora e clique em **Ações → Alterar IP** (ou duplo clique na coluna IP). Escolha o protocolo manualmente ou deixe em **Automático**. O app aguarda a impressora responder no novo IP antes de confirmar o sucesso.

### 7. Resolver problemas de fila

- Se a coluna **Offline** aparecer em vermelho: dê duplo clique para desativar o modo offline
- Se a coluna **Fila** aparecer em vermelho: dê duplo clique para retomar a fila pausada
- Para trabalhos travados: **Ações → Limpar Fila** ou **Reiniciar Spooler**

### 8. Impressão de teste

Selecione uma impressora e clique em **Ações → Enviar Impressão de Teste**. O app envia uma página ESC/POS diretamente pela porta TCP 9100, sem necessidade de driver instalado.

### 9. Compartilhar impressora

Clique com botão direito e selecione **Compartilhar na Rede**. Se o driver não estiver instalado, o app exibe um aviso com atalho para os Utilitários.

### 10. Salvar relatório

**Ações → Salvar Relatório** gera um `.txt` em `C:\` com todo o inventário atual.

---

## Configurações e persistência

Dados salvos em `%AppData%\PrinterScannerApp\`:

| Arquivo | Conteúdo |
|---|---|
| `settings.json` | Comunidades SNMP, timeout, concorrência, tema |
| `device-names.json` | Mapeamento MAC → nome personalizado |
| `device-descriptions.json` | Mapeamento MAC → descrição personalizada |
| `startup-error.log` | Log de erros de inicialização |

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `SnmpCommunities` | `public` | Comunidades SNMP tentadas em cada host |
| `TimeoutMilliseconds` | `1500` | Tempo máximo de espera por host (ms) |
| `MaxConcurrentScans` | `0` (auto) | Hosts em paralelo — `0` = `ProcessorCount × 8`, entre 8 e 64 |
| `DarkModeEnabled` | `false` | Tema escuro ativado |

---

## Colunas da grade de resultados

| Coluna | Fonte | Editável | Observação |
|---|---|---|---|
| **IP / Porta** | Varredura / Windows | Não | Duplo clique abre diálogo de alteração de IP ou troca de porta USB |
| **Tipo** | Windows / Varredura | Não | `USB`, `Rede` |
| **Nome** | SNMP / ESC-POS / HTTP / Manual | **Sim** | Duplo clique para editar; salvo por MAC |
| **MAC** | Tabela ARP local | Não | |
| **Máscara** | SNMP `ipAdEntNetMask` | Não | |
| **Gateway** | SNMP `ipRouteNextHop` | Não | |
| **DHCP** | SNMP vendor-specific | Não | |
| **Offline** | `Win32_Printer.WorkOffline` | Via duplo clique | Visível apenas quando há problema |
| **Fila** | `Get-Printer.PrinterStatus` bit 1 | Via duplo clique | Visível apenas quando há problema |
| **Descrição** | SNMP `sysDescr` / Manual | **Sim** | Duplo clique para editar; salvo por MAC |

---

## Dependências

| Pacote | Versão | Uso |
|---|---|---|
| `Lextm.SharpSnmpLib` | 12.5.7 | Consultas SNMP v1/v2c |

---

## Privacidade e dados

- Nomes, descrições e preferências são armazenados localmente em `%AppData%\PrinterScannerApp`
- O aplicativo **não realiza nenhuma comunicação com servidores externos**
- A varredura ocorre exclusivamente na rede local definida pelo usuário
- Logs de erro são gravados apenas localmente para diagnóstico

---

*Desenvolvido por UP Tecnologias · v3.0*
