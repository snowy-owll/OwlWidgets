using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace OwlWidgets.WidgetHost;

internal sealed class WidgetLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _directory;

    public WidgetLoadContext(string runtimeAssemblyPath) : base(isCollectible: false)
    {
        _directory = Path.GetDirectoryName(runtimeAssemblyPath) ?? AppContext.BaseDirectory;
        _resolver = new AssemblyDependencyResolver(runtimeAssemblyPath);
        ResolvingUnmanagedDll += ResolveNative;
        // XAML of the widget resolves the assemblies of its xmlns with Assembly.Load in the default context,
        // which does not look into the widget folder: a control library of a NuGet package is not found there.
        // Load returns null for a shared assembly, so the default context does not ask itself again.
        Default.Resolving += (_, assemblyName) => Load(assemblyName);
    }

    /// <summary>
    /// Taken from the wrapper, not from the widget folder: one copy of a type for both sides,
    /// and one Material Design and one shared UI assembly for pack URIs resolved by name.
    /// </summary>
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "OwlWidgets.Contracts",
        "OwlWidgets.Common",
        "OwlWidgets.UI.Common",
        "MaterialDesignThemes.Wpf",
        "MaterialDesignColors",
        "Microsoft.Xaml.Behaviors"
    };

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name != null && SharedAssemblies.Contains(assemblyName.Name))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path == null)
        {
            // A satellite assembly with translations lies in a subfolder named after its culture.
            var candidate = string.IsNullOrEmpty(assemblyName.CultureName)
                ? Path.Combine(_directory, assemblyName.Name + ".dll")
                : Path.Combine(_directory, assemblyName.CultureName, assemblyName.Name + ".dll");
            if (File.Exists(candidate))
                path = candidate;
        }

        return path == null ? null : LoadFromAssemblyPath(path);
    }

    private IntPtr ResolveNative(Assembly assembly, string libraryName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(libraryName);
        if (path == null)
        {
            var fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? libraryName
                : libraryName + ".dll";
            var candidate = Path.Combine(_directory, fileName);
            if (File.Exists(candidate))
                path = candidate;
        }

        return path == null ? IntPtr.Zero : NativeLibrary.Load(path);
    }
}
