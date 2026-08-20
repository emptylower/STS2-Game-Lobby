namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectDiagnosticException(
    string Type,
    int HResult,
    string Stack,
    string Fingerprint);
