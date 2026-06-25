using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PrinterScanner.App.Models;

namespace PrinterScanner.App.Services;

public sealed class IpRangeService
{
    public IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask is not null)
                .Select(a => new NetworkInterfaceInfo
                {
                    InterfaceName = n.Name,
                    IpAddress     = a.Address.ToString(),
                    SubnetMask    = a.IPv4Mask!.ToString()
                }))
            .ToList();
    }

    public (IPAddress NetworkAddress, IPAddress BroadcastAddress, IPAddress SubnetMask) GetCurrentSubnet(string? specificLocalIp = null)
    {
        UnicastIPAddressInformation? nic;

        if (specificLocalIp is not null)
        {
            nic = NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a =>
                    a.Address.AddressFamily == AddressFamily.InterNetwork &&
                    a.Address.ToString() == specificLocalIp);
        }
        else
        {
            nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
        }

        if (nic is null || nic.IPv4Mask is null)
            throw new InvalidOperationException("Nenhuma interface de rede ativa foi encontrada.");

        var network   = ToUInt32(nic.Address) & ToUInt32(nic.IPv4Mask);
        var broadcast = network | ~ToUInt32(nic.IPv4Mask);

        return (ToIpAddress(network), ToIpAddress(broadcast), nic.IPv4Mask);
    }

    public IReadOnlyList<IPAddress> ExpandCurrentSubnet(string? specificLocalIp = null)
    {
        var subnet = GetCurrentSubnet(specificLocalIp);
        return ExpandRange(subnet.NetworkAddress, subnet.BroadcastAddress);
    }

    public IReadOnlyList<IPAddress> ExpandManualRange(string startIp, string endIp)
    {
        var start = ParseIpv4(startIp);
        var end   = ParseIpv4(endIp);

        if (ToUInt32(start) > ToUInt32(end))
            throw new InvalidOperationException("O IP inicial deve ser menor ou igual ao IP final.");

        return ExpandRange(start, end);
    }

    public IReadOnlyList<IPAddress> ExpandCidr(string cidrNotation)
    {
        var parts = cidrNotation.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            throw new InvalidOperationException("A notacao CIDR informada e invalida.");

        var baseIp    = ParseIpv4(parts[0]);
        var mask      = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network   = ToUInt32(baseIp) & mask;
        var broadcast = network | ~mask;
        return ExpandRange(ToIpAddress(network), ToIpAddress(broadcast));
    }

    public bool HasActiveNetworkInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork));
    }

    private static IReadOnlyList<IPAddress> ExpandRange(IPAddress start, IPAddress end)
    {
        var results    = new List<IPAddress>();
        var startValue = ToUInt32(start);
        var endValue   = ToUInt32(end);

        for (var current = startValue; current <= endValue; current++)
            results.Add(ToIpAddress(current));

        return results;
    }

    public static IPAddress ParseIpv4(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("O endereco IP informado e invalido.");

        return parsed;
    }

    public static uint ToUInt32(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    public static IPAddress ToIpAddress(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new IPAddress(bytes);
    }
}
