// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Reflection.Metadata;
using static Aspire.Cli.Rosetta.Models.Types.SrmTypeShape;

namespace Aspire.Cli.Rosetta.Models.Types;

internal sealed class RoParameterInfo
{
    private readonly Lazy<RoType> _parameterType;
    private readonly Parameter _parameter;
    private readonly string _parameterTypeName;
    private readonly MetadataReader _reader;
    private readonly AssemblyLoaderContext _assemblyLoaderContext;

    public RoParameterInfo(Parameter parameter, string parameterTypeName, RoMethodInfo declaringMethod)
    {
        _parameter = parameter;
        _parameterTypeName = parameterTypeName;
        _reader = declaringMethod.DeclaringType.Assembly.Reader;
        _assemblyLoaderContext = declaringMethod.DeclaringType.Assembly.AssemblyLoaderContext;
        _parameterType = new(LoadParameterType);

        DeclaringMethod = declaringMethod;

        // Extract parameter name
        Name = parameter.Name.IsNil ? string.Empty : _reader.GetString(parameter.Name);

        IsOptional = (_parameter.Attributes & ParameterAttributes.Optional) != 0;

        if (TryGetParameterDefaultValue(_reader, parameter, out var defaultValue))
        {
            RawDefaultValue = defaultValue;
        }
        else
        {
            RawDefaultValue = IsOptional ? Missing.Value : DBNull.Value;
        }
    }

    public RoMethodInfo DeclaringMethod { get; }
    public RoType ParameterType => _parameterType.Value;
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the item can be omitted during in a method call.
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    /// Gets a value indicating the default value if the parameter has a default value.
    /// This property can be used in the reflection-only (RO) context since it won't depend on runtime values from
    /// other assemblies.
    /// </summary>
    public object? RawDefaultValue { get; }

    private RoType LoadParameterType()
    {
        var parameterElementType = _assemblyLoaderContext.LoadedAssemblies.Values
                                    .Select(a => a.GetTypeDefinition(_parameterTypeName))
                                    .FirstOrDefault(t => t is not null) ?? throw new InvalidOperationException($"Unknown type: {_parameterTypeName}");

        // SequenceNumber is 0 for the return type so is 1-based, but Parameter arrays are 0-base 
        return IsParameterArray(_reader, DeclaringMethod.MethodDefinition, _parameter.SequenceNumber - 1)
            ? new RoArrayType(parameterElementType, 1)
            : parameterElementType
            ;
    }

    public override string ToString()
    {
        return $"{Name}: {ParameterType}";
    }
}
