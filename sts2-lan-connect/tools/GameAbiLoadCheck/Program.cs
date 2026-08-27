// 真实加载检查工具：模拟游戏的 ModManager.TryLoadMod 行为（Assembly.GetTypes()）。
//
// 用法: dotnet run --project sts2-lan-connect/tools/GameAbiLoadCheck -- <mod dll 路径> <游戏 data 目录>
//
// 在自定义 AssemblyLoadContext 中加载 mod dll，依赖程序集按简单名在游戏 data 目录内解析
// （覆盖 sts2、0Harmony、Steamworks.NET、GodotSharp、GodotSharpEditor 及目录里其他 dll）。
// 只做类型枚举，不执行任何静态构造函数（GetTypes() 本身不会触发类型初始化）。
using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "usage: dotnet run --project sts2-lan-connect/tools/GameAbiLoadCheck -- <mod dll path> <game data dir>");
    return 2;
}

string modDllPath = Path.GetFullPath(args[0]);
string gameDataDir = Path.GetFullPath(args[1]);
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
    return 0;
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
