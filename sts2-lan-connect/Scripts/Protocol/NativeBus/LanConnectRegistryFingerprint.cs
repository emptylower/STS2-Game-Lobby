using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// 消息注册表指纹（spec §3.4 第 1 层主门禁的客户端输入）。
///
/// 算法固定为 `sha256:v1:<64 位小写 hex>`：对 MessageTypes 全表按 id 升序，每项编码
/// [id:4 大端][modIdLen:1][modId UTF-8][flags:1(bit0=affectsGameplay)][asmLen:1]
/// [assemblyName UTF-8][nameLen:2 大端][typeFullName UTF-8] 后串联取 SHA-256。
/// 身份字段覆盖 ContentSorter 全部排序键来源，杜绝"同 FullName 不同 assembly"的同指纹
/// 不同表。基座类型（isBaseGame）无 owning mod：modId 为空、affectsGameplay 视为 true
/// （与 ContentSorter 的 `?? true` 一致）。C#/TypeScript 双端共用测试向量。
/// </summary>
internal static class LanConnectRegistryFingerprint
{
    internal const string Prefix = "sha256:v1:";

    /// <summary>
    /// 枚举全表并计算指纹；仅在游戏消息注册表与 AssemblyInfo 均已初始化后调用。
    /// 空表（注册表未初始化）或 AssemblyInfo 未初始化时抛出，而非产出格式合法但内容错误的指纹。
    /// </summary>
    internal static string Compute()
    {
        if (!LanConnectAssemblyInfoAdapter.IsInitialized)
        {
            throw new InvalidOperationException(
                "AssemblyInfo is not initialized; the fingerprint must not be computed before OneTimeInitialization.");
        }

        using MemoryStream stream = new();
        for (int id = 0; ; id++)
        {
            if (!MessageTypes.TryGetMessageType(id, out Type? type) || type == null)
            {
                break;
            }

            WriteEntry(stream, id, type);
        }

        if (stream.Length == 0)
        {
            throw new InvalidOperationException(
                "Message registry is empty; the fingerprint must not be computed before OneTimeInitialization.");
        }

        return Prefix + Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    /// <summary>指纹格式校验：sha256:v1: + 64 位小写 hex。</summary>
    internal static bool IsValidFormat(string? value) =>
        value != null
        && value.Length == Prefix.Length + 64
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static void WriteEntry(MemoryStream stream, int id, Type type)
    {
        Mod? mod = LanConnectAssemblyInfoAdapter.ModForType(type, out bool isBaseGame);
        string modId = mod?.manifest?.id ?? string.Empty;
        // null-mod（既非基座也无 owning mod）属异常输入：排序退化，交由服务端指纹门禁安全拒绝。
        bool affectsGameplay = mod?.manifest?.affectsGameplay ?? isBaseGame;
        string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        string typeFullName = type.FullName ?? type.Name;

        stream.WriteByte((byte)(id >> 24));
        stream.WriteByte((byte)(id >> 16));
        stream.WriteByte((byte)(id >> 8));
        stream.WriteByte((byte)(id & 0xff));
        WriteLengthPrefixed(stream, modId, 1, "modId");
        stream.WriteByte(affectsGameplay ? (byte)1 : (byte)0);
        WriteLengthPrefixed(stream, assemblyName, 1, "assemblyName");
        WriteLengthPrefixed(stream, typeFullName, 2, "typeFullName");
    }

    private static void WriteLengthPrefixed(MemoryStream stream, string value, int lengthBytes, string field)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int maxBytes = lengthBytes == 1 ? 255 : 65535;
        if (bytes.Length > maxBytes)
        {
            throw new InvalidDataException($"{field} exceeds {maxBytes} UTF-8 bytes.");
        }

        for (int shift = (lengthBytes - 1) * 8; shift >= 0; shift -= 8)
        {
            stream.WriteByte((byte)(bytes.Length >> shift));
        }

        stream.Write(bytes);
    }
}
