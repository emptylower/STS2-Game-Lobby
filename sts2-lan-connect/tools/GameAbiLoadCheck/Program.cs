// 真实加载检查工具：模拟游戏的 ModManager.TryLoadMod 行为（Assembly.GetTypes()）。
//
// 用法: dotnet run --project sts2-lan-connect/tools/GameAbiLoadCheck -- <mod dll 路径> <游戏 data 目录> [--members]
//
// 在自定义 AssemblyLoadContext 中加载 mod dll，依赖程序集按简单名在游戏 data 目录内解析
// （覆盖 sts2、0Harmony、Steamworks.NET、GodotSharp、GodotSharpEditor 及目录里其他 dll）。
// 默认只做类型枚举；--members 还解析直接引用的游戏成员，捕获 JIT 前的字段/方法签名漂移。
// 两种模式都不执行游戏代码或静态构造函数，也不覆盖通过反射字符串访问的成员。
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

bool checkMembers = args.Contains("--members", StringComparer.Ordinal);
string[] paths = args.Where(argument => argument != "--members").ToArray();
if (paths.Length != 2 || args.Length != paths.Length + (checkMembers ? 1 : 0))
{
    Console.Error.WriteLine(
        "usage: dotnet run --project sts2-lan-connect/tools/GameAbiLoadCheck -- <mod dll path> <game data dir> [--members]");
    return 2;
}

string modDllPath = Path.GetFullPath(paths[0]);
string gameDataDir = Path.GetFullPath(paths[1]);
if (!File.Exists(modDllPath))
{
    Console.Error.WriteLine($"mod dll not found: {modDllPath}");
    return 2;
}

if (!Directory.Exists(gameDataDir))
{
    Console.Error.WriteLine($"game data dir not found: {gameDataDir}");
    return 2;
}

Console.WriteLine($"mod dll:      {modDllPath}");
Console.WriteLine($"game data:    {gameDataDir}");

GameDataFolderLoadContext loadContext = new(gameDataDir);
try
{
    Assembly modAssembly = loadContext.LoadFromAssemblyPath(modDllPath);
    Type[] types = modAssembly.GetTypes();
    Console.WriteLine($"abi load OK: {types.Length} types loaded.");
    return checkMembers ? CheckGameMemberReferences(modAssembly, modDllPath) : 0;
}
catch (ReflectionTypeLoadException exception)
{
    Type[] presentTypes = exception.Types.Where(static type => type is not null).Select(static type => type!).ToArray();
    Console.Error.WriteLine($"abi load FAILED: only {presentTypes.Length}/{exception.Types.Length} types materialized.");
    foreach (Exception? loaderException in exception.LoaderExceptions)
    {
        if (loaderException != null)
        {
            Console.Error.WriteLine($"  - {DescribeLoaderException(loaderException)}");
        }
    }

    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"abi load FAILED unexpectedly: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

static string DescribeLoaderException(Exception exception) => exception switch
{
    TypeLoadException typeLoad => typeLoad.Message,
    FileNotFoundException fileNotFound => fileNotFound.Message,
    FileLoadException fileLoad => fileLoad.Message,
    BadImageFormatException badImage => badImage.Message,
    _ => $"{exception.GetType().Name}: {exception.Message}",
};

static int CheckGameMemberReferences(Assembly modAssembly, string modDllPath)
{
    using PEReader pe = new(File.OpenRead(modDllPath));
    MetadataReader metadata = pe.GetMetadataReader();
    Module modModule = modAssembly.ManifestModule;
    int checkedCount = 0;
    int failedCount = 0;
    foreach (MemberReferenceHandle handle in metadata.MemberReferences)
    {
        MemberReference reference = metadata.GetMemberReference(handle);
        EntityHandle declaringType = reference.Parent;
        if (declaringType.Kind == HandleKind.TypeSpecification)
        {
            declaringType = metadata.GetTypeSpecification((TypeSpecificationHandle)declaringType)
                .DecodeSignature(DeclaringTypeProvider.Instance, genericContext: null);
            if (declaringType.IsNil)
            {
                // A generic parameter as the declaring type has no statically known assembly.
                // Do not silently certify a reference whose game ownership cannot be determined.
                Console.Error.WriteLine($"  - 0x{MetadataTokens.GetToken(handle):X8}: unsupported declaring TypeSpec; cannot determine assembly.");
                failedCount++;
                continue;
            }
        }

        if (declaringType.Kind != HandleKind.TypeReference
            || GetScopeAssemblyName(metadata, (TypeReferenceHandle)declaringType) != "sts2")
        {
            continue;
        }

        checkedCount++;
        int token = MetadataTokens.GetToken(handle);
        TypeReference typeReference = metadata.GetTypeReference((TypeReferenceHandle)declaringType);
        string memberName = $"{metadata.GetString(typeReference.Namespace)}.{metadata.GetString(typeReference.Name)}::{metadata.GetString(reference.Name)}";
        try
        {
            // ResolveMember resolves the MemberRef's field/method signature against the actual
            // game dependency. GetTypes alone never resolves the bodies' member references.
            if (modModule.ResolveMember(token) == null)
            {
                throw new MissingMemberException("ResolveMember returned null.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or TypeLoadException
            or MissingMemberException or BadImageFormatException or FileNotFoundException or FileLoadException)
        {
            Console.Error.WriteLine($"  - 0x{token:X8} {memberName}: {exception.GetType().Name}: {exception.Message}");
            if (exception is ArgumentException or BadImageFormatException)
            {
                Console.Error.WriteLine("    Open generic references requiring caller type/method context are unsupported; this reference was NOT verified.");
            }

            failedCount++;
        }
    }

    Console.WriteLine($"abi members {(failedCount == 0 ? "OK" : "FAILED")}: {checkedCount} game references checked, {failedCount} failed/unverified.");
    Console.WriteLine("member coverage: direct sts2 MemberRefs only; reflection strings, MethodSpec constraints and executed/JIT behavior are not verified.");
    return failedCount == 0 ? 0 : 1;
}

static string? GetScopeAssemblyName(MetadataReader metadata, TypeReferenceHandle handle)
{
    EntityHandle scope = metadata.GetTypeReference(handle).ResolutionScope;
    return scope.Kind switch
    {
        HandleKind.AssemblyReference => metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
        HandleKind.TypeReference => GetScopeAssemblyName(metadata, (TypeReferenceHandle)scope),
        _ => null,
    };
}

// Keep only a TypeSpec's declaring generic definition. Its type arguments may reference
// sts2 without the declaring type belonging to sts2 (for example List<GameType>).
internal sealed class DeclaringTypeProvider : ISignatureTypeProvider<EntityHandle, object?>
{
    public static DeclaringTypeProvider Instance { get; } = new();

    public EntityHandle GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => handle;
    public EntityHandle GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => handle;
    public EntityHandle GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public EntityHandle GetGenericInstantiation(EntityHandle genericType, ImmutableArray<EntityHandle> typeArguments) => genericType;
    public EntityHandle GetArrayType(EntityHandle elementType, ArrayShape shape) => elementType;
    public EntityHandle GetSZArrayType(EntityHandle elementType) => elementType;
    public EntityHandle GetByReferenceType(EntityHandle elementType) => elementType;
    public EntityHandle GetPointerType(EntityHandle elementType) => elementType;
    public EntityHandle GetPinnedType(EntityHandle elementType) => elementType;
    public EntityHandle GetModifiedType(EntityHandle modifier, EntityHandle unmodifiedType, bool isRequired) => unmodifiedType;
    public EntityHandle GetGenericMethodParameter(object? genericContext, int index) => default;
    public EntityHandle GetGenericTypeParameter(object? genericContext, int index) => default;
    public EntityHandle GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
    public EntityHandle GetFunctionPointerType(MethodSignature<EntityHandle> signature) => default;
}

internal sealed class GameDataFolderLoadContext : AssemblyLoadContext
{
    public GameDataFolderLoadContext(string gameDataDirectory)
        : base(name: "sts2-game-data-dir", isCollectible: false)
    {
        GameDataDirectory = gameDataDirectory;
    }

    private string GameDataDirectory { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? simpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return null;
        }

        string candidate = Path.Combine(GameDataDirectory, simpleName + ".dll");
        if (!File.Exists(candidate))
        {
            // 让默认上下文处理 BCL 与工具自带依赖。
            return null;
        }

        try
        {
            return LoadFromAssemblyPath(candidate);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        // 元数据检查不需要加载原生库；返回零句柄让默认运行时路径处理。
        return nint.Zero;
    }
}
