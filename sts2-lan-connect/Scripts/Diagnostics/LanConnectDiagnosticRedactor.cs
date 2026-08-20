using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sts2LanConnect.Scripts;

internal static partial class LanConnectDiagnosticRedactor
{
    private const int MaxTextLength = 16 * 1024;

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9+.-]*://[^\s\""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?<![\w])(?:[A-Za-z]:[\\/](?:[^\\/\s:]+[\\/])+[^\\/\s:]+|\\\\[^\\\s]+\\[^\\\s]+(?:\\[^\\\s]+)*|/(?:[^/\s:]+/)+[^/\s:]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathRegex();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"(?<![0-9A-Fa-f:])(?=[0-9A-Fa-f:]*:)[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}(?![0-9A-Fa-f:])", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"\b(?:[A-Fa-f0-9]{2}[:-]){5}[A-Fa-f0-9]{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"\b\d{12,20}\b", RegexOptions.CultureInvariant)]
    private static partial Regex PlatformIdRegex();

    [GeneratedRegex(@"(?i)\b(player(?:_?name|_?id)?|platform(?:_?id)?|machine(?:_?name)?|room(?:_?id|_?name)?|ticket|control(?:_?id)?|save(?:_?id)?|password|passwd|token|authorization|config|chat)\s*[:=][\s\S]*", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@":line\s+\d+", RegexOptions.CultureInvariant)]
    private static partial Regex SourceLineRegex();

    public static string RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        string redacted = UrlRegex().Replace(value, "<url>");
        redacted = AbsolutePathRegex().Replace(redacted, "<path>");
        redacted = MacRegex().Replace(redacted, "<mac>");
        redacted = Ipv4Regex().Replace(redacted, "<ip>");
        redacted = Ipv6Regex().Replace(redacted, "<ip>");
        redacted = PlatformIdRegex().Replace(redacted, "<platform_id>");
        redacted = SensitiveAssignmentRegex().Replace(redacted, static match => $"{match.Groups[1].Value}=<redacted>");
        return redacted.Length <= MaxTextLength ? redacted : redacted[..MaxTextLength] + "<truncated>";
    }

    public static LanConnectDiagnosticException DescribeException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string type = exception.GetType().FullName ?? exception.GetType().Name;
        string stack = RedactText(exception.StackTrace ?? string.Empty);
        stack = SourceLineRegex().Replace(stack, ":line <redacted>");

        StringBuilder identity = new();
        Exception? current = exception;
        for (int depth = 0; current != null && depth < 8; depth++, current = current.InnerException)
        {
            identity.Append(current.GetType().FullName)
                .Append('|')
                .Append(current.HResult.ToString("X8"))
                .Append('|');
        }
        identity.Append(stack);

        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))[..16].ToLowerInvariant();
        return new LanConnectDiagnosticException(type, exception.HResult, stack, fingerprint);
    }
}
