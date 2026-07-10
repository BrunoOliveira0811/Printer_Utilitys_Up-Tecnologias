using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace PrinterScanner.App.Services;

public enum PrinterProtocol
{
    Auto,       // tenta todos em sequência
    Rongta,     // Rongta / Jetway JP-800  (1F 69 ...)
    TectoyQ4,   // Tectoy Q4 / i7          (1F 1B 1F 22 ...)
    EscPosGsN,  // ESC/POS GS ( N — Epson, Elgin
    Http        // interface web (porta 80/8080)
}

public sealed class PrinterIpService
{
    public async Task<bool> ChangeIpAsync(
        string currentIp,
        string newIp,
        string mask,
        string gateway,
        string macAddress,
        IProgress<string> status,
        IProgress<double> progressPct,
        PrinterProtocol protocol = PrinterProtocol.Auto,
        CancellationToken ct = default)
    {
        mask    = string.IsNullOrWhiteSpace(mask)    ? "255.255.255.0"     : mask;
        gateway = string.IsNullOrWhiteSpace(gateway) ? GuessGateway(currentIp) : gateway;

        status.Report($"Verificando alcance de {currentIp}...");
        if (!await PingAsync(currentIp, ct))
        {
            status.Report($"Impressora nao responde em {currentIp}. Verifique a rede.");
            return false;
        }
        progressPct.Report(10);

        bool tryRongta    = protocol is PrinterProtocol.Auto or PrinterProtocol.Rongta;
        bool tryTectoyQ4  = protocol is PrinterProtocol.Auto or PrinterProtocol.TectoyQ4;
        bool tryEscPosGsN = protocol is PrinterProtocol.Auto or PrinterProtocol.EscPosGsN;
        bool tryHttp      = protocol is PrinterProtocol.Auto or PrinterProtocol.Http;

        int step = 0, totalSteps =
            (tryRongta ? 1 : 0) + (tryTectoyQ4 ? 1 : 0) +
            (tryEscPosGsN ? 1 : 0) + (tryHttp ? 1 : 0);

        // ── Rongta (TCP 9100) — confirmado: Rongta / Jetway JP-800 ─────────────
        if (tryRongta)
        {
            step++;
            status.Report($"Tentativa {step}/{totalSteps} — Rongta / Jetway JP-800 (TCP 9100)...");
            bool sent = await TryRongtaTcpAsync(currentIp, newIp, mask, gateway, status, ct);
            progressPct.Report(10 + step * 70 / totalSteps);
            if (sent)
            {
                status.Report($"[Rongta] Comando enviado. Aguardando {newIp} (45s)...");
                if (await WaitForNewIpAsync(newIp, 45, status, progressPct, ct))
                { status.Report($"Sucesso! IP alterado para {newIp}."); progressPct.Report(100); return true; }
            }
            if (step < totalSteps) await Task.Delay(1200, ct);
        }

        // ── Tectoy Q4 (TCP 9100) — confirmado via pcap: 1F 1B 1F 22 [IP] ──────
        if (tryTectoyQ4)
        {
            step++;
            status.Report($"Tentativa {step}/{totalSteps} — Tectoy Q4 (TCP 9100)...");
            bool sent = await TryTectoyQ4TcpAsync(currentIp, newIp, status, ct);
            progressPct.Report(10 + step * 70 / totalSteps);
            if (sent)
            {
                status.Report($"[Tectoy Q4] Comando enviado. Aguardando {newIp} (60s)...");
                if (await WaitForNewIpAsync(newIp, 60, status, progressPct, ct))
                { status.Report($"Sucesso! IP alterado para {newIp}."); progressPct.Report(100); return true; }
                status.Report(
                    "[Tectoy Q4] Comando aceito mas impressora nao reapareceu automaticamente. " +
                    "Desligue e ligue a impressora para aplicar.");
            }
            if (step < totalSteps) await Task.Delay(1200, ct);
        }

        // ── ESC/POS GS ( N (TCP 9100) — Epson, Elgin e compatíveis ─────────────
        if (tryEscPosGsN)
        {
            step++;
            status.Report($"Tentativa {step}/{totalSteps} — ESC/POS GS(N (TCP 9100)...");
            bool sent = await TryEscPosNetConfigAsync(currentIp, newIp, mask, gateway, status, ct);
            progressPct.Report(10 + step * 70 / totalSteps);
            if (sent)
            {
                status.Report($"[ESC/POS GS(N] Comando enviado. Aguardando {newIp} (50s)...");
                if (await WaitForNewIpAsync(newIp, 50, status, progressPct, ct))
                { status.Report($"Sucesso! IP alterado para {newIp}."); progressPct.Report(100); return true; }
            }
            if (step < totalSteps) await Task.Delay(1200, ct);
        }

        // ── HTTP (porta 80/8080) — fallback para impressoras com interface web ──
        if (tryHttp)
        {
            step++;
            foreach (int hp in new[] { 80, 8080 })
            {
                ct.ThrowIfCancellationRequested();
                if (!await IsRealHttpServerAsync(currentIp, hp, ct)) continue;
                status.Report($"Tentativa {step}/{totalSteps} — HTTP porta {hp}...");
                progressPct.Report(10 + step * 70 / totalSteps);
                await TryHttpAsync(currentIp, hp, newIp, mask, gateway, ct);
                if (await WaitForNewIpAsync(newIp, 50, status, progressPct, ct))
                { status.Report($"Sucesso via HTTP! IP alterado para {newIp}."); progressPct.Report(100); return true; }
            }
        }

        progressPct.Report(100);
        status.Report(
            "Nao foi possivel confirmar a alteracao.\n" +
            "Desligue e ligue a impressora — o comando pode ter sido aceito e requer reinicializacao.\n" +
            "Se nao funcionar, use o utilitario 'PrinterTool JP-800' na aba Utilitarios.");
        return false;
    }

    // ── Tectoy Q4 (TCP 9100) — confirmado via captura de pacotes ───────────────
    // Payload: 1F 1B 1F 22 [IP 4 bytes]  (apenas IP, sem máscara/gateway)
    // Captura: 1F 1B 1F 22 C0 A8 64 67 = alterar para 192.168.100.103
    private static async Task<bool> TryTectoyQ4TcpAsync(
        string currentIp, string newIp,
        IProgress<string> status, CancellationToken ct)
    {
        byte[] ipB = ParseIp(newIp);
        if (ipB.Length != 4) return false;

        byte[] payload = [0x1F, 0x1B, 0x1F, 0x22, ..ipB];
        status.Report($"TCP {currentIp}:9100 -> {BitConverter.ToString(payload)}");

        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            await tcp.ConnectAsync(currentIp, 9100, cts.Token);
            var stream = tcp.GetStream();
            await stream.WriteAsync(payload, cts.Token);
            tcp.Close();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            status.Report($"Erro TCP Tectoy Q4: {ex.Message}");
            return false;
        }
        finally { tcp?.Dispose(); }
    }

    // ── ESC/POS GS ( N — configuração de rede (Epson, Tectoy, Elgin, EPPOS...) ──
    // Formato: 1D 28 4E [pL] [pH] 30 [fn] [dados]
    //   fn 00h = IP, fn 01h = máscara, fn 02h = gateway, fn 10h = aplicar/salvar
    private static async Task<bool> TryEscPosNetConfigAsync(
        string currentIp, string newIp, string mask, string gw,
        IProgress<string> status, CancellationToken ct)
    {
        byte[] ipB   = ParseIp(newIp);
        byte[] maskB = ParseIp(mask);
        byte[] gwB   = ParseIp(gw);
        if (ipB.Length != 4 || maskB.Length != 4 || gwB.Length != 4) return false;

        // GS ( N: 1D 28 4E pL pH 30 fn <4 bytes>
        byte[] cmdIp    = [0x1D, 0x28, 0x4E, 0x07, 0x00, 0x30, 0x00, ..ipB];
        byte[] cmdMask  = [0x1D, 0x28, 0x4E, 0x07, 0x00, 0x30, 0x01, ..maskB];
        byte[] cmdGw    = [0x1D, 0x28, 0x4E, 0x07, 0x00, 0x30, 0x02, ..gwB];
        byte[] cmdApply = [0x1D, 0x28, 0x4E, 0x02, 0x00, 0x30, 0x10]; // salvar e reiniciar

        var payload = (byte[])[..cmdIp, ..cmdMask, ..cmdGw, ..cmdApply];
        status.Report($"ESC/POS GS(N -> {BitConverter.ToString(payload)}");

        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            await tcp.ConnectAsync(currentIp, 9100, cts.Token);
            var stream = tcp.GetStream();
            await stream.WriteAsync(payload, cts.Token);
            await Task.Delay(800, ct); // dá tempo de processar antes de fechar
            tcp.Close();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            status.Report($"ESC/POS GS(N: {ex.Message}");
            return false;
        }
        finally { tcp?.Dispose(); }
    }

    // ── TCP porta 9100 — protocolo Rongta confirmado via pcap ──
    // Captura confirmou: PC envia dados e fecha a conexão IMEDIATAMENTE (sem delay).
    // Qualquer delay antes de fechar faz o firmware tratar o restante como impressão.
    private static async Task<bool> TryRongtaTcpAsync(
        string currentIp, string newIp, string mask, string gw,
        IProgress<string> status, CancellationToken ct)
    {
        byte[] ipB   = ParseIp(newIp);
        byte[] maskB = ParseIp(mask);
        byte[] gwB   = ParseIp(gw);

        if (ipB.Length != 4 || maskB.Length != 4 || gwB.Length != 4)
        {
            status.Report("Endereco IP invalido.");
            return false;
        }

        byte[] payload = [0x1F, 0x69, ..ipB, 0x1F, 0x25, 0x00, ..maskB, 0x1F, 0x25, 0x01, ..gwB];
        status.Report($"TCP {currentIp}:9100 -> {BitConverter.ToString(payload)}");

        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            await tcp.ConnectAsync(currentIp, 9100, cts.Token);
            var stream = tcp.GetStream();
            // Envia e fecha imediatamente — igual ao comportamento da PrinterTool no pcap
            await stream.WriteAsync(payload, cts.Token);
            tcp.Close(); // FIN imediato, sem delay
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            status.Report($"Erro TCP: {ex.Message}");
            return false;
        }
        finally { tcp?.Dispose(); }
    }

    // ── HTTP ──
    private static async Task TryHttpAsync(
        string ip, int port, string newIp, string mask, string gw,
        CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        string b = $"http://{ip}:{port}";
        string[] urls =
        [
            $"{b}/cgi-bin/netconfig.cgi?ip={Uri.EscapeDataString(newIp)}&mask={Uri.EscapeDataString(mask)}&gw={Uri.EscapeDataString(gw)}",
            $"{b}/config?ip={Uri.EscapeDataString(newIp)}&mask={Uri.EscapeDataString(mask)}&gw={Uri.EscapeDataString(gw)}",
        ];
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try { await client.GetAsync(url, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
    }

    private static async Task<bool> IsRealHttpServerAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(2000);
            await tcp.ConnectAsync(ip, port, cts.Token);
            var stream = tcp.GetStream();
            stream.WriteTimeout = 2000; stream.ReadTimeout = 2000;
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HEAD / HTTP/1.0\r\n\r\n"), cts.Token);
            var buf = new byte[8]; int read = 0;
            try { read = await stream.ReadAsync(buf.AsMemory(0, 8), cts.Token); } catch { }
            return read >= 5 && Encoding.ASCII.GetString(buf, 0, 5) == "HTTP/";
        }
        catch { return false; }
    }

    // ── Aguarda novo IP via ping OU porta 9100 (printers que bloqueiam ICMP) ──
    private static async Task<bool> WaitForNewIpAsync(
        string newIp, int seconds,
        IProgress<string> status,
        IProgress<double> progressPct,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        int elapsed  = 0;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000, ct);
            elapsed += 2;
            progressPct.Report(Math.Min(92, 40 + elapsed));
            if (await PingAsync(newIp, ct)) return true;
            if (await IsPort9100OpenAsync(newIp, ct)) return true;
            status.Report($"Aguardando {newIp}... ({elapsed}s/{seconds}s)");
        }
        return false;
    }

    private static async Task<bool> IsPort9100OpenAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1000);
            await tcp.ConnectAsync(ip, 9100, cts.Token);
            return true;
        }
        catch { return false; }
    }

    private static async Task<bool> PingAsync(string ip, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1500);
            return reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }

    private static string GetBroadcastAddress(string ip, string mask)
    {
        try
        {
            byte[] ipB   = ParseIp(ip);
            byte[] maskB = ParseIp(mask);
            if (ipB.Length != 4 || maskB.Length != 4) return ip;
            byte[] bcast = new byte[4];
            for (int i = 0; i < 4; i++)
                bcast[i] = (byte)(ipB[i] | ~maskB[i]);
            return string.Join(".", bcast);
        }
        catch { return ip; }
    }

    private static byte[] ParseIp(string ip)
    {
        try { return ip.Split('.').Select(byte.Parse).ToArray(); }
        catch { return []; }
    }

    private static byte[] ParseMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return [];
        try
        {
            var clean = mac.Replace(":", "").Replace("-", "");
            return Enumerable.Range(0, clean.Length / 2)
                             .Select(i => Convert.ToByte(clean.Substring(i * 2, 2), 16))
                             .ToArray();
        }
        catch { return []; }
    }

    private static string GuessGateway(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}.1" : "0.0.0.0";
    }

    // ── Impressão de teste direta via TCP 9100 ──────────────────────────────
    // Envia um payload ESC/POS para a impressora sem precisar de driver instalado.
    // Mesmo princípio da troca de IP: conecta, envia e fecha.
    public async Task<bool> SendDirectTestPageAsync(
        string ip, string deviceName,
        string mask = "", string gateway = "",
        CancellationToken ct = default)
    {
        var payload = BuildEscPosTestPage(ip, deviceName, mask, gateway);
        TcpClient? tcp = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Parse(ip), 9100, cts.Token);
            var stream = tcp.GetStream();
            await stream.WriteAsync(payload, cts.Token);
            await stream.FlushAsync(cts.Token);
            await Task.Delay(400, ct); // dá tempo de transmitir antes de fechar
            tcp.Close();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
        finally { tcp?.Dispose(); }
    }

    private static byte[] BuildEscPosTestPage(string ip, string deviceName, string mask, string gateway)
    {
        var buf = new List<byte>();

        void B(params byte[] b) => buf.AddRange(b);
        void T(string s) => buf.AddRange(Encoding.ASCII.GetBytes(s));

        const string Sep = "--------------------------------\n";

        // Inicializa impressora
        B(0x1B, 0x40);

        // Título — centralizado, negrito, duplo tamanho
        B(0x1B, 0x61, 0x01); // ESC a 1  — centralizar
        B(0x1B, 0x21, 0x38); // ESC ! 56 — bold + double height + double width
        T("TESTE DE IMPRESSAO\n");
        B(0x1B, 0x21, 0x00); // ESC ! 0  — normal
        B(0x1B, 0x61, 0x00); // ESC a 0  — alinhar esquerda

        B(0x1B, 0x61, 0x01); // ESC a 1 — centralizar
        T(Sep);
        T($"IP      : {ip}\n");

        var safeName = deviceName?.Replace('\n', ' ').Replace('\r', ' ') ?? "";
        if (!string.IsNullOrWhiteSpace(safeName) && safeName != "Impressora (broadcast)")
            T($"Nome    : {safeName[..Math.Min(safeName.Length, 28)]}\n");

        if (!string.IsNullOrWhiteSpace(mask))    T($"Mascara : {mask}\n");
        if (!string.IsNullOrWhiteSpace(gateway)) T($"Gateway : {gateway}\n");
        T($"Data    : {DateTime.Now:dd/MM/yyyy}\n");
        T($"Hora    : {DateTime.Now:HH:mm:ss}\n");
        T(Sep);
        B(0x1B, 0x61, 0x00); // ESC a 0 — esquerda

        // Mensagem de ação — centralizada, negrito
        B(0x1B, 0x61, 0x01);
        B(0x1B, 0x21, 0x08); // ESC ! 8 — negrito
        T("FAVOR ENCAMINHAR UMA FOTO\n");
        T("PARA O SUPORTE\n");
        B(0x1B, 0x21, 0x00); // ESC ! 0 — normal
        B(0x1B, 0x61, 0x00);

        T(Sep);

        // Rodapé centralizado
        B(0x1B, 0x61, 0x01);
        T("UP Tecnologias\n");
        B(0x1B, 0x61, 0x00);

        // Avança papel e corte parcial
        B(0x1B, 0x64, 0x05); // ESC d 5 — avançar 5 linhas
        B(0x1D, 0x56, 0x01); // GS  V 1 — corte parcial

        return [.. buf];
    }
}
