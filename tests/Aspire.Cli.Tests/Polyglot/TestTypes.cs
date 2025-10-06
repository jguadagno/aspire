// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Cli.Tests.Polyglot;

public class A
{

}

public class B : A
{
}

public class GenericTypeA<T> { }
public class GenericTypeA<T, U> { }

public class GenericTypeB<T> { }
public class GenericTypeB<T, U> { }

public class PublicType { }
public sealed class PublicSealedType { public class NestedType { } }
sealed class PrivateType { }
internal sealed class InternalType { }
