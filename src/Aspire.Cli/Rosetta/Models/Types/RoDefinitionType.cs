// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Reflection.Metadata;

namespace Aspire.Cli.Rosetta.Models.Types;

internal sealed class RoDefinitionType : RoType
{
    private readonly Lazy<RoType[]> _genericParameterConstraints;
    private readonly MetadataReader _reader;
    private readonly Lazy<RoType?> _baseType;
    private readonly Lazy<IReadOnlyList<RoType>> _interfaces;
    private readonly Lazy<bool> _isGenericType;
    private readonly Lazy<bool> _isEnum;
    private readonly Lazy<IReadOnlyList<RoMethodInfo>> _methods;
    private readonly Lazy<IReadOnlyList<RoType>> _genericArguments;
    private readonly Lazy<IReadOnlyList<RoType>> _genericTypeArguments;
    private readonly Lazy<bool> _containsGenericParameters;

    public RoDefinitionType(TypeDefinition typeDefinition, RoAssembly assembly)
        :base(assembly)
    {
        TypeDefinition = typeDefinition;
        _genericParameterConstraints = new(LoadGenericParameterConstraints);
        _reader = assembly.Reader;

        // Initialize lazy-loaded fields
        _baseType = new(LoadBaseType);
        _interfaces = new(LoadInterfaces);
        _isGenericType = new(LoadIsGenericType);
        _isEnum = new(LoadIsEnum);
        _methods = new(LoadMethods);
        _genericArguments = new(LoadGenericArguments);
        _genericTypeArguments = new(LoadGenericTypeArguments);
        _containsGenericParameters = new(LoadContainsGenericParameters);

        // Extract basic type information
        Name = _reader.GetString(typeDefinition.Name) ?? throw new InvalidOperationException("Invalid type, missing Name.");

        if (typeDefinition.IsNested)
        {
            var enclosingTypeHandle = typeDefinition.GetDeclaringType();
            var enclosingType = _reader.GetTypeDefinition(enclosingTypeHandle);
            var enclosingTypeName = _reader.GetString(enclosingType.Name) ?? throw new InvalidOperationException("Invalid enclosing type, missing Name.");

            Name = $"{enclosingTypeName}+{Name}";

            var namespaceName = enclosingType.Namespace.IsNil ? string.Empty : _reader.GetString(enclosingType.Namespace);
            FullName = string.IsNullOrEmpty(namespaceName) ? Name : $"{namespaceName}.{Name}";
        }
        else
        {
            var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : _reader.GetString(typeDefinition.Namespace);
            FullName = string.IsNullOrEmpty(namespaceName) ? Name : $"{namespaceName}.{Name}";
        }

        // Extract type attributes
        var attributes = typeDefinition.Attributes;
        var visibility = attributes & TypeAttributes.VisibilityMask;
        IsPublic = visibility == TypeAttributes.Public ||
                  visibility == TypeAttributes.NestedPublic;

        IsAbstract = (attributes & TypeAttributes.Abstract) != 0;
        IsSealed = (attributes & TypeAttributes.Sealed) != 0;
        IsInterface = (attributes & TypeAttributes.Interface) != 0;
        IsNested = typeDefinition.IsNested;
        IsTypeDefinition = true;

        // Simple properties that don't require complex resolution
        IsGenericParameter = false;

        // If this RoType represents a generic type definition (has generic parameters), its generic type definition is itself.
        GenericTypeDefinition = TypeDefinition.GetGenericParameters().Count > 0 ? this : null;
    }

    public override string Name { get; }
    public override string FullName { get; }
    public TypeDefinition TypeDefinition { get; }
    public override bool IsEnum => _isEnum.Value;

    public override bool IsGenericType => _isGenericType.Value;

    public override IReadOnlyList<RoType> GenericArguments => _genericArguments.Value;
    public override bool ContainsGenericParameters => _containsGenericParameters.Value;
    public override IReadOnlyList<RoType> Interfaces => _interfaces.Value;
    public override RoType? BaseType => _baseType.Value;
    public override IReadOnlyList<RoType> GenericTypeArguments => _genericTypeArguments.Value;
    public override RoType MakeGenericType(params RoType[] typeArguments) => throw new NotImplementedException();
    public override IReadOnlyList<RoMethodInfo> Methods => _methods.Value;
    public override RoMethodInfo? GetMethod(string name)
    {
        return Methods.FirstOrDefault(m => m.Name == name);
    }
    public override IEnumerable<RoCustomAttributeData> GetCustomAttributes() => throw new NotImplementedException();
    public override IReadOnlyList<RoType> GenericParameterConstraints => _genericParameterConstraints.Value;

    private RoType[] LoadGenericParameterConstraints()
    {
        return [];
        //MetadataReader reader = _reader;
        //GenericParameterConstraintHandleCollection handles = GenericParameter.GetConstraints();
        //int count = handles.Count;
        //if (count == 0)
        //{
        //    return Array.Empty<RoType>();
        //}

        //TypeContext typeContext = TypeContext;
        //RoType[] constraints = new RoType[count];
        //int index = 0;
        //foreach (GenericParameterConstraintHandle h in handles)
        //{
        //    RoType constraint = h.GetGenericParameterConstraint(reader).Type.ResolveTypeDefRefOrSpec(GetEcmaModule(), typeContext);

        //    // A constraint can have modifiers such as 'System.Runtime.InteropServices.UnmanagedType' which here is a 'System.ValueType'
        //    // modified type with a modreq for 'UnmanagedType' which would be obtainable through 'GetRequiredCustomModifiers()'.
        //    // However, for backwards compat, just return the unmodified type ('ValueType' in this case). This also prevents modified types from
        //    // "leaking" into an unmodified type hierarchy.
        //    if (constraint is RoModifiedType)
        //    {
        //        constraint = (RoType)constraint.UnderlyingSystemType;
        //    }

        //    constraints[index++] = constraint;
        //}
        //return constraints;
    }

    private RoType? LoadBaseType()
    {
        var baseTypeHandle = TypeDefinition.BaseType;

        // If there's no base type, return null (e.g., System.Object or interfaces)
        if (baseTypeHandle.IsNil)
        {
            return null;
        }

        // Resolve the handle to get the full type name
        string? baseTypeFullName;

        switch (baseTypeHandle.Kind)
        {
            case HandleKind.TypeDefinition:
                // Base type is defined in the same assembly
                var typeDefHandle = (TypeDefinitionHandle)baseTypeHandle;
                var typeDef = _reader.GetTypeDefinition(typeDefHandle);
                var name = _reader.GetString(typeDef.Name);
                var namespaceName = typeDef.Namespace.IsNil ? string.Empty : _reader.GetString(typeDef.Namespace);
                baseTypeFullName = string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
                break;

            case HandleKind.TypeReference:
                // Base type is defined in another assembly
                var typeRefHandle = (TypeReferenceHandle)baseTypeHandle;
                var typeRef = _reader.GetTypeReference(typeRefHandle);
                var refName = _reader.GetString(typeRef.Name);
                var refNamespace = typeRef.Namespace.IsNil ? string.Empty : _reader.GetString(typeRef.Namespace);
                baseTypeFullName = string.IsNullOrEmpty(refNamespace) ? refName : $"{refNamespace}.{refName}";
                break;

            case HandleKind.TypeSpecification:
                // Base type is a generic instantiation or other complex type
                // For now, we'll skip complex type specifications
                // TODO: Implement type specification resolution for generic base types

                return null;

            default:
                // Unknown handle type
                return null;
        }

        return Assembly.GetTypeDefinition(baseTypeFullName) ??
                Assembly.AssemblyLoaderContext.LoadedAssemblies.Values
                    .Select(a => a.GetTypeDefinition(baseTypeFullName))
                    .FirstOrDefault(t => t is not null);

    }

    private IReadOnlyList<RoType> LoadInterfaces()
    {
        // TODO: Implement interface resolution
        // This would involve getting interface implementations from TypeDefinition.GetInterfaceImplementations()
        // and resolving each interface handle to an RoType
        return [];
    }

    private bool LoadIsGenericType()
    {
        // Check if the type has generic parameters
        var genericParams = TypeDefinition.GetGenericParameters();
        return genericParams.Count > 0;
    }

    private bool LoadIsEnum()
    {
        // Check if the base type is System.Enum
        var baseType = BaseType;
        return baseType?.FullName == "System.Enum";
    }

    private List<RoMethodInfo> LoadMethods()
    {
        var methods = new List<RoMethodInfo>();

        // Memoize property accessor so we can skip them

        var accessorHandles = TypeDefinition.GetProperties()
            .Select(ph => _reader.GetPropertyDefinition(ph).GetAccessors())
            .SelectMany(a => new[] { a.Getter, a.Setter }.Where(h => !h.IsNil))
            .ToHashSet();

        foreach (var methodHandle in TypeDefinition.GetMethods())
        {
            var methodDefinition = _reader.GetMethodDefinition(methodHandle);

            // Extract method attributes to filter
            var attributes = methodDefinition.Attributes;
            var memberAccess = attributes & MethodAttributes.MemberAccessMask;
            var isPublic = memberAccess == MethodAttributes.Public;

            // Skip non-public methods for now (could be configurable later)
            if (!isPublic)
            {
                continue;
            }

            if (accessorHandles.Contains(methodHandle))
            {
                continue; // skip property accessors
            }

            // Create RoMethodInfo instance - all metadata extraction happens in constructor
            var method = new RoMethodInfo(methodDefinition, this);
            methods.Add(method);
        }

        return methods;
    }

    private IReadOnlyList<RoType> LoadGenericArguments()
    {
        //GenericParameterHandleCollection gps = TypeDefinition.GetGenericParameters();
        //if (gps.Count == 0)
        //{
        //    return Array.Empty<RoType>();
        //}

        //// A type parameter definition (T in List<T>, for example), not an actual constructed type.
        //// We can only tell what kind of generic parameter it is, where it’s declared, and what its constraints are.

        //RoType[] genericParameters = new RoType[gps.Count];
        //foreach (GenericParameterHandle gph in gps)
        //{
        //    MetadataReader reader = _reader;
        //    GenericParameter gp = reader.GetGenericParameter(gph);

        //    //var parameterElementType = AssemblyLoaderContext.LoadedAssemblies.Values
        //    //                        .Select(a => a.GetType(_parameterTypeName))
        //    //                        .FirstOrDefault(t => t is not null) ?? throw new InvalidOperationException($"Unknown type: {_parameterTypeName}");

        //    var typ = gp.Parent.Kind switch
        //    {
        //        HandleKind.TypeDefinition => new EcmaGenericTypeParameterType(gph, module),
        //        _ => throw new BadImageFormatException(), // Not a legal token type to be found in a GenericParameter.Parent record.
        //    };

        //    genericParameters[gp.GenericParameterPosition] = gp;
        //}

        // For now, assume generic types contain generic parameters
        // Full implementation would need to check if all generic parameters are resolved

        return [];
    }

    private IReadOnlyList<RoType> LoadGenericTypeArguments()
    {
        // TODO: Implement generic type arguments resolution
        // This would be used for generic type definitions
        return [];
    }

    private bool LoadContainsGenericParameters()
    {
        // Check if this type or any of its generic arguments contain unresolved generic parameters
        if (IsGenericType)
        {
            return true;
        }
        return false;
    }

    public override string ToString()
    {
        return FullName;
    }
}
