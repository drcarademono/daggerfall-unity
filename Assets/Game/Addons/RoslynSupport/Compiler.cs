namespace DaggerfallWorkshop.Game.Utility.Roslyn;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics.CodeAnalysis;

public sealed class Compiler
{
    private static Dictionary<string, Assembly> DynamicAssemblyResolver = new();
    private static Compiler? _compiler;
    public static Compiler Instance => _compiler ??= new();

    private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs e) =>
        DynamicAssemblyResolver.TryGetValue(e.Name, out var assembly) ? assembly : null;

    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
    }

    private Compiler()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    public bool CompileSource(string assemblyName, string[] sources, [NotNullWhen(true)] out Assembly? assembly)
    {
        assembly = null;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));

        var syntaxTrees = sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => CSharpSyntaxTree.ParseText(s));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            assemblies,
            new(
                optimizationLevel: OptimizationLevel.Debug,
                outputKind: OutputKind.DynamicallyLinkedLibrary
            )
        );

        using var peStream = new System.IO.MemoryStream();
        using var pdbStream = new System.IO.MemoryStream();
        var result = compilation.Emit(peStream, pdbStream);

        if (!result.Success)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                // TODO: check if the error is sufficiently informative
                UnityEngine.Debug.LogFormat(UnityEngine.LogType.Error, UnityEngine.LogOption.NoStacktrace, null, "{0}", diagnostic.ToString());
            }
            return false;
        }

        peStream.Seek(0, System.IO.SeekOrigin.Begin);
        pdbStream.Seek(0, System.IO.SeekOrigin.Begin);

        assembly = Assembly.Load(peStream.ToArray(), pdbStream.ToArray());

        DynamicAssemblyResolver.Add(assemblyName, assembly);
        return assembly != null;
    }
}