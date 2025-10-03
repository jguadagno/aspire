// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;

namespace Aspire.Cli.Rosetta.Models.Types;

internal sealed class RoType
{
    private readonly Lazy<RoType[]> _genericParameterConstraints;
    private readonly TypeDefinition _typeDefinition;
    private readonly MetadataReader _reader;
    private readonly AssemblyLoaderContext _assemblyLoaderContext;
    private readonly Lazy<RoType?> _baseType;
    private readonly Lazy<IReadOnlyList<RoType>> _interfaces;
    private readonly Lazy<bool> _isGenericType;
    private readonly Lazy<bool> _isEnum;
    private readonly Lazy<IReadOnlyList<RoMethodInfo>> _methods;
    private readonly Lazy<IReadOnlyList<RoType>> _genericArguments;
    private readonly Lazy<IReadOnlyList<RoType>> _genericTypeArguments;
    private readonly Lazy<bool> _containsGenericParameters;

    public RoType(TypeDefinition typeDefinition, RoAssembly assembly, MetadataReader reader, AssemblyLoaderContext assemblyLoaderContext)
    {
        _genericParameterConstraints = new(LoadGenericParameterConstraints);
        _typeDefinition = typeDefinition;
        _reader = reader;
        _assemblyLoaderContext = assemblyLoaderContext;
        Assembly = assembly;

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
        Name = reader.GetString(typeDefinition.Name) ?? throw new InvalidOperationException("Invalid type, missing Name.");

        // Get namespace and construct full name
        var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : reader.GetString(typeDefinition.Namespace);
        FullName = string.IsNullOrEmpty(namespaceName) ? Name : $"{namespaceName}.{Name}";

        // Extract type attributes
        var attributes = typeDefinition.Attributes;
        var visibility = attributes & System.Reflection.TypeAttributes.VisibilityMask;
        IsPublic = visibility == System.Reflection.TypeAttributes.Public ||
                  visibility == System.Reflection.TypeAttributes.NestedPublic;

        IsAbstract = (attributes & System.Reflection.TypeAttributes.Abstract) != 0;
        IsSealed = (attributes & System.Reflection.TypeAttributes.Sealed) != 0;
        IsInterface = (attributes & System.Reflection.TypeAttributes.Interface) != 0;
        IsNested = typeDefinition.IsNested;
        IsTypeDefinition = true;

        // Simple properties that don't require complex resolution
        IsByRef = false;
        IsPointer = false;
        IsArray = false;
        IsGenericParameter = false;
        ElementType = null;
        GenericTypeDefinition = null;
    }

    public RoAssembly Assembly { get; }
    public string Name { get; }
    public string FullName { get; }
    public bool IsAbstract { get; }
    public bool IsPublic { get; }
    public bool IsGenericType => _isGenericType.Value;
    public bool IsByRef { get; }
    public bool IsPointer { get; }
    public bool IsEnum => _isEnum.Value;
    public bool IsArray { get; }
    public bool IsTypeDefinition { get; }
    public bool IsSealed { get; }
    public bool IsNested { get; }
    public RoType? ElementType { get; }
    public RoType? GenericTypeDefinition { get; }
    public IReadOnlyList<RoType> GenericArguments => _genericArguments.Value;
    public bool IsGenericParameter { get; }
    public bool IsInterface { get; }
    public IEnumerable<string> GetEnumNames() => throw new NotImplementedException();
    public bool ContainsGenericParameters => _containsGenericParameters.Value;
    public IReadOnlyList<RoType> Interfaces => _interfaces.Value;
    public RoType? BaseType => _baseType.Value;
    public IReadOnlyList<RoType> GenericTypeArguments => _genericTypeArguments.Value;
    public RoType MakeGenericType(params RoType[] typeArguments) => throw new NotImplementedException();
    public IReadOnlyList<RoMethodInfo> Methods => _methods.Value;
    public RoMethodInfo? GetMethod(string name)
    {
        return Methods.FirstOrDefault(m => m.Name == name);
    }
    public IEnumerable<RoCustomAttributeData> GetCustomAttributes() => throw new NotImplementedException();
    public IReadOnlyList<RoType> GenericParameterConstraints => _genericParameterConstraints.Value;
    public bool IsAssignableTo(RoType targetType)
    {
        return targetType.IsAssignableFrom(this);
    }
    public bool IsAssignableFrom(RoType? c)
    {
        if (c == null)
        {
            return false;
        }

        // Check if the types are the same
        if (this == c)
        {
            return true;
        }

        // Check inheritance hierarchy
        var current = c;
        while (current != null)
        {
            if (this == current)
            {
                return true;
            }
            current = current.BaseType;
        }

        // Check if this type is an interface that c implements
        if (this.IsInterface)
        {
            return c.Interfaces.Contains(this) || c.Interfaces.Any(this.IsAssignableFrom);
        }

        // Check if c implements any interfaces that are assignable to this type
        foreach (var interfaceType in c.Interfaces)
        {
            if (this.IsAssignableFrom(interfaceType))
            {
                return true;
            }
        }

        return false;
    }

    private RoType[] LoadGenericParameterConstraints()
    {
        throw new NotImplementedException();
    }

    private RoType? LoadBaseType()
    {
        var baseTypeHandle = _typeDefinition.BaseType;

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
                return Assembly.GetType(baseTypeFullName);

            case HandleKind.TypeReference:
                // Base type is defined in another assembly
                var typeRefHandle = (TypeReferenceHandle)baseTypeHandle;
                var typeRef = _reader.GetTypeReference(typeRefHandle);
                var refName = _reader.GetString(typeRef.Name);
                var refNamespace = typeRef.Namespace.IsNil ? string.Empty : _reader.GetString(typeRef.Namespace);
                baseTypeFullName = string.IsNullOrEmpty(refNamespace) ? refName : $"{refNamespace}.{refName}";

                return Assembly.GetType(baseTypeFullName) ??
                                _assemblyLoaderContext.LoadedAssemblies.Values
                                    .Select(a => a.GetType(baseTypeFullName))
                                    .FirstOrDefault(t => t is not null);

                throw new NotImplementedException($"Cross-assembly type resolution not implemented: {baseTypeFullName}");

            case HandleKind.TypeSpecification:
                // Base type is a generic instantiation or other complex type
                // For now, we'll skip complex type specifications
                // TODO: Implement type specification resolution for generic base types

                return null;

            default:
                // Unknown handle type
                return null;
        }
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
        var genericParams = _typeDefinition.GetGenericParameters();
        return genericParams.Count > 0;
    }

    private bool LoadIsEnum()
    {
        // Check if the base type is System.Enum
        var baseType = BaseType;
        return baseType?.FullName == "System.Enum";
    }

    private IReadOnlyList<RoMethodInfo> LoadMethods()
    {
        var methods = new List<RoMethodInfo>();

        foreach (var methodHandle in _typeDefinition.GetMethods())
        {
            var methodDef = _reader.GetMethodDefinition(methodHandle);

            // Extract method attributes to filter
            var attributes = methodDef.Attributes;
            var memberAccess = attributes & System.Reflection.MethodAttributes.MemberAccessMask;
            var isPublic = memberAccess == System.Reflection.MethodAttributes.Public;

            // Skip non-public methods for now (could be configurable later)
            if (!isPublic)
            {
                continue;
            }

            // Create RoMethodInfo instance - all metadata extraction happens in constructor
            var method = new RoMethodInfo(methodDef, this, _reader, _assemblyLoaderContext);
            methods.Add(method);
        }

        return methods;
    }


    private IReadOnlyList<RoType> LoadGenericArguments()
    {
        // TODO: Implement generic arguments resolution
        // This would be used for constructed generic types
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
            // For now, assume generic types contain generic parameters
            // Full implementation would need to check if all generic parameters are resolved
            return true;
        }
        return false;
    }

    public override string ToString()
    {
        return FullName;
    }
}
