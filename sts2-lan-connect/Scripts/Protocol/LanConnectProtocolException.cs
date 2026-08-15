namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectProtocolException : Exception
{
    public LanConnectProtocolException(LanConnectProtocolFailure failure)
        : base(failure?.Detail ?? failure?.Code)
    {
        Failure = failure?.Validate() ?? throw new ArgumentNullException(nameof(failure));
    }

    public LanConnectProtocolException(LanConnectProtocolFailure failure, Exception innerException)
        : base(failure?.Detail ?? failure?.Code, innerException)
    {
        Failure = failure?.Validate() ?? throw new ArgumentNullException(nameof(failure));
    }

    public LanConnectProtocolFailure Failure { get; }
}
