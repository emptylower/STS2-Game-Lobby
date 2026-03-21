using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectNetUtil
{
    public static bool TryParseEndpoint(string raw, out string ip, out ushort port, out string error)
    {
        ip = string.Empty;
        port = LanConnectConstants.DefaultPort;
        error = string.Empty;

        string input = raw.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "请输入 IPv4/IPv6 地址，例如 192.168.1.20:33771 或 [2001:db8::2]:33771。";
            return false;
        }

        if (string.Equals(input, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            ip = "127.0.0.1";
            return true;
        }

        string ipPart = input;
        if (input.StartsWith("[", StringComparison.Ordinal))
        {
            int closeBracketIndex = input.IndexOf("]", StringComparison.Ordinal);
            if (closeBracketIndex <= 1)
            {
                error = "IPv6 地址格式无效。";
                return false;
            }

            ipPart = input[1..closeBracketIndex].Trim();
            string remain = input[(closeBracketIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(remain))
            {
                if (!remain.StartsWith(":", StringComparison.Ordinal))
                {
                    error = "端口格式无效，请输入 1-65535 之间的数字。";
                    return false;
                }

                string portPart = remain[1..].Trim();
                if (!ushort.TryParse(portPart, out port))
                {
                    error = "端口格式无效，请输入 1-65535 之间的数字。";
                    return false;
                }
            }
        }
        else
        {
            int colonCount = input.Count(static c => c == ':');
            if (colonCount == 1)
            {
                int colonIndex = input.LastIndexOf(':');
                ipPart = input[..colonIndex].Trim();
                string portPart = input[(colonIndex + 1)..].Trim();
                if (!ushort.TryParse(portPart, out port))
                {
                    error = "端口格式无效，请输入 1-65535 之间的数字。";
                    return false;
                }
            }
        }

        if (!IPAddress.TryParse(ipPart, out IPAddress? address)
            || (address.AddressFamily != AddressFamily.InterNetwork && address.AddressFamily != AddressFamily.InterNetworkV6))
        {
            error = "请输入有效的 IPv4/IPv6 地址。";
            return false;
        }

        ip = address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
        return true;
    }

    public static string GetPrimaryLanAddress()
    {
        IPAddress? bestMatch = GetLanAddresses().FirstOrDefault();

        return bestMatch?.ToString() ?? "127.0.0.1";
    }

    public static IReadOnlyList<string> GetLanAddressStrings()
    {
        return GetLanAddresses()
            .Select(static address => address.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string FormatEndpoint(string host, int port)
    {
        string trimmedHost = host.Trim();
        if (trimmedHost.Contains(':') &&
            !trimmedHost.StartsWith("[", StringComparison.Ordinal) &&
            !trimmedHost.EndsWith("]", StringComparison.Ordinal))
        {
            return $"[{trimmedHost}]:{port}";
        }

        return $"{trimmedHost}:{port}";
    }

    public static ulong GenerateClientNetId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        ulong value = BitConverter.ToUInt64(bytes);
        return value <= 1 ? value + 2 : value;
    }

    private static IEnumerable<IPAddress> GetLanAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(static nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(static nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
            .Select(static info => info.Address)
            .Where(static address =>
                address.AddressFamily == AddressFamily.InterNetwork
                || (address.AddressFamily == AddressFamily.InterNetworkV6
                    && !address.IsIPv6LinkLocal
                    && !address.IsIPv6Multicast))
            .OrderByDescending(ScoreAddress);
    }

    private static int ScoreAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] ipv6Bytes = address.GetAddressBytes();
            if ((ipv6Bytes[0] & 0xfe) == 0xfc) // fc00::/7 (ULA)
            {
                return 2;
            }

            return 1;
        }

        byte[] bytes = address.GetAddressBytes();
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return 5;
        }

        if (bytes[0] == 10)
        {
            return 4;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return 3;
        }

        return 0;
    }
}
