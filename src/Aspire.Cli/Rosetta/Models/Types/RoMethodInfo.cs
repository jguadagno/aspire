// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;

namespace Aspire.Cli.Rosetta.Models.Types;

[DebuggerDisplay("{ToString(),nq}")]
internal sealed class RoMethodInfo
{
    private readonly Lazy<RoType> _returnType;
    private readonly MetadataReader _reader;
    private readonly AssemblyLoaderContext _assemblyLoaderContext;
    private readonly Lazy<List<RoCustomAttributeData>> _customAttributes;

    public RoMethodInfo(MethodDefinition methodDefinition, RoType declaringType)
    {
        MethodDefinition = methodDefinition;
        DeclaringType = declaringType;

        // Note: assemblyLoaderContext will be used for resolving parameter/return types from other assemblies
        _assemblyLoaderContext = declaringType.DeclaringAssembly.AssemblyLoaderContext;
        _reader = declaringType.DeclaringAssembly.Reader;

        // Extract method name
        Name = _reader.GetString(methodDefinition.Name) ?? throw new InvalidOperationException("Invalid method, missing Name.");

        // Extract method attributes
        var attributes = methodDefinition.Attributes;
        IsStatic = (attributes & MethodAttributes.Static) != 0;

        // Get generic parameters
        var genericParams = methodDefinition.GetGenericParameters();
        IsGenericMethodDefinition = genericParams.Count > 0;

        // Load parameters
        Parameters = LoadParameters(methodDefinition);

        // Initialize lazy-loaded fields
        _returnType = new(LoadReturnType);

        var flags = methodDefinition.Attributes;
        IsStatic = (flags & MethodAttributes.Static) != 0;

        IsPublic = (flags & MethodAttributes.Public) != 0;

        // TODO: Load generic arguments for generic methods
        GenericArguments = [];

        // TODO: Get metadata token using proper API
        MetadataToken = 0;

        _customAttributes = new(LoadCustomAttributes);
    }

    public MethodDefinition MethodDefinition { get; }

    private List<RoParameterInfo> LoadParameters(MethodDefinition methodDefinition)
    {
        var parameters = new List<RoParameterInfo>();

        var sig = methodDefinition.DecodeSignature(new DisplayTypeProvider(_reader), genericContext: null);

        var i = 0;
        foreach (var paramHandle in methodDefinition.GetParameters())
        {
            var paramDef = _reader.GetParameter(paramHandle);

            if (paramDef.SequenceNumber == 0)
            {
                continue; // skip return parameter row
            }

            var type = sig.ParameterTypes[i];
            var parameter = new RoParameterInfo(paramDef, type, this);
            parameters.Add(parameter);
            i++;
        }

        return parameters;
    }

    public RoType ReturnType => _returnType.Value;
    public RoType DeclaringType { get; }
    public string Name { get; }
    public IReadOnlyList<RoParameterInfo> Parameters { get; }
    public bool IsStatic { get; init; }
    public bool IsPublic { get; init; }
    public IReadOnlyList<RoType> GenericArguments { get; }
    public bool IsGenericMethodDefinition { get; }
    public int MetadataToken { get; init; }
    public RoMethodInfo MakeGenericMethod(params RoType[] typeArguments) => throw new NotImplementedException();
    public IEnumerable<RoCustomAttributeData> GetCustomAttributes() => _customAttributes.Value;

    private List<RoCustomAttributeData> LoadCustomAttributes()
    {
        var list = new List<RoCustomAttributeData>();

        foreach (var attrHandle in MethodDefinition.GetCustomAttributes())
        {
            try
            {
                var customAttribute = _reader.GetCustomAttribute(attrHandle);
                RoType? attributeType = null;

                switch (customAttribute.Constructor.Kind)
                {
                    case HandleKind.MethodDefinition:
                        {
                            var ctorMethod = _reader.GetMethodDefinition((MethodDefinitionHandle)customAttribute.Constructor);
                            var declaringTypeHandle = ctorMethod.GetDeclaringType();
                            var typeDef = _reader.GetTypeDefinition(declaringTypeHandle);
                            var name = _reader.GetString(typeDef.Name);
                            var ns = typeDef.Namespace.IsNil ? string.Empty : _reader.GetString(typeDef.Namespace);
                            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                            attributeType = DeclaringType.DeclaringAssembly.GetType(fullName);
                            break;
                        }
                    case HandleKind.MemberReference:
                        {
                            var memberRef = _reader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                            var parent = memberRef.Parent;
                            var fullName = parent.GetTypeName(_reader);

                            if (fullName is not null)
                            {
                                attributeType = DeclaringType.DeclaringAssembly.GetType(fullName) ??
                                    _assemblyLoaderContext.LoadedAssemblies.Values
                                        .Select(a => a.GetType(fullName))
                                        .FirstOrDefault(t => t is not null);
                            }
                            break;
                        }
                }

                if (attributeType is not null)
                {
                    list.Add(new RoCustomAttributeData
                    {
                        AttributeType = attributeType,
                        NamedArguments = []
                    });
                }
            }
            catch
            {
            }
        }

        return list;
    }

    private RoType LoadReturnType()
    {
        var returnType = MethodDefinition.DecodeSignature(new DisplayTypeProvider(_reader), null).ReturnType;

        return DeclaringType.DeclaringAssembly.AssemblyLoaderContext.GetType(returnType) ?? throw new ArgumentException($"Unknown type: {returnType}");
    }

    public override string ToString()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(Name);

        if (GenericArguments.Count > 0)
        {
            builder.Append('<');
            builder.Append(string.Join(", ", GenericArguments.Select(t => t.ToString())));
            builder.Append('>');
        }
        builder.Append('(');
        builder.Append(string.Join(", ", Parameters.Select(p => p.ParameterType.Name)));
        builder.Append(')');
        return builder.ToString();
    }
}
