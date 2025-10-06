// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Aspire.Cli.Rosetta.Models.Types;

namespace Aspire.Cli.Rosetta;

// NOTE: Previously this code used MetadataLoadContext to load assemblies in an isolated context.
// That API is relatively heavyweight and not friendly to NativeAOT trimming. We now rely on
// System.Reflection.Metadata (PEReader) to inspect assemblies cheaply and then load the ones
// we actually need directly. This loses full isolation (version conflicts are still possible)
// but is acceptable for the CLI scenario where we only need surface information to drive code
// generation / model creation.

// Type specifications vs. type definitions vs. type references:
// Assemblies contain Type Definition, for instance System.Int32, or generic types like System.Action`1, System.Action`2.
// Arrays are represented as Type Specifications. So `System.String[]` is a Type Specification based on the Type Definition `System.String`.
// Type References are used to reference types defined in other assemblies. For instance, if assembly A defines type X and assembly B references A and defines type Y : X, then Y's base type is a Type Reference to X in assembly A.
// Assemblies reference each other via Assembly References. An assembly references all the assemblies it directly depends on. Hence if a Type Reference is encountered, the referenced assembly must be one of the Assembly References of the assembly being inspected.

// Type instantiations:
// System.Action`1 is a generic type definition. System.Action`1[System.String] is a generic type instantiation, represented as a Type Specification based on the Type Definition System.Action`1.

internal class AssemblyLoaderContext : IDisposable
{
    private List<IDisposable> _disposables = [];
    private bool _disposed;
    private readonly Dictionary<string, RoAssembly> _loadedAssemblies = [];

    public IReadOnlyDictionary<string, RoAssembly> LoadedAssemblies => _loadedAssemblies;

    public RoAssembly? LoadAssembly(string assemblyPath)
    {
        // Use PEReader first so that we exercise System.Reflection.Metadata directly (requirement)
        try
        {
            using var fs = File.OpenRead(assemblyPath);
            var peReader = new PEReader(fs, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                // Not a managed assembly; skip.
                throw new BadImageFormatException();
            }
            var reader = peReader.GetMetadataReader();
            var asmDef = reader.GetAssemblyDefinition();
            var asmName = reader.GetString(asmDef.Name);
            if (_loadedAssemblies.TryGetValue(asmName, out var existing))
            {
                peReader.Dispose();
                return existing;
            }

            RegisterForDisposal(peReader);

            return _loadedAssemblies[asmName] = new RoAssembly(asmDef, reader, this);
        }
        catch
        {
            return null;
        }
    }

    public bool TryGetType(EntityHandle entityHandle, MetadataReader reader, RoType? type)
    {
        type = null;

        if (entityHandle.IsNil)
        {
            return false;
        }

        // Resolve the handle to get the full type name
        string? fullName;

        switch (entityHandle.Kind)
        {
            case HandleKind.TypeDefinition:
                // Base type is defined in the same assembly
                var typeDefHandle = (TypeDefinitionHandle)entityHandle;
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                var name = reader.GetString(typeDef.Name);
                var namespaceName = typeDef.Namespace.IsNil ? string.Empty : reader.GetString(typeDef.Namespace);
                fullName = string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
                break;

            case HandleKind.TypeReference:
                // Base type is defined in another assembly
                var typeRefHandle = (TypeReferenceHandle)entityHandle;
                var typeRef = reader.GetTypeReference(typeRefHandle);
                var refName = reader.GetString(typeRef.Name);
                var refNamespace = typeRef.Namespace.IsNil ? string.Empty : reader.GetString(typeRef.Namespace);
                fullName = string.IsNullOrEmpty(refNamespace) ? refName : $"{refNamespace}.{refName}";
                break;

            case HandleKind.TypeSpecification:
                // Base type is a generic instantiation or other complex type (array, pointer, etc.)
                // TODO: Implement type specification resolution for generic base types

                return false;

            default:
                // Unknown handle type
                return false;
        }

        type = LoadedAssemblies.Values
                .Select(a => a.GetTypeDefinition(fullName))
                .FirstOrDefault(t => t is not null);

        return type is not null;
    }

    /// <summary>
    /// Adds an object to an internal list of objects to be disposed when the MetadataLoadContext is disposed.
    /// </summary>
    internal void RegisterForDisposal(IDisposable disposable) => _disposables.Add(disposable);

    private void DisposeInternal()
    {
        if (_disposed)
        {
            return;
        }

        // Dispose all PE readers. This releases any file locks on the underlying
        // assembly files.
        var disposables = _disposables;
        if (disposables != null)
        {
            _disposables = null!;

            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }

        _disposed = true;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        DisposeInternal();
    }

    ~AssemblyLoaderContext()
    {
        DisposeInternal();
    }
}
