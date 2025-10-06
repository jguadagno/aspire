// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Cli.Rosetta.Models.Types;

internal abstract class RoType
{
    protected RoType(RoAssembly assembly)
    {
        DeclaringAssembly = assembly;
    }

    public RoAssembly DeclaringAssembly { get; }

    public abstract string Name { get; }
    public abstract string FullName { get; }
    public virtual bool IsAbstract { get; protected set; }
    public virtual bool IsPublic { get; protected set; }
    public virtual bool IsEnum { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether the current type represents an array.
    /// </summary>
    public virtual bool IsArray { get; }

    /// <summary>
    /// Retrieves the type of the elements contained within an array, pointer, or reference type.
    /// </summary>
    public virtual RoType? GetElementType() => null;

    public virtual int GetArrayRank() => throw new ArgumentException("Must be an array type.");

    /// <summary>
    /// Gets a value indicating whether the current type is a generic type, e.g. IResourceBuilder&lt;&gt; or IResourceBuilder&lt;Container%gt;
    /// </summary>
    public virtual bool IsGenericType { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether the current type represents a type definition rather than a type reference or
    /// constructed type, e.g., IResourceBuilder&lt;&gt;
    /// </summary>
    public virtual bool IsTypeDefinition { get; protected set; }

    public virtual bool IsSealed { get; protected set; }
    public virtual bool IsNested { get; protected set; }

    /// <summary>
    /// Gets the generic type definition for this type, if it represents a constructed generic type; otherwise, returns
    /// null.
    /// </summary>
    /// <remarks>Use this property to obtain the generic type definition from a constructed generic type, such
    /// as List&lt;int&gt; yielding List&lt;&gt;. If the type is not a constructed generic type, the property returns
    /// null.
    /// </remarks>
    public virtual RoType? GenericTypeDefinition { get; protected set; }

    public virtual IReadOnlyList<RoType> GenericArguments => [];
    public virtual bool IsGenericParameter { get; protected set; }
    public virtual bool IsInterface { get; protected set; }
    public virtual IEnumerable<string> GetEnumNames() => throw new NotImplementedException();
    public virtual bool ContainsGenericParameters { get; protected set; }
    public virtual IReadOnlyList<RoType> Interfaces => [];

    /// <summary>
    /// Gets the base type of the current type, if one exists.
    /// </summary>
    /// <remarks>
    /// This is null for interfaces and System.Object.
    /// </remarks>
    public virtual RoType? BaseType => null;
    public virtual IReadOnlyList<RoType> GenericTypeArguments => [];
    public virtual RoType MakeGenericType(params RoType[] typeArguments) => throw new NotImplementedException();

    /// <summary>
    /// Gets the collection of method metadata associated with the current type.
    /// </summary>
    /// <remarks>Only public methods. Doesn't include property accessors.
    /// The returned list provides read-only access to method information. The order of methods in
    /// the collection is not guaranteed and may vary depending on the underlying type system.
    /// </remarks>
    public virtual IReadOnlyList<RoMethodInfo> Methods => [];
    public virtual RoMethodInfo? GetMethod(string name) => null;
    public virtual IEnumerable<RoCustomAttributeData> GetCustomAttributes() => throw new NotImplementedException();
    public virtual IReadOnlyList<RoType> GenericParameterConstraints => [];

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
        if (IsInterface)
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

    public override string ToString()
    {
        return FullName;
    }
}
