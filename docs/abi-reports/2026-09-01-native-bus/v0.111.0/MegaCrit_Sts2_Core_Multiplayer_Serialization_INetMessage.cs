using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.SourceGeneration;

namespace MegaCrit.Sts2.Core.Multiplayer.Serialization;

[GenerateSubtypes]
public interface INetMessage : IPacketSerializable
{
	/// <summary>
	/// If set, when this message is sent to the host, it will be echoed to all clients.
	/// </summary>
	bool ShouldBroadcast { get; }

	/// <summary>
	/// Whether this message is transferred reliably or unreliably.
	/// </summary>
	NetTransferMode Mode { get; }

	/// <summary>
	/// What log level to use when logging info about this message.
	/// Almost all messages should be VeryDebug - only set this to Info for messages that are logged very infrequently.
	/// </summary>
	LogLevel LogLevel { get; }

	/// <summary>
	/// Determines whether a message is buffered when NetMessageBus.SetBufferMessages is called with true.
	/// Should almost always be true. Only set to false for messages that can always be properly handled when a
	/// connection is valid.
	/// </summary>
	bool ShouldBuffer { get; }
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
