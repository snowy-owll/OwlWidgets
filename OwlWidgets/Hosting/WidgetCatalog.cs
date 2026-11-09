using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Services;

namespace OwlWidgets.Hosting;

internal sealed class WidgetPackage
{
    public required string Folder { get; init; }

    public required string FolderName { get; init; }

    public required string DescriptorAssemblyPath { get; init; }

    public required string DescriptorTypeName { get; init; }

    public required string RuntimeAssemblyPath { get; init; }

    public required string RuntimeTypeName { get; init; }

    public IWidgetDescriptor Descriptor { get; set; } = null!;

    /// <summary>The descriptor icon, frozen; null when the widget has none or it did not load.</summary>
    public ImageSource? Icon { get; set; }
}

/// <summary>
/// The widget packages of this delivery. The snapshot lives as long as the manager process.
/// </summary>
internal interface IWidgetCatalog
{
    /// <summary>Empty until <see cref="Load"/>.</summary>
    IReadOnlyList<WidgetPackage> Packages { get; }

    /// <summary>Finds the packages and loads their descriptors into the manager process.</summary>
    void Load();
}

internal sealed class WidgetCatalog : IWidgetCatalog
{
    private const string DescriptorName = "OwlWidgets.Contracts.IWidgetDescriptor";
    private const string RuntimeName = "OwlWidgets.Contracts.IWidgetRuntime";

    private readonly AppPaths _paths;
    private readonly ILogger<WidgetCatalog> _log;

    public WidgetCatalog(AppPaths paths, ILogger<WidgetCatalog> log)
    {
        _paths = paths;
        _log = log;
    }

    public IReadOnlyList<WidgetPackage> Packages { get; private set; } = [];

    public void Load()
    {
        DescriptorResolve.Register();
        var shared = AppPaths.SharedAssemblies.ToDictionary(name => name, _paths.SharedAssemblyPath);
        Packages = Discover(_paths.WidgetsRoot, shared, _log);
    }

    /// <param name="shared">Name and path of each assembly the manager gives to widgets.</param>
    private static IReadOnlyList<WidgetPackage> Discover(string widgetsRoot, IReadOnlyDictionary<string, string> shared,
        ILogger log)
    {
        if (!Directory.Exists(widgetsRoot))
        {
            log.LogWarning("Widgets folder not found: {Path}", widgetsRoot);
            return [];
        }

        var folders = Directory.GetDirectories(widgetsRoot);
        var dlls = folders
            .SelectMany(folder => Directory.GetFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
            .ToArray();

        var found = new List<WidgetPackage>();
        using (var context = CreateLoadContext(shared, dlls))
        {
            foreach (var folder in folders)
            {
                try
                {
                    var package = Inspect(folder, context, log);
                    if (package != null)
                        found.Add(package);
                }
                catch (Exception exception)
                {
                    log.LogWarning(exception, "Package \"{Folder}\" was not inspected", Path.GetFileName(folder));
                }
            }
        }

        var packages = new List<WidgetPackage>();
        foreach (var package in found)
        {
            try
            {
                DescriptorResolve.AddFolder(package.Folder);
                var assembly = Assembly.LoadFrom(package.DescriptorAssemblyPath);
                var type = assembly.GetType(package.DescriptorTypeName, throwOnError: true)
                    ?? throw new InvalidOperationException("Descriptor type not found.");
                package.Descriptor = Activator.CreateInstance(type) as IWidgetDescriptor
                    ?? throw new InvalidOperationException("Descriptor type does not implement IWidgetDescriptor.");
                package.Icon = LoadIcon(package, log);
                packages.Add(package);
            }
            catch (Exception exception)
            {
                log.LogWarning(exception, "Package \"{Folder}\" was not loaded", package.FolderName);
            }
        }

        var valid = new List<WidgetPackage>();
        foreach (var group in packages.GroupBy(package => package.Descriptor.Id))
        {
            if (group.Count() == 1)
            {
                var package = group.First();
                var descriptor = package.Descriptor;
                log.LogInformation("Widget package \"{Folder}\": {Name} {Version} by {Author}, type {WidgetId}",
                    package.FolderName, descriptor.Name, descriptor.Version, descriptor.Author, descriptor.Id);
                valid.Add(package);
                continue;
            }

            var names = string.Join(", ", group.Select(package => package.FolderName));
            log.LogError("Type {WidgetId} found in several packages ({Folders}), these packages are skipped", group.Key, names);
        }

        log.LogInformation("Found {Count} widget packages in {Path}", valid.Count, widgetsRoot);

        return valid;
    }

    private static WidgetPackage? Inspect(string folder, MetadataLoadContext context, ILogger log)
    {
        var name = Path.GetFileName(folder);
        var descriptors = new List<(string Assembly, string Type)>();
        var runtimes = new List<(string Assembly, string Type)>();
        var contractsVersion = typeof(IWidgetDescriptor).Assembly.GetName().Version;

        foreach (var dll in Directory.GetFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromAssemblyPath(dll);
            }
            catch (Exception exception)
            {
                log.LogWarning(exception, "Assembly {Assembly} in \"{Folder}\" was not read", Path.GetFileName(dll), name);
                continue;
            }

            var referenced = assembly.GetReferencedAssemblies()
                .FirstOrDefault(reference => reference.Name == "OwlWidgets.Contracts");
            if (referenced?.Version != null && contractsVersion != null && referenced.Version > contractsVersion)
            {
                log.LogWarning("Package \"{Folder}\" references contracts {Referenced}, the manager provides {Provided}",
                    name, referenced.Version, contractsVersion);
                return null;
            }

            foreach (var type in SafeTypes(assembly))
            {
                if (!IsConcrete(type))
                    continue;
                if (Implements(type, DescriptorName))
                    descriptors.Add((dll, type.FullName ?? type.Name));
                if (Implements(type, RuntimeName))
                    runtimes.Add((dll, type.FullName ?? type.Name));
            }
        }

        if (descriptors.Count != 1 || runtimes.Count != 1)
        {
            if (descriptors.Count > 0 || runtimes.Count > 0)
                log.LogWarning("Package \"{Folder}\" skipped: {Descriptors} descriptors, {Runtimes} runtimes",
                    name, descriptors.Count, runtimes.Count);
            return null;
        }

        return new WidgetPackage
        {
            Folder = folder,
            FolderName = name,
            DescriptorAssemblyPath = descriptors[0].Assembly,
            DescriptorTypeName = descriptors[0].Type,
            RuntimeAssemblyPath = runtimes[0].Assembly,
            RuntimeTypeName = runtimes[0].Type
        };
    }

    /// <summary>
    /// The shared assemblies come from the manager folder, like at the real load: a descriptor and its settings
    /// screen may build on Common and UI.Common, which are not copied into the widget folder.
    /// </summary>
    private static MetadataLoadContext CreateLoadContext(IReadOnlyDictionary<string, string> shared, IEnumerable<string> dlls)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddDirectory(paths, RuntimeEnvironment.GetRuntimeDirectory(), overwrite: false);
        // Microsoft.NETCore.App contains a WindowsBase stub without WPF types.
        // The real desktop assemblies must replace it.
        AddDirectory(paths, Path.GetDirectoryName(typeof(System.Windows.Window).Assembly.Location), overwrite: true);
        foreach (var dll in dlls)
            paths.TryAdd(Path.GetFileNameWithoutExtension(dll), dll);
        foreach (var (name, path) in shared)
        {
            if (File.Exists(path))
                paths[name] = path;
        }

        return new MetadataLoadContext(new PathAssemblyResolver(paths.Values));
    }

    private static void AddDirectory(Dictionary<string, string> paths, string? directory, bool overwrite)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;
        foreach (var file in Directory.GetFiles(directory, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (overwrite)
                paths[name] = file;
            else
                paths.TryAdd(name, file);
        }
    }

    /// <summary>
    /// A broken icon does not make the package unusable: the widget is shown without it.
    /// </summary>
    private static ImageSource? LoadIcon(WidgetPackage package, ILogger log)
    {
        try
        {
            var icon = package.Descriptor.Icon;
            if (icon is { CanFreeze: true, IsFrozen: false })
                icon.Freeze();
            return icon;
        }
        catch (Exception exception)
        {
            log.LogWarning(exception, "Icon of package \"{Folder}\" was not loaded", package.FolderName);
            return null;
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null)!;
        }
    }

    private static bool IsConcrete(Type type) =>
        type.IsPublic && type.IsClass && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null;

    private static bool Implements(Type type, string interfaceName) =>
        type.GetInterfaces().Any(item => item.FullName == interfaceName);
}

internal static class DescriptorResolve
{
    private static readonly List<string> Folders = [];

    public static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

    public static void AddFolder(string folder)
    {
        if (!Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            Folders.Add(folder);
    }

    /// <summary>
    /// Satellite assemblies with translations of a descriptor lie in <c>{folder}/{culture}/{name}.resources.dll</c>.
    /// </summary>
    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name);
        var name = assemblyName.Name;
        if (name is null or "OwlWidgets.Contracts" or "OwlWidgets.Common" or "OwlWidgets.UI.Common" or "OwlWidgets.Protocol")
            return null;

        var culture = assemblyName.CultureName;
        foreach (var folder in Folders)
        {
            var path = string.IsNullOrEmpty(culture)
                ? Path.Combine(folder, name + ".dll")
                : Path.Combine(folder, culture, name + ".dll");
            if (File.Exists(path))
                return Assembly.LoadFrom(path);
        }

        return null;
    }
}
