using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace MegaCrit.Sts2.Core.Multiplayer;

public class NetMessageBus
{
	private delegate void AnonymizedMessageHandlerDelegate(INetMessage message, ulong senderId);

	private struct CallbackPair
	{
		public AnonymizedMessageHandlerDelegate handler;

		public object originalHandler;
	}

	private readonly PacketReader _reader;

	private readonly PacketWriter _writer;

	private readonly Logger _logger = new Logger("NetMessageBus", LogType.Network);

	private readonly Dictionary<Type, List<CallbackPair>> _messageHandlers = new Dictionary<Type, List<CallbackPair>>();

	private readonly List<CallbackPair> _cachedPairList = new List<CallbackPair>();

	private bool _isBufferingMessages;

	private readonly HashSet<byte> _warnedMessageTypes = new HashSet<byte>();

	private readonly List<(INetMessage, ulong)> _bufferedMessages = new List<(INetMessage, ulong)>();

	public NetMessageBus(PacketReader reader, PacketWriter writer)
	{
		_reader = reader;
		_writer = writer;
	}

	public byte[] SerializeMessage<T>(ulong senderId, T message, out int length) where T : INetMessage
	{
		_writer.Reset();
		_writer.WriteByte((byte)message.ToId());
		_writer.WriteULong(senderId);
		message.Serialize(_writer);
		length = _writer.BytePosition;
		return _writer.Buffer;
	}

	public bool TryDeserializeMessage(byte[] packetBytes, out INetMessage? message, out ulong? overrideSenderId)
	{
		overrideSenderId = null;
		message = null;
		_reader.Reset(packetBytes);
		byte b = _reader.ReadByte();
		if (!MessageTypes.TryGetMessageType(b, out Type type))
		{
			if (ModManager.IsRunningModded() && b >= MessageTypes.Count && !_warnedMessageTypes.Contains(b))
			{
				Log.Warn($"Received message with length {packetBytes.Length} and first byte {b} that is outside the bounds of our known messages ({MessageTypes.Count}). Since we are modded, we are assuming this is a message that does not affect gameplay and will not warn about this again.");
				_warnedMessageTypes.Add(b);
			}
			else
			{
				Log.Error($"Received message with length {packetBytes.Length} and first byte {b} that is not a valid message ID!");
			}
			return false;
		}
		overrideSenderId = _reader.ReadULong();
		message = (INetMessage)Activator.CreateInstance(type);
		message.Deserialize(_reader);
		return true;
	}

	public void SendMessageToAllHandlers(INetMessage message, ulong senderId)
	{
		if (_isBufferingMessages && message.ShouldBuffer)
		{
			_logger.Debug($"Received message of type {message.GetType()} but we are currently buffering messages.");
			_bufferedMessages.Add((message, senderId));
			return;
		}
		if (!_messageHandlers.TryGetValue(message.GetType(), out List<CallbackPair> value) || value.Count == 0)
		{
			Log.Error($"Received message of type {message.GetType()}, but no message handlers are registered for that type!");
			return;
		}
		_cachedPairList.Clear();
		_cachedPairList.AddRange(value);
		_logger.LogMessage(message.LogLevel, $"Received message {message}, sending to {_cachedPairList.Count} handlers", 0);
		foreach (CallbackPair cachedPair in _cachedPairList)
		{
			try
			{
				cachedPair.handler(message, senderId);
			}
			catch (Exception value2)
			{
				_logger.Error($"Exception encountered while processing message {message}: {value2}");
			}
		}
	}

	public void SetBufferMessages(bool bufferMessages)
	{
		if (_isBufferingMessages == bufferMessages)
		{
			return;
		}
		_isBufferingMessages = bufferMessages;
		if (bufferMessages)
		{
			_logger.Debug("NetMessageBus is starting to buffer messages.");
			return;
		}
		_logger.Debug($"NetMessageBus is releasing {_bufferedMessages.Count} buffered messages.");
		foreach (var (message, senderId) in _bufferedMessages)
		{
			SendMessageToAllHandlers(message, senderId);
		}
		_bufferedMessages.Clear();
	}

	public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
	{
		if (typeof(T) == typeof(INetMessage))
		{
			throw new InvalidOperationException("RegisterMessageHandler must be called with a concrete implementation of INetMessage!");
		}
		if (!_messageHandlers.TryGetValue(typeof(T), out List<CallbackPair> value))
		{
			value = new List<CallbackPair>();
			_messageHandlers[typeof(T)] = value;
		}
		CallbackPair item = new CallbackPair
		{
			handler = delegate(INetMessage message, ulong senderId)
			{
				handler((T)message, senderId);
			},
			originalHandler = handler
		};
		value.Add(item);
	}

	public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
	{
		if (typeof(T) == typeof(INetMessage))
		{
			throw new InvalidOperationException("UnregisterMessageHandler must be called with a concrete implementation of INetMessage!");
		}
		if (_messageHandlers.TryGetValue(typeof(T), out List<CallbackPair> value))
		{
			value.RemoveAll((CallbackPair p) => (Delegate?)(MessageHandlerDelegate<T>)p.originalHandler == (Delegate?)handler);
		}
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
