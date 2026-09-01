using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace MegaCrit.Sts2.Core.Multiplayer.Serialization;

/// <summary>
/// Used in the ModelIdSerializationCache to sort models before IDing them.
/// When mods are installed, the Sort function ensures that the types returned are sorted in a deterministic order
/// across all peers. It guards against several issues:
///  - Mods can be loaded in different sort orders
///  - Different mods can have content with the same ID
///  - The same mod can have content with the same ID
/// When in the base game, this is mostly for theoretical safety; ModelDb is likely to return models in the same order.
///
/// This class is generic so it can be used across several places.
/// </summary>
public static class ContentSorter<TIdType> where TIdType : IComparable<TIdType>
{
	public struct Item
	{
		public Type type;

		public TIdType id;

		public Mod? mod;

		public override string ToString()
		{
			return $"{type} {id} {mod?.manifest?.id}";
		}
	}

	/// <summary>
	/// Sort some content.
	/// </summary>
	/// <param name="types">The types to sort.</param>
	/// <param name="getId">The delegate which will be used to obtain the ID of each type.</param>
	/// <param name="affectsGameplayAtEnd">If true, types from mods that do not affect gameplay will be placed at the
	/// end of the resulting list. This is important so that players without those mods will still have a stable and
	/// deterministic sorting of IDs that will be sent over the network.</param>
	/// <returns></returns>
	public static List<Item> Sort(IEnumerable<Type> types, Func<Type, TIdType> getId, bool affectsGameplayAtEnd = true)
	{
		if (AssemblyInfo.ModMap == null)
		{
			throw new InvalidOperationException("ContentSorter called before AssemblyInfo was initialized. This is not allowed.");
		}
		List<Item> list = new List<Item>();
		foreach (Type type in types)
		{
			bool isBaseGame;
			Mod mod = AssemblyInfo.ModForType(type, out isBaseGame);
			if (mod == null && !isBaseGame)
			{
				Log.Error($"Attempting to register type {type} in assembly {type.Assembly}, but it is not associated with any mod! You may need to call {"ModManager"}.{"AssociateAssemblyWithMod"} to manually register the assembly. Type sorting may break because of this, causing errors in multiplayer.");
			}
			list.Add(new Item
			{
				type = type,
				id = getId(type),
				mod = mod
			});
		}
		Sort(list, affectsGameplayAtEnd);
		return list;
	}

	/// <summary>
	/// This is public for testing.
	/// </summary>
	public static List<Item> Sort(List<Item> items, bool affectsGameplayAtEnd = true)
	{
		items.Sort(delegate(Item p1, Item p2)
		{
			if (affectsGameplayAtEnd)
			{
				bool value = p1.mod?.manifest?.affectsGameplay ?? true;
				int num = (p2.mod?.manifest?.affectsGameplay ?? true).CompareTo(value);
				if (num != 0)
				{
					return num;
				}
			}
			ref TIdType id = ref p1.id;
			TIdType id2 = p2.id;
			int num2 = id.CompareTo(id2);
			if (num2 != 0)
			{
				return num2;
			}
			if (p1.mod != null && p2.mod == null)
			{
				return 1;
			}
			if (p1.mod == null && p2.mod != null)
			{
				return -1;
			}
			if (p1.mod == null && p2.mod == null)
			{
				return 0;
			}
			int num3 = string.CompareOrdinal(p1.mod.manifest.id, p2.mod.manifest.id);
			if (num3 != 0)
			{
				return num3;
			}
			int num4 = string.CompareOrdinal(p1.type.FullName, p2.type.FullName);
			if (num4 != 0)
			{
				return num4;
			}
			string fullName = p1.type.Assembly.FullName;
			string fullName2 = p2.type.Assembly.FullName;
			int num5 = string.CompareOrdinal(fullName, fullName2);
			return (num5 != 0) ? num5 : 0;
		});
		return items;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '9.1.0.7988')
