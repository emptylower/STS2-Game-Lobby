using System;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectHostChannels
{
    public const string Lan = "lan";
    public const string Lobby = "lobby";

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is Lan or Lobby;
    }

    /// <summary>
    /// Missing/empty/unknown → lobby (compat). Unknown non-empty logs warning.
    /// </summary>
    public static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Lobby;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is Lan or Lobby)
        {
            return normalized;
        }

        // Prefer Console over GD.Print so pure Resolve stays safe under xUnit
        // (Godot native print aborts when no engine host is present).
        Console.WriteLine($"sts2_lan_connect host_channel: unknown value '{value}', treating as lobby");
        return Lobby;
    }

    public static string DescribePersisted(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<missing>" : value.Trim();
    }
}
