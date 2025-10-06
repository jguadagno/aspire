// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Cli.Rosetta;
using Aspire.Cli.Rosetta.Models.Types;

namespace Aspire.Cli.Tests.Polyglot;

public class TypeResolutionTests
{
    [Fact]
    public void LoadsTypesByFullName()
    {
        using var loader = CreateAssemblyLoaderContext(out var testAssembly);

        var testMethodsType = testAssembly.GetTypeDefinition(typeof(TestMethods).FullName!);
        Assert.NotNull(testMethodsType);
        Assert.Equal(typeof(TestMethods).FullName, testMethodsType.FullName);
    }

    [Fact]
    public void ArrayTypesAreResolved()
    {
        using var loader = CreateAssemblyLoaderContext(out var testAssembly);

        var testMethodsType = testAssembly.GetTypeDefinition(typeof(TestMethods).FullName!);
        Assert.NotNull(testMethodsType);

        var methodC = testMethodsType.GetMethod(nameof(TestMethods.MethodC));

        Assert.NotNull(methodC);
        Assert.Equal("MethodC", methodC.Name);
        Assert.Single(methodC.Parameters);
        Assert.Equal("input", methodC.Parameters[0].Name);
        Assert.Equal("System.String", methodC.Parameters[0].ParameterType.FullName);

        var methodCReturnType = methodC.ReturnType;
        Assert.Equal("System.String[]", methodCReturnType.FullName);

        var methodD = testMethodsType.GetMethod(nameof(TestMethods.MethodD));
        Assert.NotNull(methodD);
        Assert.Equal("MethodD", methodD.Name);
        Assert.Single(methodD.Parameters);
        Assert.Equal("inputs", methodD.Parameters[0].Name);
        Assert.Equal("System.String[]", methodD.Parameters[0].ParameterType.FullName);

        var methodDReturnType = methodD.ReturnType;
        Assert.Equal("System.String", methodDReturnType.FullName);
    }

    [Fact]
    public void ReadsOptionalParameters()
    {
        using var loader = CreateAssemblyLoaderContext(out var testAssembly);

        var testMethodsType = testAssembly.GetTypeDefinition(typeof(TestMethods).FullName!);
        Assert.NotNull(testMethodsType);

        var methodE = testMethodsType.GetMethod(nameof(TestMethods.MethodE));
        Assert.NotNull(methodE);
        Assert.Equal("MethodE", methodE.Name);
        Assert.Equal(4, methodE.Parameters.Count);

        var paramA = methodE.Parameters[0];
        Assert.Equal("a", paramA.Name);
        Assert.False(paramA.IsOptional);
        Assert.Equal("System.Int32", paramA.ParameterType.FullName);
        Assert.Equal(DBNull.Value, paramA.RawDefaultValue);

        var paramB = methodE.Parameters[1];
        Assert.Equal("b", paramB.Name);
        Assert.False(paramB.IsOptional);
        Assert.Equal("System.Nullable`1", paramB.ParameterType.FullName);
        Assert.Equal(DBNull.Value, paramB.RawDefaultValue);

        var paramC = methodE.Parameters[2];
        Assert.Equal("c", paramC.Name);
        Assert.True(paramC.IsOptional);
        Assert.Equal("System.Int32", paramC.ParameterType.FullName);
        Assert.Equal(1, paramC.RawDefaultValue);

        var paramD = methodE.Parameters[3];
        Assert.Equal("d", paramD.Name);
        Assert.True(paramD.IsOptional);
        Assert.Equal("System.Nullable`1", paramD.ParameterType.FullName);
        Assert.Equal(default(int?), paramD.RawDefaultValue);
    }

    [Fact]
    public void AssemblyLoaderContextResolvesTypeDefinitions()
    {
        using var loader = CreateAssemblyLoaderContext(out var testAssembly);

        var typeA = testAssembly.GetTypeDefinition(typeof(A).FullName!);
        var typeB = testAssembly.GetTypeDefinition(typeof(B).FullName!);
        Assert.NotNull(typeA);
        Assert.NotNull(typeB);
        Assert.Equal(typeof(A).FullName, typeA.FullName);
        Assert.Equal(typeof(B).FullName, typeB.FullName);
        Assert.Equal(typeA, typeB.BaseType);
    }

    [Fact]
    public void AssemblyLoaderContextResolvesGenericTypeDefinitions()
    {
        // Type Definition names come from the assembly blobs. They use the `1, `2 suffixes for generic types.

        using var loader = CreateAssemblyLoaderContext(out var testAssembly);

        var typeA1 = testAssembly.GetTypeDefinition(typeof(GenericTypeA<>).FullName!);
        var typeA2 = testAssembly.GetTypeDefinition(typeof(GenericTypeA<,>).FullName!);

        var typeB1 = testAssembly.GetTypeDefinition(typeof(GenericTypeB<>).FullName!);
        var typeB2 = testAssembly.GetTypeDefinition(typeof(GenericTypeB<,>).FullName!);

        Assert.NotNull(typeA1);
        Assert.NotNull(typeA2);
        Assert.NotNull(typeB1);
        Assert.NotNull(typeB2);
        Assert.Equal(typeof(GenericTypeA<>).FullName, typeA1.FullName);
        Assert.Equal(typeof(GenericTypeA<,>).FullName, typeA2.FullName);
        Assert.Equal(typeof(GenericTypeB<>).FullName, typeB1.FullName);
        Assert.Equal(typeof(GenericTypeB<,>).FullName, typeB2.FullName);
    }

    [Fact]
    public void NonPublicTypeDefinitionsAreIgnored()
    {
        using var loader = CreateAssemblyLoaderContext(out var testAssembly);

        var publicType = testAssembly.GetTypeDefinition(typeof(PublicType).FullName!);
        var internalType = testAssembly.GetTypeDefinition(typeof(InternalType).FullName!);
        var privateType = testAssembly.GetTypeDefinition("Aspire.Cli.Tests.Polyglot.PrivateType");
        var nestedType = testAssembly.GetTypeDefinition("Aspire.Cli.Tests.Polyglot.PublicSealedType+NestedType");
        Assert.NotNull(nestedType); // Nested types are considered public if the containing type is public
        Assert.NotNull(publicType);
        Assert.Null(internalType);
        Assert.Null(privateType);
    }

    private static AssemblyLoaderContext CreateAssemblyLoaderContext(out RoAssembly testAssembly)
    {
        var assemblyLoaderContext = new AssemblyLoaderContext();
        var mscorlib = assemblyLoaderContext.LoadAssembly(typeof(int).Assembly.Location);
        var result = assemblyLoaderContext.LoadAssembly(typeof(TestMethods).Assembly.Location);
        Assert.NotNull(mscorlib);
        Assert.NotNull(result);
        testAssembly = result;
        return assemblyLoaderContext;
    }
}
