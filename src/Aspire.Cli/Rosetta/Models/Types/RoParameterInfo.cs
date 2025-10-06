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

    public RoParameterInfo(Parameter parameter, string parameterTypeName, RoMethodInfo declaringMethod)
    {
        _parameter = parameter;
        _parameterTypeName = parameterTypeName;
        _reader = declaringMethod.DeclaringType.DeclaringAssembly.Reader;
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
        return DeclaringMethod.DeclaringType.DeclaringAssembly.AssemblyLoaderContext.GetType(_parameterTypeName) ?? throw new ArgumentException($"Unknown type: {_parameterTypeName}");
    }

    public override string ToString()
    {
        return $"{Name}: {ParameterType}";
    }
}
