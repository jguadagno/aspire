// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Aspire.Cli.Rosetta.Models.Types;

namespace Aspire.Cli.Rosetta;

internal class AssemblyLoaderContext : IDisposable
{
    private List<IDisposable> _disposables = [];
    private bool _disposed;
    private readonly Dictionary<string, RoAssembly> _loadedAssemblies = [];

    public IReadOnlyDictionary<string, RoAssembly> LoadedAssemblies => _loadedAssemblies;

    // NOTE: Previously this code used MetadataLoadContext to load assemblies in an isolated context.
    // That API is relatively heavyweight and not friendly to NativeAOT trimming. We now rely on
    // System.Reflection.Metadata (PEReader) to inspect assemblies cheaply and then load the ones
    // we actually need directly. This loses full isolation (version conflicts are still possible)
    // but is acceptable for the CLI scenario where we only need surface information to drive code
    // generation / model creation.
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
