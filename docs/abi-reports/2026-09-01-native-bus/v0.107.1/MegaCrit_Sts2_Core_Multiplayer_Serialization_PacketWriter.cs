using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace MegaCrit.Sts2.Core.Multiplayer.Serialization;

public class PacketWriter
{
	private readonly byte[] _tempBuffer = new byte[16];

	private byte[] _stringBuffer = new byte[64];

	public int BitPosition { get; private set; }

	public int BytePosition => Mathf.CeilToInt((float)BitPosition / 8f);

	public byte[] Buffer { get; private set; } = new byte[1024];

	public bool WarnOnGrow { get; set; } = true;

	public void Reset()
	{
		BitPosition = 0;
	}

	public void WriteBool(bool val)
	{
		_tempBuffer[0] = (val ? ((byte)1) : ((byte)0));
		ResizeBufferIfNecessary(1);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, 1);
		BitPosition++;
	}

	public void WriteByte(byte val, int bits = 8)
	{
		_tempBuffer[0] = val;
		ResizeBufferIfNecessary(bits);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteBytes(byte[] bytes, int byteCount)
	{
		ResizeBufferIfNecessary(byteCount * 8);
		BitSerializationUtil.WriteBytes(bytes, Buffer, BitPosition, byteCount * 8);
		BitPosition += byteCount * 8;
	}

	public void WriteShort(short val, int bits = 16)
	{
		ResizeBufferIfNecessary(bits);
		BinaryPrimitives.WriteInt16LittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteUShort(ushort val, int bits = 16)
	{
		ResizeBufferIfNecessary(bits);
		BinaryPrimitives.WriteUInt16LittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteEnum<T>(T val) where T : struct, Enum
	{
		if (!typeof(int).IsAssignableFrom(Enum.GetUnderlyingType(typeof(T))))
		{
			throw new InvalidOperationException($"Trying to write enum type {typeof(T)} that is not assignable to int!");
		}
		int bits = Mathf.CeilToInt(Math.Log2(MaxEnumValueCache.Get<T>()) + 1.0);
		int val2 = Convert.ToInt32(val);
		WriteInt(val2, bits);
	}

	public void WriteInt(int val, int bits = 32)
	{
		ResizeBufferIfNecessary(bits);
		BinaryPrimitives.WriteInt32LittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteUInt(uint val, int bits = 32)
	{
		ResizeBufferIfNecessary(bits);
		BinaryPrimitives.WriteUInt32LittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteFloat(float val, QuantizeParams? quantizeParams = null)
	{
		if (quantizeParams.HasValue)
		{
			ResizeBufferIfNecessary(quantizeParams.Value.bits);
			uint val2 = Quantize(val, quantizeParams.Value.min, quantizeParams.Value.max, quantizeParams.Value.bits);
			WriteUInt(val2, quantizeParams.Value.bits);
		}
		else
		{
			ResizeBufferIfNecessary(32);
			BinaryPrimitives.WriteSingleLittleEndian(_tempBuffer.AsSpan(), val);
			BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, 32);
			BitPosition += 32;
		}
	}

	public void WriteLong(long val, int bits = 64)
	{
		ResizeBufferIfNecessary(64);
		BinaryPrimitives.WriteInt64LittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteULong(ulong val, int bits = 64)
	{
		ResizeBufferIfNecessary(64);
		BinaryPrimitives.WriteUInt64LittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, bits);
		BitPosition += bits;
	}

	public void WriteDouble(double val)
	{
		ResizeBufferIfNecessary(64);
		BinaryPrimitives.WriteDoubleLittleEndian(_tempBuffer.AsSpan(), val);
		BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, 64);
		BitPosition += 64;
	}

	public void WriteVector2(Vector2 val, QuantizeParams? quantizeParamsX = null, QuantizeParams? quantizeParamsY = null)
	{
		WriteFloat(val.X, quantizeParamsX);
		WriteFloat(val.Y, quantizeParamsY);
	}

	public void WriteList<T>(IReadOnlyList<T> list, int lengthBits = 32) where T : IPacketSerializable, new()
	{
		if ((ulong)list.Count >= (ulong)(1L << lengthBits))
		{
			throw new IndexOutOfRangeException($"List length {list.Count} is too large to fit in bit size {lengthBits}!");
		}
		WriteInt(list.Count, lengthBits);
		foreach (T item in list)
		{
			Write(item);
		}
	}

	public void WriteString(string str)
	{
		int byteCount = Encoding.UTF8.GetByteCount(str);
		if (_stringBuffer.Length < byteCount)
		{
			int num = (int)Math.Pow(2.0, Math.Ceiling(Math.Log2(byteCount)));
			_stringBuffer = new byte[num];
		}
		int bytes = Encoding.UTF8.GetBytes(str, _stringBuffer);
		WriteInt(bytes);
		WriteBytes(_stringBuffer, bytes);
	}

	public void Write<T>(T val) where T : IPacketSerializable
	{
		val.Serialize(this);
	}

	public void ZeroByteRemainder()
	{
		int num = 8 - BitPosition % 8;
		if (num != 8)
		{
			_tempBuffer[0] = 0;
			BitSerializationUtil.WriteBytes(_tempBuffer, Buffer, BitPosition, num);
		}
	}

	private void ResizeBufferIfNecessary(int bitsBeingWritten)
	{
		int num = Mathf.CeilToInt((float)(BitPosition + bitsBeingWritten) / 8f);
		int num2 = Buffer.Length;
		while (num >= num2)
		{
			num2 *= 2;
		}
		if (num2 != Buffer.Length)
		{
			byte[] array = new byte[num2];
			Array.Copy(Buffer, array, Buffer.Length);
			if (WarnOnGrow)
			{
				Log.Warn($"Warning: Packet writer is growing from {Buffer.Length} bytes to {array.Length} bytes!");
			}
			Buffer = array;
		}
	}

	public static uint Quantize(float value, float min, float max, int bitLength)
	{
		return (uint)((double)((value - min) / (max - min)) * Math.Pow(2.0, bitLength));
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
