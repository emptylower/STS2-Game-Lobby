using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectLobbyHandshakeCompatibility
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags StaticMethods =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal sealed record PeerVersionSnapshot(
        string Version,
        ulong IdDatabaseHash,
        List<string> GameplayAffectingMods,
        object? RawVersionInfo);

    internal static PeerVersionSnapshot ReadInitialGameInfo(object initialMessage)
    {
        ArgumentNullException.ThrowIfNull(initialMessage);

        Type messageType = initialMessage.GetType();
        FieldInfo? versionInfoField = messageType.GetField("versionInfo", InstanceFields);
        object? rawVersionInfo = versionInfoField?.GetValue(initialMessage);
        object versionSource = rawVersionInfo ?? initialMessage;

        string version = ReadRequiredField<string>(versionSource, "version");
        ulong idDatabaseHash = Convert.ToUInt64(ReadRequiredField<object>(versionSource, "idDatabaseHash"));
        List<string> gameplayAffectingMods = ReadStringList(
            versionSource,
            "gameplayAffectingMods",
            "mods");

        return new PeerVersionSnapshot(
            version,
            idDatabaseHash,
            gameplayAffectingMods,
            rawVersionInfo);
    }

    internal static T AttachLocalVersionInfo<T>(T message)
        where T : struct
    {
        FieldInfo? versionInfoField = typeof(T).GetField("versionInfo", InstanceFields);
        if (versionInfoField == null)
        {
            return message;
        }

        object boxedMessage = message;
        versionInfoField.SetValue(boxedMessage, CreateLocalVersionInfo(versionInfoField.FieldType));
        return (T)boxedMessage;
    }

    internal static TExtraInfo PopulateFailureExtraInfo<TExtraInfo>(
        TExtraInfo extraInfo,
        PeerVersionSnapshot remoteInfo,
        List<string> missingModsOnLocal,
        List<string> missingModsOnHost)
        where TExtraInfo : class
    {
        ArgumentNullException.ThrowIfNull(extraInfo);
        ArgumentNullException.ThrowIfNull(remoteInfo);

        Type extraInfoType = extraInfo.GetType();
        FieldInfo? localInfoField = extraInfoType.GetField("localInfo", InstanceFields);
        FieldInfo? remoteInfoField = extraInfoType.GetField("remoteInfo", InstanceFields);

        if (localInfoField != null || remoteInfoField != null)
        {
            if (localInfoField == null || remoteInfoField == null ||
                remoteInfo.RawVersionInfo == null ||
                !remoteInfoField.FieldType.IsInstanceOfType(remoteInfo.RawVersionInfo))
            {
                throw new MissingFieldException(
                    extraInfoType.FullName,
                    "localInfo/remoteInfo compatible with InitialGameInfoMessage.versionInfo");
            }

            localInfoField.SetValue(extraInfo, CreateLocalVersionInfo(localInfoField.FieldType));
            remoteInfoField.SetValue(extraInfo, remoteInfo.RawVersionInfo);
            SetFieldIfPresent(extraInfo, "localIsHost", false);
        }

        SetFieldIfPresent(extraInfo, "missingModsOnLocal", missingModsOnLocal);
        SetFieldIfPresent(extraInfo, "missingModsOnHost", missingModsOnHost);
        return extraInfo;
    }

    private static object CreateLocalVersionInfo(Type versionInfoType)
    {
        MethodInfo? localDefaultMethod = versionInfoType.GetMethod(
            "LocalDefault",
            StaticMethods,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (localDefaultMethod == null || localDefaultMethod.ReturnType != versionInfoType)
        {
            throw new MissingMethodException(versionInfoType.FullName, "LocalDefault()");
        }

        return localDefaultMethod.Invoke(null, null)
            ?? throw new InvalidOperationException($"{versionInfoType.FullName}.LocalDefault() returned null.");
    }

    private static T ReadRequiredField<T>(object source, string fieldName)
    {
        FieldInfo? field = source.GetType().GetField(fieldName, InstanceFields);
        object? value = field?.GetValue(source);
        if (value is T typedValue)
        {
            return typedValue;
        }

        throw new MissingFieldException(source.GetType().FullName, fieldName);
    }

    private static List<string> ReadStringList(object source, params string[] fieldNames)
    {
        object? value = fieldNames
            .Select(fieldName => source.GetType().GetField(fieldName, InstanceFields)?.GetValue(source))
            .FirstOrDefault(static fieldValue => fieldValue != null);
        return value is IEnumerable<string> values
            ? values
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToList()
            : new List<string>();
    }

    private static void SetFieldIfPresent(object target, string fieldName, object value)
    {
        target.GetType().GetField(fieldName, InstanceFields)?.SetValue(target, value);
    }
}
