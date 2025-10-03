// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Reflection.Metadata;

namespace Aspire.Cli.Rosetta.Models.Types;

internal sealed class RoMethodInfo
{
    private readonly Lazy<RoType> _returnType;
    private readonly MethodDefinition _methodDefinition;
    private readonly MetadataReader _reader;
    private readonly AssemblyLoaderContext _assemblyLoaderContext;
    private readonly Lazy<List<RoCustomAttributeData>> _customAttributes;

    public RoMethodInfo(MethodDefinition methodDefinition, RoType declaringType, MetadataReader reader, AssemblyLoaderContext assemblyLoaderContext)
    {
        // Note: assemblyLoaderContext will be used for resolving parameter/return types from other assemblies
        _assemblyLoaderContext = assemblyLoaderContext;
        _methodDefinition = methodDefinition;
        DeclaringType = declaringType;
        _reader = reader;

        // Extract method name
        Name = reader.GetString(methodDefinition.Name) ?? throw new InvalidOperationException("Invalid method, missing Name.");

        // Extract method attributes
        var attributes = methodDefinition.Attributes;
        IsStatic = (attributes & MethodAttributes.Static) != 0;

        // Get generic parameters
        var genericParams = methodDefinition.GetGenericParameters();
        IsGenericMethodDefinition = genericParams.Count > 0;

        // Load parameters
        Parameters = LoadParameters(methodDefinition, reader, assemblyLoaderContext);

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

    private IReadOnlyList<RoParameterInfo> LoadParameters(MethodDefinition methodDefinition, MetadataReader reader, AssemblyLoaderContext assemblyLoaderContext)
    {
        var parameters = new List<RoParameterInfo>();

        var sig = methodDefinition.DecodeSignature(new DisplayTypeProvider(reader), genericContext: null);

        var i = 0;
        foreach (var paramHandle in methodDefinition.GetParameters())
        {
            var paramDef = reader.GetParameter(paramHandle);

            if (paramDef.SequenceNumber == 0)
            {
                continue; // skip return parameter row
            }

            var type = sig.ParameterTypes[i];
            var parameter = new RoParameterInfo(paramDef, type, this, reader, assemblyLoaderContext);
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

        foreach (var attrHandle in _methodDefinition.GetCustomAttributes())
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
                            attributeType = DeclaringType.Assembly.GetType(fullName);
                            break;
                        }
                    case HandleKind.MemberReference:
                        {
                            var memberRef = _reader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                            var parent = memberRef.Parent;
                            var fullName = parent.GetTypeName(_reader);

                            if (fullName is not null)
                            {
                                attributeType = DeclaringType.Assembly.GetType(fullName) ??
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
                        NamedArguments = Array.Empty<KeyValuePair<string, object>>()
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
        var sig = _methodDefinition.DecodeSignature(new DisplayTypeProvider(_reader), null);
        var returnType = sig.ReturnType;

        return DeclaringType.Assembly.GetType(returnType) ??
                                _assemblyLoaderContext.LoadedAssemblies.Values
                                    .Select(a => a.GetType(returnType))
                                    .FirstOrDefault(t => t is not null) ?? throw new InvalidOperationException($"Unknown type: {returnType}");
    }

    public override string ToString()
    {
        return Name;
    }
}
