using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Packaging;

// 来源：0.107.1（Steam public 分支）实机日志的 ReflectionTypeLoadException 报告。
// 游戏的 ModManager.TryLoadMod 对整个 mod 程序集调用 Assembly.GetTypes()；
// 只要有一个 TypeDef 在类型加载层面（基类 / 实现接口 / 字段类型 / 泛型实参）
// 引用了 0.107.1 不存在或签名漂移的游戏类型，整个 mod 都不加载。
public sealed class LanConnectGameAbiTypeLoadContractTests
{
    private static readonly string[] BlacklistedGameTypeFullNames =
    [
        "MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer",
        "MegaCrit.Sts2.Core.Entities.Multiplayer.LoadRunLobbyPlayer",
        "MegaCrit.Sts2.Core.Multiplayer.Game.INetClientGameService",
        "MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer",
        // 游戏 0.107.1 没有 PeerVersionInfo（0.110.x 引入，见 RELEASE_NOTES_V0.5.5_CLIENT_ZH.md）；
        // 类型加载层面同样不允许出现在字段 / 基类 / 接口签名上。
        "MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo",
    ];

    [Fact]
    public void No_typedef_references_blacklisted_game_types_at_type_load_level()
    {
        List<string> violations = CollectTypeLoadLevelViolations();

        Assert.True(
            violations.Count == 0,
            $"mod 程序集存在类型加载层面的黑名单游戏类型引用（{violations.Count} 处）：\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void No_typedef_implements_iclientconnectioninitializer()
    {
        List<string> violations = [];
        using PEReader peReader = OpenModPeReader();
        MetadataReader reader = peReader.GetMetadataReader();
        VisitAllTypeDefinitions(reader, (typeDefHandle, typeFullName) =>
        {
            TypeDefinition typeDefinition = reader.GetTypeDefinition(typeDefHandle);
            foreach (InterfaceImplementationHandle ifaceHandle in typeDefinition.GetInterfaceImplementations())
            {
                InterfaceImplementation implementation = reader.GetInterfaceImplementation(ifaceHandle);
                if (TryRenderEntity(reader, implementation.Interface, out string? rendered)
                    && rendered != null
                    && rendered.Contains("IClientConnectionInitializer", StringComparison.Ordinal))
                {
                    violations.Add($"{typeFullName} [iface] -> {rendered}");
                }
            }
        });

        Assert.True(
            violations.Count == 0,
            "不应有任何 TypeDef 实现游戏接口 IClientConnectionInitializer（两版本间签名漂移）：\n"
            + string.Join("\n", violations));
    }

    // 第二轮（计划第 9.4 节）：MemberRef 层面的 JIT 期回归保护。
    // 只要有人把 new NetHostGameService(PeerVersionInfo.LocalDefault()) / new NetClientGameService(PeerVersionInfo.LocalDefault())
    // 或直接的 PeerVersionInfo.LocalDefault() 调用写回源码，本测试立即失败。
    // 注意：LanConnectPeerVersionInfoPatches 通过 typeof(PeerVersionInfo) + AccessTools 取方法，
    // 只产生 TypeRef 与字符串常量，不会命中这里的 MemberRef 断言。
    private static readonly string[] VersionInfoDependentServiceFullNames =
    [
        "sts2::MegaCrit.Sts2.Core.Multiplayer.NetHostGameService",
        "sts2::MegaCrit.Sts2.Core.Multiplayer.NetClientGameService",
    ];

    private const string PeerVersionInfoMemberRefFullName =
        "sts2::MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo";

    [Fact]
    public void No_memberref_constructs_game_services_with_peer_version_info_or_calls_local_default()
    {
        List<string> violations = [];
        using PEReader peReader = OpenModPeReader();
        MetadataReader reader = peReader.GetMetadataReader();
        foreach (MemberReferenceHandle memberRefHandle in reader.MemberReferences)
        {
            MemberReference memberRef = reader.GetMemberReference(memberRefHandle);
            string memberName = reader.GetString(memberRef.Name);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            string parentFullName = RenderTypeReference(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent));
            if (memberName == ".ctor"
                && VersionInfoDependentServiceFullNames.Contains(parentFullName)
                && MemberSignatureReferencesPeerVersionInfo(reader, memberRef))
            {
                violations.Add($"{parentFullName}::ctor(PeerVersionInfo)");
            }

            if (memberName == "LocalDefault"
                && string.Equals(parentFullName, PeerVersionInfoMemberRefFullName, StringComparison.Ordinal))
            {
                violations.Add($"{parentFullName}::LocalDefault()");
            }
        }

        Assert.True(
            violations.Count == 0,
            "mod 程序集存在对游戏版本信息构造路径的 MemberRef 调用（0.107.1 上 JIT 即抛异常）：\n"
            + string.Join("\n", violations));
    }

    // 第三轮（2026-09-05 alpha.3 冒烟回归）：0.107.1 没有 MegaCrit.Sts2.Core.Modding.AssemblyInfo。
    // alpha.2 只在 LanConnectRegistryFingerprint.WriteEntry（仅 0.111 tail 路径可达）里引用它；
    // alpha.3 的启动自检把 AssemblyInfo.ModMap 写进了 Entry 阶段必经的 Run()，0.107.1 上 JIT 即抛
    // TypeLoadException，整个 mod 初始化失败。任何对该类型成员的直接 MemberRef 都不允许出现，
    // 一律经反射适配器访问。
    private static readonly string[] MemberRefBlacklistedGameTypeFullNames =
    [
        "sts2::MegaCrit.Sts2.Core.Modding.AssemblyInfo",
    ];

    [Fact]
    public void No_memberref_targets_game_types_absent_in_0_107_1()
    {
        List<string> violations = [];
        using PEReader peReader = OpenModPeReader();
        MetadataReader reader = peReader.GetMetadataReader();
        foreach (MemberReferenceHandle memberRefHandle in reader.MemberReferences)
        {
            MemberReference memberRef = reader.GetMemberReference(memberRefHandle);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            string parentFullName = RenderTypeReference(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent));
            if (MemberRefBlacklistedGameTypeFullNames.Contains(parentFullName))
            {
                violations.Add($"{parentFullName}::{reader.GetString(memberRef.Name)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "mod 程序集存在对 0.107.1 不存在的游戏类型成员的 MemberRef（JIT 期即抛 TypeLoadException）：\n"
            + string.Join("\n", violations));
    }

    private static bool MemberSignatureReferencesPeerVersionInfo(MetadataReader reader, MemberReference memberRef)
    {
        BlobReader signatureReader = reader.GetBlobReader(memberRef.Signature);
        SignatureDecoder<string, object?> decoder = new(
            ContractSignatureTypeProvider.Instance,
            reader,
            null);
        MethodSignature<string> signature = decoder.DecodeMethodSignature(ref signatureReader);
        return signature.ParameterTypes.Any(parameterType =>
            parameterType.Contains("PeerVersionInfo", StringComparison.Ordinal));
    }

    private static List<string> CollectTypeLoadLevelViolations()
    {
        List<string> violations = [];
        using PEReader peReader = OpenModPeReader();
        MetadataReader reader = peReader.GetMetadataReader();
        VisitAllTypeDefinitions(reader, (typeDefHandle, typeFullName) =>
        {
            TypeDefinition typeDefinition = reader.GetTypeDefinition(typeDefHandle);

            if (TryRenderEntity(reader, typeDefinition.BaseType, out string? baseType))
            {
                CheckAndAdd(violations, typeFullName, "base", baseType);
            }

            foreach (InterfaceImplementationHandle ifaceHandle in typeDefinition.GetInterfaceImplementations())
            {
                InterfaceImplementation implementation = reader.GetInterfaceImplementation(ifaceHandle);
                if (TryRenderEntity(reader, implementation.Interface, out string? ifaceName) && ifaceName != null)
                {
                    CheckAndAdd(violations, typeFullName, "iface", ifaceName);
                }
            }

            foreach (FieldDefinitionHandle fieldHandle in typeDefinition.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                string fieldName = reader.GetString(field.Name);
                // DecodeFieldSignature 会自行校验字段签名的调用约定头。
                BlobReader signatureReader = reader.GetBlobReader(field.Signature);
                SignatureDecoder<string, object?> signatureDecoder = new(
                    ContractSignatureTypeProvider.Instance,
                    reader,
                    new ContractGenericContext(reader, typeDefHandle));
                string signature = signatureDecoder.DecodeFieldSignature(ref signatureReader);
                CheckAndAdd(violations, typeFullName, $"field:{fieldName}", signature);
            }
        });

        return violations;
    }

    private static void CheckAndAdd(
        List<string> violations,
        string typeFullName,
        string slot,
        string? signature)
    {
        if (signature == null)
        {
            return;
        }

        foreach (string blacklisted in BlacklistedGameTypeFullNames)
        {
            if (signature.Contains(blacklisted, StringComparison.Ordinal))
            {
                violations.Add($"{typeFullName} [{slot}] -> {signature}");
                break;
            }
        }
    }

    private static void VisitAllTypeDefinitions(
        MetadataReader reader,
        Action<TypeDefinitionHandle, string> visit)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            visit(handle, RenderTypeDefinitionFullName(reader, handle));
        }
    }

    private static string RenderTypeDefinitionFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        return RenderTypeDefinitionFullName(reader, definition);
    }

    private static string RenderTypeDefinitionFullName(MetadataReader reader, TypeDefinition definition)
    {
        string name = reader.GetString(definition.Name);
        if (!definition.IsNested)
        {
            string ns = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        return $"{RenderTypeDefinitionFullName(reader, definition.GetDeclaringType())}/{name}";
    }

    private static bool TryRenderEntity(MetadataReader reader, EntityHandle entity, out string? rendered)
    {
        // 接口类型等没有基类的 TypeDef，其 BaseType 是 NIL 句柄（但 Kind 仍为 TypeDefinition）。
        if (entity.IsNil)
        {
            rendered = null;
            return false;
        }

        switch (entity.Kind)
        {
            case HandleKind.TypeReference:
                rendered = RenderTypeReference(reader, reader.GetTypeReference((TypeReferenceHandle)entity));
                return true;
            case HandleKind.TypeDefinition:
                rendered = RenderTypeDefinitionFullName(reader, (TypeDefinitionHandle)entity);
                return true;
            case HandleKind.TypeSpecification:
                rendered = reader
                    .GetTypeSpecification((TypeSpecificationHandle)entity)
                    .DecodeSignature(ContractSignatureTypeProvider.Instance, null);
                return true;
            default:
                rendered = null;
                return false;
        }
    }

    private static string RenderTypeReference(MetadataReader reader, TypeReference reference)
    {
        string assemblyName = ResolveScopeAssemblyName(reader, reference.ResolutionScope);
        string fullName = ResolveTypeReferenceFullName(reader, reference);
        return $"{assemblyName}::{fullName}";
    }

    private static string ResolveScopeAssemblyName(MetadataReader reader, EntityHandle scope)
    {
        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
                return reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
            case HandleKind.ModuleReference:
                return reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)scope).Name);
            case HandleKind.TypeReference:
            {
                // 嵌套类型的 ResolutionScope 是外层 TypeReference；继续向上找到程序集。
                return ResolveScopeAssemblyName(
                    reader,
                    reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope);
            }
            default:
                return "<module>";
        }
    }

    private static string ResolveTypeReferenceFullName(MetadataReader reader, TypeReference reference)
    {
        string name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            string outer = ResolveTypeReferenceFullName(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope));
            return $"{outer}/{name}";
        }

        string ns = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static PEReader OpenModPeReader()
    {
        string assemblyPath = typeof(Entry).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(assemblyPath), "mod 程序集缺少磁盘路径，无法进行元数据审计。");
        return new PEReader(File.OpenRead(assemblyPath));
    }

    private readonly record struct ContractGenericContext(MetadataReader Reader, TypeDefinitionHandle Type);

    private sealed class ContractSignatureTypeProvider : ISignatureTypeProvider<string, object?>
    {
        public static readonly ContractSignatureTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            return RenderTypeDefinitionFullName(reader, handle);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            return RenderTypeReference(reader, reader.GetTypeReference(handle));
        }

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            int rank = Math.Max(1, shape.Rank);
            return $"{elementType}[{new string(',', rank - 1)}]";
        }

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            if (genericContext is ContractGenericContext context)
            {
                TypeDefinition declaringType = context.Reader.GetTypeDefinition(context.Type);
                int cursor = 0;
                foreach (GenericParameterHandle parameterHandle in declaringType.GetGenericParameters())
                {
                    GenericParameter parameter = context.Reader.GetGenericParameter(parameterHandle);
                    if (cursor++ == index)
                    {
                        return context.Reader.GetString(parameter.Name);
                    }
                }
            }

            return $"!{index}";
        }

        public string GetGenericInstantiation(
            string genericType,
            ImmutableArray<string> typeArguments)
        {
            return $"{genericType}<{string.Join(",", typeArguments)}>";
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            List<string> parameters = signature.ParameterTypes.ToList();
            if (signature.Header.IsInstance)
            {
                parameters.Insert(0, "this");
            }

            return $"fnptr {signature.ReturnType} ({string.Join(",", parameters)})";
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(Instance, genericContext);
        }
    }
}
