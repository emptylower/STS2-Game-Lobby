using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectStartRunLobbyConstructorCompatibility
{
    internal static ConstructorInfo? Resolve(
        Type lobbyType,
        Type gameModeType,
        Type netGameServiceType,
        Type listenerType)
    {
        ConstructorInfo[] constructors = lobbyType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Type[] current = [gameModeType, typeof(bool), netGameServiceType, listenerType, typeof(int)];
        Type[] legacy = [gameModeType, netGameServiceType, listenerType, typeof(int)];

        // 0.113 added isCustom before netService. Match exact types so unrelated
        // overloads cannot win through the default binder's argument conversions.
        return constructors.FirstOrDefault(constructor =>
                   constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(current))
               ?? constructors.FirstOrDefault(constructor =>
                   constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(legacy));
    }
}
