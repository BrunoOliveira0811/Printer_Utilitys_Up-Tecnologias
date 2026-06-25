# Arquitetura proposta

## Decisoes principais

- Plataforma: `Windows 10/11 64 bits`
- UI: `WPF`
- Framework: `.NET 8`
- Padrao arquitetural: `MVVM`
- Persistencia local: `JSON` em `%AppData%\PrinterScannerApp`
- Exportacao: `CSV` e `XLSX`

## Mapeamento dos requisitos

### Requisitos funcionais

- `RF01`: `IpRangeService.GetCurrentSubnet()` + `MainWindow.Window_Loaded()`
- `RF02`: `IpRangeService.ExpandManualRange()`
- `RF03`: `IpRangeService.ExpandCidr()`
- `RF04`: `NetworkScannerService.TryReadSnmpAsync()`
- `RF05`: `NetworkScannerService.IsPortOpenAsync()`
- `RF06`: `NetworkScannerService.TryReadMacAddressAsync()`
- `RF07`: `NetworkScannerService.SelectPreferredName()`
- `RF08`: campos `SubnetMask` e `Gateway` no modelo `PrinterDevice`
- `RF09`: campo `DhcpStatus` com fallback para `Nao Identificado`
- `RF10`: `ExportService.ExportCsvAsync()`
- `RF11`: `ExportService.ExportXlsxAsync()`
- `RF12`: filtro por `SearchText` em `MainViewModel`
- `RF13`: `CancellationTokenSource` em `MainViewModel`
- `RF14`: `SettingsService.LoadLastScanAsync()` e `SaveLastScanAsync()`
- `RF15`: `SnmpCommunitiesText` e `settings.json`

### Requisitos nao funcionais

- `RNF01`: alvo `net8.0-windows`
- `RNF02`: concorrencia dinamica no `NetworkScannerService`
- `RNF03`: processamento assincrono com `Task`, `Progress` e `CancellationToken`
- `RNF04`: base pronta para publicacao `self-contained`
- `RNF05`: modelo enxuto em memoria
- `RNF06`: janela redimensionavel com `DataGrid` adaptavel
- `RNF07`: implementado em `C# + WPF`
- `RNF08`: tratamento centralizado de erros e log local
- `RNF09`: captura de MAC via `arp -a`, sem exigir UAC por padrao
- `RNF10`: textos em `pt-BR`
- `RNF11`: depende de publicacao final e trimming
- `RNF12`: limite dinamico de concorrencia
- `RNF13`: persistencia em JSON no `AppData`
- `RNF14`: ordenacao habilitada no `DataGrid`
- `RNF15`: `ProgressBar` + percentual textual

### Regras de negocio

- `RN01` e `RN02`: validacao de IP/range no `IpRangeService`
- `RN03`: deduplicacao por MAC/IP em `MainViewModel.UpsertDevice()`
- `RN04`: descarte de hosts sem caracteristicas de impressao
- `RN05`: timeout padrao de `1500 ms`
- `RN06`: nome padrao `Dispositivo de Impressao Desconhecido`
- `RN07`: MAC em maiusculo com hifen
- `RN08`: fallback `Nao Identificado` para DHCP
- `RN09`: prioridade `PrinterName -> SysName -> DNS reverso`
- `RN10`: exportacao habilitada apenas com dados
- `RN11`: atualizacao em vez de duplicacao
- `RN12`: bloqueio sem interface de rede ativa
- `RN13`: ordenacao inicial crescente por IP
- `RN14`: sugestao de mascara da rede local
- `RN15`: rotacao de logs em `5 MB`

## Proximos passos recomendados

1. Instalar o `.NET 8 SDK` no ambiente.
2. Restaurar pacotes NuGet e validar a compilacao.
3. Adicionar OIDs especificos por fabricante para rede, gateway e DHCP.
4. Incluir testes automatizados para parser de IP/CIDR e deduplicacao.
5. Empacotar como executavel unico via `dotnet publish`.
