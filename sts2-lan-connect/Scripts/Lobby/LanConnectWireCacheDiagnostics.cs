using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectWireCacheDiagnostics
{
    public static LanConnectWireCacheCaptureResult GetCurrentResult() =>
        RuntimeCacheHolder.Instance.GetCurrentResult();

    public static LanConnectWireCacheSnapshot GetRequiredCurrent() =>
        GetCurrentResult().GetRequiredSnapshot();

    public static void LogStartupSnapshot()
    {
        LogStartupSnapshot(
            RuntimeCacheHolder.Instance,
            GameCacheHolder.LogStartup,
            GameCacheHolder.LogStartupUnavailable);
    }

    internal static void LogStartupSnapshot(
        LanConnectWireCacheCaptureCache cache,
        Action<LanConnectWireCacheSnapshot> logAvailable,
        Action<LanConnectWireCacheCaptureResult> logUnavailable)
    {
        try
        {
            LanConnectWireCacheCaptureResult result = cache.GetCurrentResult();
            if (!cache.TryMarkStartupOutcomeLogged())
            {
                return;
            }

            if (result.IsAvailable)
            {
                logAvailable(result.Snapshot!);
            }
            else
            {
                logUnavailable(result);
            }
        }
        catch
        {
            // Startup diagnostics are optional and must never block lobby initialization.
        }
    }

    private static class RuntimeCacheHolder
    {
        // Keep production capture wiring lazy so loading diagnostics does not resolve sts2 types.
        static RuntimeCacheHolder()
        {
        }

        internal static readonly LanConnectWireCacheCaptureCache Instance = new(
            GameCacheHolder.Capture,
            GameCacheHolder.LogComputed,
            GameCacheHolder.LogFailure);
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
            FieldInfo categoryForwardMapField = RequireField("_categoryNameToNetIdMap", typeof(Dictionary<string, int>));
            FieldInfo entryForwardMapField = RequireField("_entryNameToNetIdMap", typeof(Dictionary<string, int>));
            FieldInfo epochForwardMapField = RequireField("_epochNameToNetIdMap", typeof(Dictionary<string, int>));
            FieldInfo propertyForwardMapField = RequireField("_propertyNameToNetIdMap", typeof(Dictionary<string, int>));
            PropertyInfo categoryBitSizeProperty = RequireProperty("CategoryIdBitSize", typeof(int));
            PropertyInfo entryBitSizeProperty = RequireProperty("EntryIdBitSize", typeof(int));
            PropertyInfo epochBitSizeProperty = RequireProperty("EpochIdBitSize", typeof(int));
            PropertyInfo propertyBitSizeProperty = RequireProperty("PropertyIdBitSize", typeof(int));
            PropertyInfo hashProperty = RequireProperty("Hash", typeof(uint));

            return LanConnectWireCacheSnapshotCapture.Capture(() => new LanConnectWireCacheState(
                ReadValue<bool>(initializedField),
                ReadTable(categoryTableField),
                ReadTable(entryTableField),
                ReadTable(epochTableField),
                ReadTable(propertyTableField),
                ReadForwardMap(categoryForwardMapField),
                ReadForwardMap(entryForwardMapField),
                ReadForwardMap(epochForwardMapField),
                ReadForwardMap(propertyForwardMapField),
                ReadValue<int>(categoryBitSizeProperty),
                ReadValue<int>(entryBitSizeProperty),
                ReadValue<int>(epochBitSizeProperty),
                ReadValue<int>(propertyBitSizeProperty),
                ReadValue<uint>(hashProperty)));
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

        internal static void LogStartupUnavailable(LanConnectWireCacheCaptureResult result)
        {
            Log.Error(
                $"sts2_lan_connect wire_cache startup_unavailable: reason={result.FailureReason}");
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

        private static KeyValuePair<string, int>[] ReadForwardMap(FieldInfo field)
        {
            if (field.GetValue(null) is not Dictionary<string, int> map)
            {
                throw new InvalidOperationException(
                    $"{CacheType.FullName}.{field.Name} returned null or a non-Dictionary<string, int> value.");
            }
            return map.ToArray();
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
