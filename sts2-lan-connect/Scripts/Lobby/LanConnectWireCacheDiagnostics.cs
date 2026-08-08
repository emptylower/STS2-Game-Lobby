using System.Reflection;
using System.Threading;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectWireCacheDiagnostics
{
    private static readonly object Sync = new();
    private static LanConnectWireCacheSnapshot? _cached;
    private static int _startupSnapshotLogged;

    public static LanConnectWireCacheSnapshot GetCurrent()
    {
        lock (Sync)
        {
            if (_cached != null)
            {
                return _cached;
            }

            try
            {
                _cached = GameCacheHolder.Capture();
                GameCacheHolder.LogComputed(_cached);
                return _cached;
            }
            catch (Exception ex)
            {
                GameCacheHolder.LogFailure(ex);
                throw new InvalidOperationException(
                    "WireCacheSignatureV1 could not capture ModelIdSerializationCache safely.",
                    ex);
            }
        }
    }

    public static void LogStartupSnapshot()
    {
        LanConnectWireCacheSnapshot snapshot = GetCurrent();
        if (Interlocked.Exchange(ref _startupSnapshotLogged, 1) != 0)
        {
            return;
        }

        GameCacheHolder.LogStartup(snapshot);
    }

    private static class GameCacheHolder
    {
        // The explicit cctor prevents beforefieldinit from resolving sts2 types until first use.
        static GameCacheHolder()
        {
        }

        private static readonly Type CacheType = typeof(ModelIdSerializationCache);

        internal static LanConnectWireCacheSnapshot Capture()
        {
            FieldInfo initializedField = RequireField("_initialized", typeof(bool));
            FieldInfo categoryTableField = RequireField("_netIdToCategoryNameMap", typeof(List<string>));
            FieldInfo entryTableField = RequireField("_netIdToEntryNameMap", typeof(List<string>));
            FieldInfo epochTableField = RequireField("_netIdToEpochNameMap", typeof(List<string>));
            FieldInfo propertyTableField = RequireField("_netIdToPropertyNameMap", typeof(List<string>));
            PropertyInfo categoryBitSizeProperty = RequireProperty("CategoryIdBitSize", typeof(int));
            PropertyInfo entryBitSizeProperty = RequireProperty("EntryIdBitSize", typeof(int));
            PropertyInfo epochBitSizeProperty = RequireProperty("EpochIdBitSize", typeof(int));
            PropertyInfo propertyBitSizeProperty = RequireProperty("PropertyIdBitSize", typeof(int));
            PropertyInfo hashProperty = RequireProperty("Hash", typeof(uint));

            if (!ReadValue<bool>(initializedField))
            {
                throw new InvalidOperationException(
                    "ModelIdSerializationCache is not initialized; refusing to compute a premature wire signature.");
            }

            string[] categoryTable = ReadTable(categoryTableField);
            string[] entryTable = ReadTable(entryTableField);
            string[] epochTable = ReadTable(epochTableField);
            string[] propertyTable = ReadTable(propertyTableField);
            int categoryBitSize = ReadValue<int>(categoryBitSizeProperty);
            int entryBitSize = ReadValue<int>(entryBitSizeProperty);
            int epochBitSize = ReadValue<int>(epochBitSizeProperty);
            int propertyBitSize = ReadValue<int>(propertyBitSizeProperty);
            uint vanillaHash = ReadValue<uint>(hashProperty);
            string signature = LanConnectWireCacheSignatureV1.Compute(
                categoryTable,
                entryTable,
                epochTable,
                propertyTable,
                categoryBitSize,
                entryBitSize,
                epochBitSize,
                propertyBitSize);

            return new LanConnectWireCacheSnapshot(
                signature,
                categoryBitSize,
                entryBitSize,
                epochBitSize,
                propertyBitSize,
                categoryTable.Length,
                entryTable.Length,
                epochTable.Length,
                propertyTable.Length,
                vanillaHash);
        }

        internal static void LogComputed(LanConnectWireCacheSnapshot snapshot)
        {
            Log.Info(
                $"sts2_lan_connect wire_cache computed: signature={snapshot.Signature}, " +
                $"categoryCount={snapshot.CategoryCount}, entryCount={snapshot.EntryCount}, " +
                $"epochCount={snapshot.EpochCount}, propertyCount={snapshot.PropertyCount}, " +
                $"vanillaHash={snapshot.VanillaHash}");
        }

        internal static void LogStartup(LanConnectWireCacheSnapshot snapshot)
        {
            Log.Info(
                $"sts2_lan_connect wire_cache startup_complete: signature={snapshot.Signature}, " +
                $"categoryBits={snapshot.CategoryIdBitSize}, entryBits={snapshot.EntryIdBitSize}, " +
                $"epochBits={snapshot.EpochIdBitSize}, propertyBits={snapshot.PropertyIdBitSize}");
        }

        internal static void LogFailure(Exception exception)
        {
            Log.Error($"sts2_lan_connect wire_cache capture failed: {exception}");
        }

        private static FieldInfo RequireField(string name, Type expectedType)
        {
            FieldInfo? field = CacheType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingMemberException(
                    CacheType.FullName,
                    $"required private static field {name}");
            }
            if (!field.IsPrivate || !field.IsStatic || field.FieldType != expectedType)
            {
                throw new InvalidOperationException(
                    $"{CacheType.FullName}.{name} must be a private static {expectedType.FullName} field; " +
                    $"actual visibility={field.Attributes & FieldAttributes.FieldAccessMask}, static={field.IsStatic}, " +
                    $"type={field.FieldType.FullName}.");
            }
            return field;
        }

        private static PropertyInfo RequireProperty(string name, Type expectedType)
        {
            PropertyInfo? property = CacheType.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
            MethodInfo? getter = property?.GetMethod;
            if (property == null || getter == null || !getter.IsPublic || !getter.IsStatic ||
                property.GetIndexParameters().Length != 0)
            {
                throw new MissingMemberException(
                    CacheType.FullName,
                    $"required public static property {name}");
            }
            if (property.PropertyType != expectedType)
            {
                throw new InvalidOperationException(
                    $"{CacheType.FullName}.{name} has type {property.PropertyType.FullName}; expected {expectedType.FullName}.");
            }
            return property;
        }

        private static string[] ReadTable(FieldInfo field)
        {
            if (field.GetValue(null) is not List<string> table)
            {
                throw new InvalidOperationException(
                    $"{CacheType.FullName}.{field.Name} returned null or a non-List<string> value.");
            }
            return table.ToArray();
        }

        private static T ReadValue<T>(MemberInfo member)
        {
            object? value = member switch
            {
                FieldInfo field => field.GetValue(null),
                PropertyInfo property => property.GetValue(null),
                _ => null
            };
            return value is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"{CacheType.FullName}.{member.Name} returned null or a non-{typeof(T).Name} value.");
        }
    }
}
