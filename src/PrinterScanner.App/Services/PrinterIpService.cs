using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace PrinterScanner.App.Services;

public sealed class PrinterIpService
{
    private static readonly Func<string, string, string, byte[]>[] CommandFormats =
    [
        (ip, mask, gw) => Enc($"IPADDR {ip}\nMASK {mask}\nGATE {gw}\n"),
        (ip, mask, gw) => Enc($"IPADDR {ip}\r\nMASK {mask}\r\nGATE {gw}\r\n"),
        (ip, mask, gw) => Enc($"IPADDR={ip}\r\nMASK={mask}\r\nGATE={gw}\r\n"),
        (ip, mask, gw) => Enc($"IPADDR={ip}\nMASK={mask}\nGATE={gw}\n"),
        (ip, mask, gw) => Enc($"IPADDR\n{ip}\nMASK\n{mask}\nGATE\n{gw}\n"),
    ];

    private static byte[] Enc(string s) => Encoding.ASCII.GetBytes(s);

    public async Task<bool> ChangeIpAsync(
        string currentIp,
        string newIp,
        string mask,
        string gateway,
        IProgress<string> status,
        IProgress<double> progressPct)
    {
        mask    = string.IsNullOrWhiteSpace(mask)    ? "255.255.255.0" : mask;
        gateway = string.IsNullOrWhiteSpace(gateway) ? GuessGateway(currentIp) : gateway;

        // Verifica conectividade antes de tentar
        status.Report($"Verificando conexao com {currentIp}:9100...");
        var (canConnect, connectErr) = await TryConnectAsync(currentIp);
        if (!canConnect)
        {
            status.Report($"Sem conexao com a impressora: {connectErr}");
            return false;
        }
        status.Report($"Conexao OK. Enviando comando de alteracao de IP...");

        for (var i = 0; i < CommandFormats.Length; i++)
        {
            var n = i + 1;
            progressPct.Report(10 + i * 12);

            var cmd = CommandFormats[i](newIp, mask, gateway);
            var cmdText = Encoding.ASCII.GetString(cmd).Replace("\r", "\\r").Replace("\n", "\\n");
            status.Report($"[{n}/{CommandFormats.Length}] Enviando: {cmdText}");

            var (sent, sendErr) = await TrySendAsync(currentIp, cmd);
            if (!sent)
            {
                status.Report($"[{n}] Erro ao enviar: {sendErr}");
                continue;
            }

            status.Report($"[{n}] Enviado. Aguardando impressora em {newIp} (30s max)...");
            progressPct.Report(40 + i * 8);

            var found = await WaitForNewIpAsync(newIp, 30, status, progressPct);
            if (found)
            {
                status.Report($"IP alterado com sucesso para {newIp}!");
                progressPct.Report(100);
                return true;
            }

            status.Report($"[{n}] Impressora nao respondeu no novo IP. Tentando proximo formato...");
        }

        status.Report($"Nenhum formato funcionou. Verifique se a impressora suporta alteracao de IP via porta 9100.");
        return false;
    }

    private static async Task<(bool ok, string error)> TryConnectAsync(string ip)
    {
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(ip, 9100);
            if (await Task.WhenAny(connect, Task.Delay(5000)) != connect)
                return (false, $"timeout ao conectar em {ip}:9100");
            await connect;
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(bool ok, string error)> TrySendAsync(string ip, byte[] command)
    {
        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            var connect = tcp.ConnectAsync(ip, 9100);
            if (await Task.WhenAny(connect, Task.Delay(5000)) != connect)
                return (false, "timeout na conexao");
            await connect;

            var stream = tcp.GetStream();
            stream.WriteTimeout = 4000;
            await stream.WriteAsync(command);
            await stream.FlushAsync();
            await Task.Delay(400);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            tcp?.Close();
        }
    }

    private static async Task<bool> WaitForNewIpAsync(
        string newIp, int seconds,
        IProgress<string> status,
        IProgress<double> progressPct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var elapsed  = 0;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000);
            elapsed += 2;
            progressPct.Report(Math.Min(95, 55 + elapsed));

            if (await IsReachableAsync(newIp))
                return true;

            status.Report($"Aguardando {newIp}... ({elapsed}s / {seconds}s)");
        }

        return false;
    }

    private static async Task<bool> IsReachableAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 800);
            if (reply.Status == IPStatus.Success) return true;
        }
        catch { }

        try
        {
            using var tcp = new TcpClient();
            var t = tcp.ConnectAsync(ip, 9100);
            return await Task.WhenAny(t, Task.Delay(900)) == t && !t.IsFaulted;
        }
        catch
        {
            return false;
        }
    }

    private static string GuessGateway(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}.1" : "0.0.0.0";
    }
}
