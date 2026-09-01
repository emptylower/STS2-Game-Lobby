using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MegaCrit.Sts2.Core.Multiplayer.Serialization;

public class PacketReader
{
	private readonly byte[] _tempBuffer = new byte[16];

	private byte[] _stringBuffer = new byte[64];

	public int BitPosition { get; private set; }

	public byte[] Buffer { get; private set; }

	public void Reset(byte[] buffer)
	{
		BitPosition = 0;
		Buffer = buffer;
	}

	public bool ReadBool()
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, 1);
		BitPosition++;
		return _tempBuffer[0] != 0;
	}

	public byte ReadByte(int bits = 8)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return _tempBuffer[0];
	}

	public void ReadBytes(byte[] destinationBuffer, int byteCount)
	{
		BitSerializationUtil.ReadBits(Buffer, BitPosition, destinationBuffer, byteCount * 8);
		BitPosition += byteCount * 8;
	}

	public short ReadShort(int bits = 16)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return BinaryPrimitives.ReadInt16LittleEndian(_tempBuffer.AsSpan());
	}

	public ushort ReadUShort(int bits = 16)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return BinaryPrimitives.ReadUInt16LittleEndian(_tempBuffer.AsSpan());
	}

	public T ReadEnum<T>() where T : struct, Enum
	{
		if (!typeof(int).IsAssignableFrom(Enum.GetUnderlyingType(typeof(T))))
		{
			throw new InvalidOperationException($"Trying to write enum type {typeof(T)} that is not assignable to int!");
		}
		int bits = Mathf.CeilToInt(Math.Log2(MaxEnumValueCache.Get<T>()) + 1.0);
		int value = ReadInt(bits);
		return (T)Enum.ToObject(typeof(T), value);
	}

	public int ReadInt(int bits = 32)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return BinaryPrimitives.ReadInt32LittleEndian(_tempBuffer.AsSpan());
	}

	public uint ReadUInt(int bits = 32)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return BinaryPrimitives.ReadUInt32LittleEndian(_tempBuffer.AsSpan());
	}

	public float ReadFloat(QuantizeParams? quantizeParams = null)
	{
		Array.Clear(_tempBuffer);
		if (quantizeParams.HasValue)
		{
			uint value = ReadUInt(quantizeParams.Value.bits);
			return Unquantize(value, quantizeParams.Value.min, quantizeParams.Value.max, quantizeParams.Value.bits);
		}
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, 32);
		BitPosition += 32;
		return BinaryPrimitives.ReadSingleLittleEndian(_tempBuffer.AsSpan());
	}

	public long ReadLong(int bits = 64)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return BinaryPrimitives.ReadInt64LittleEndian(_tempBuffer.AsSpan());
	}

	public ulong ReadULong(int bits = 64)
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, bits);
		BitPosition += bits;
		return BinaryPrimitives.ReadUInt64LittleEndian(_tempBuffer.AsSpan());
	}

	public double ReadDouble()
	{
		Array.Clear(_tempBuffer);
		BitSerializationUtil.ReadBits(Buffer, BitPosition, _tempBuffer, 64);
		BitPosition += 64;
		return BinaryPrimitives.ReadDoubleLittleEndian(_tempBuffer.AsSpan());
	}

	public Vector2 ReadVector2(QuantizeParams? quantizeParamsX = null, QuantizeParams? quantizeParamsY = null)
	{
		float x = ReadFloat(quantizeParamsX);
		float y = ReadFloat(quantizeParamsY);
		return new Vector2(x, y);
	}

	public List<T> ReadList<T>(int lengthBits = 32) where T : IPacketSerializable, new()
	{
		List<T> list = new List<T>();
		int num = ReadInt(lengthBits);
		for (int i = 0; i < num; i++)
		{
			list.Add(Read<T>());
		}
		return list;
	}

	public string ReadString()
	{
		int num = ReadInt();
		if (_stringBuffer.Length < num)
		{
			int num2 = (int)Math.Pow(2.0, Math.Ceiling(Math.Log2(num)));
			_stringBuffer = new byte[num2];
		}
		ReadBytes(_stringBuffer, num);
		return Encoding.UTF8.GetString(_stringBuffer, 0, num);
	}

	public T Read<T>() where T : IPacketSerializable, new()
	{
		T result = new T();
		result.Deserialize(this);
		return result;
	}

	public static float Unquantize(uint value, float min, float max, int bitLength)
	{
		return (float)((double)value / Math.Pow(2.0, bitLength) * (double)(max - min) + (double)min);
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
