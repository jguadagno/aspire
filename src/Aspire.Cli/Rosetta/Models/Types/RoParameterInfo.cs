// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;

namespace Aspire.Cli.Rosetta.Models.Types;

internal sealed class RoParameterInfo
{
    private readonly Lazy<RoType> _parameterType;
    private readonly Parameter _parameter;
    private readonly string _parameterTypeName;
    private readonly MetadataReader _reader;
    private readonly AssemblyLoaderContext _assemblyLoaderContext;

    public RoParameterInfo(Parameter parameter, string parameterTypeName, RoMethodInfo declaringMethod, MetadataReader reader, AssemblyLoaderContext assemblyLoaderContext)
    {
        _parameter = parameter;
        _parameterTypeName = parameterTypeName;
        _reader = reader;
        _assemblyLoaderContext = assemblyLoaderContext;
        _parameterType = new(LoadParameterType);

        DeclaringMethod = declaringMethod;

        // Extract parameter name
        Name = parameter.Name.IsNil ? string.Empty : reader.GetString(parameter.Name);

        // TODO: Determine if parameter is optional from signature or attributes
        IsOptional = false;

        // TODO: Extract default values from parameter attributes or signature
        RawDefaultValue = null;
        DefaultValue = null;
    }

    public RoMethodInfo DeclaringMethod { get; }
    public RoType ParameterType => _parameterType.Value;
    public string Name { get; }
    public bool IsOptional { get; }
    public object? RawDefaultValue { get; }
    public object? DefaultValue { get; }

    private RoType LoadParameterType()
    {
        return _assemblyLoaderContext.LoadedAssemblies.Values
                    .Select(a => a.GetType(_parameterTypeName))
                    .FirstOrDefault(t => t is not null) ?? throw new InvalidOperationException($"Unknown type: {_parameterTypeName}");
    }
}
