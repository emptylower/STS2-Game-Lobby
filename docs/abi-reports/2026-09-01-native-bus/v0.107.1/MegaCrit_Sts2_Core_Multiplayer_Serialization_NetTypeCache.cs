using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace MegaCrit.Sts2.Core.Multiplayer.Serialization;

/// <summary>
/// Bidirectional map of types to unique integer IDs, for use in serialization.
/// </summary>
/// <typeparam name="TBase">All classes which implement this type will automatically be mapped by this class.</typeparam>
public class NetTypeCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TBase> where TBase : class
{
	private readonly Dictionary<Type, int> _typeToId = new Dictionary<Type, int>();

	private readonly List<Type> _idToType;

	public NetTypeCache(List<Type> types)
	{
		types.Sort((Type t1, Type t2) => string.CompareOrdinal(t1.Name, t2.Name));
		_idToType = types;
		for (int num = 0; num < types.Count; num++)
		{
			Type type = types[num];
			if (!type.GetInterfaces().Contains<Type>(typeof(TBase)))
			{
				throw new InvalidOperationException($"Type {types[num]} does not implement interface {typeof(TBase)}!");
			}
			_typeToId[type] = num;
		}
	}

	/// <summary>
	/// Obtain the ID for a given type.
	/// </summary>
	/// <typeparam name="T">The type to map.</typeparam>
	/// <returns>The integer ID for the type T.</returns>
	/// <exception cref="T:System.InvalidOperationException">Thrown if T does not implement TBase.</exception>
	public int TypeToId<T>() where T : TBase
	{
		return TypeToId(typeof(T));
	}

	/// <summary>
	/// Obtain the ID for a given type.
	/// </summary>
	/// <param name="type">The type to map.</param>
	/// <returns>The integer ID for the given type.</returns>
	/// <exception cref="T:System.InvalidOperationException">Thrown if type does not implement TBase.</exception>
	public int TypeToId(Type type)
	{
		return _typeToId[type];
	}

	/// <summary>
	/// Obtain the type for a given integer ID.
	/// </summary>
	/// <param name="id">The ID to map to a type.</param>
	/// <param name="type">The type for the given ID.</param>
	/// <returns>True if a type was found for the ID, false otherwise.</returns>
	public bool TryGetTypeFromId(int id, out Type? type)
	{
		if (id < 0 || id >= _idToType.Count)
		{
			type = null;
			return false;
		}
		type = _idToType[id];
		return true;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
