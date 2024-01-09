# :warning: Deprecation Warning :warning:

This library has been deprecated and will no longer receive updates.

---

RockLib has been a cornerstone of our open source efforts here at Rocket Mortgage, and it's played a significant role in our journey to drive innovation and collaboration within our organization and the open source community. It's been amazing to witness the collective creativity and hard work that you all have poured into this project.

However, as technology relentlessly evolves, so must we. The decision to deprecate this library is rooted in our commitment to staying at the cutting edge of technological advancements. While this chapter is ending, it opens the door to exciting new opportunities on the horizon.

We want to express our heartfelt thanks to all the contributors and users who have been a part of this incredible journey. Your contributions, feedback, and enthusiasm have been invaluable, and we are genuinely grateful for your dedication. 🚀

---

## RockLib.UniversalMemberAccessor

*Defines a dynamic proxy object that enables easy access to non-public members.*

```powershell
PM> Install-Package RockLib.UniversalMemberAccessor
```

## Table of Contents

- [Supported Targets](#supported-targets)
- [Overview](#overview)
  - [TL;DR](#tldr)
- [UniversalMemberAccessor Class](#universalmemberaccessor-class)
- [Extension methods](#extension-methods)
- [Pseudo-members](#pseudo-members)
- [Uses Cases](#use-cases)

## Supported Targets

 This library supports the following targets:
   - .NET 6
   - .NET Core 3.1
   - .NET Framework 4.8

## Overview

Have you ever needed to access the non-public members of a class?

Maybe you need to test some class's private method - you could make the method internal and add an `[InternalsVisibleTo("Your.Test.Assembly")]` attribute. But that's annoying to have to do this and you don't really it to be internal - you just want to test it.

Perhaps a third-party library that you depend on doesn't expose a field that you need. When you debug your application, and you can see the field you need under 'Non-Public Members', but there's no corresponding public property. You could use reflection to access the private field, but that API is awkward and verbose and slow.

There are other non-public members that you need to access: fields, properties, methods, constructors, or events. What about internal types? Or nested, non-public types? Each of these things is increasingly difficult to work with when using .NET's reflection API.

Wouldn't it be great if you could just pretend that everything was public? With `dynamic`, you *almost* can.

```csharp
using System;

public class Foo
{
    private readonly DateTime _timestamp = DateTime.UtcNow;
    public override string ToString() => $"Timestamp: {_timestamp:O}";
}

void Main()
{
    dynamic foo = new Foo();
    var then = new DateTime(2018, 4, 23, 21, 21, 29, 314, DateTimeKind.Utc);
    foo._timestamp = then; // RuntimeBinderException: 'Foo._timestamp' is inaccessible due to its protection level
    Console.WriteLine(foo.ToString());
}
```

This compiles and starts to run. But it throws a run-time exception when trying to set the private field because the DLR forbids access to non-visible members. However, with one small change, it works.

```csharp
using RockLib.Dynamic;
using System;

public class Foo
{
    private readonly DateTime _timestamp = DateTime.UtcNow;
    public override string ToString() => $"Timestamp: {_timestamp:O}";
}

void Main()
{
    dynamic foo = new Foo().Unlock(); // Unlocks the non-public members of this instance of Foo
    var then = new DateTime(2018, 4, 23, 21, 21, 41, 730, DateTimeKind.Utc);
    foo._timestamp = then; // Successfully changes the value of _timestamp
    Console.WriteLine(foo.ToString());
}
```

### TL;DR

UniversalMemberAccessor lets you pretend everything is public.

## UniversalMemberAccessor Class

The main class that this package exposes is `RockLib.UniversalMemberAccessor` - all public methods in the package return an instance of this class. It inherits from [`System.Dynamic.DynamicObject`](https://msdn.microsoft.com/en-us/library/system.dynamic.dynamicobject.aspx) and is meant to be used as a variable of type `dynamic`. When used this way, an instance of this class allows you to call non-public members with the same syntax as if they were public. The following example demonstrates most of the basic functionality of the library:

```csharp
using RockLib.UniversalMemeberAccessor;
using System;

public class Foo
{
    public Foo(int bar) => _bar = bar * 2;

    private readonly int _bar;
    private int Bar => _bar * 3;
    private void PrintBar() => Baz.Qux(Bar);

    private static class Baz
    {
        private void Qux(int corge) => Console.WriteLine(corge);
    }
}

class Program
{
    static void Main(string[] args)
    {
        dynamic foo = typeof(Foo).New(123); // Invoke a private constructor
        int barField = foo._bar; // Get the value of a private field
        int barProperty = foo.Bar; // Get the value of a private property
        Console.WriteLine($"foo._bar: {barField}, foo.Bar: {barProperty}");
        foo._bar = 777; // Set the value of a private readonly field
        foo.PrintBar(); // Call a private method
        foo.Baz.Qux(54321); // Access a private nested class and call its private method
    }
}
```

### Extension methods

There are three extension methods defined in this package:

- `public static dynamic New(this Type type, params object[] args)`
  - Invokes the constructor of the specified type, passing it the arguments from the `args` parameter, and returns a dynamic proxy object for the newly created object.
- `public static dynamic Unlock(this Type type)`
  - Returns a dynamic proxy object that allows access to all static members of the specified type.
- `public static dynamic Unlock(this object instance)`
  - Returns a dynamic proxy object that allows access to all instance and static members of the specified object.

### Pseudo-members

Some concepts are not easily represented with a proxy object, such as invoking a constructor, or accessing the instance's base class's private members. These problems are solved with "pseudo-members", where you call the pseudo-member as if it were regular method or property. Each pseudo-member has several names, in case there is a real member with same name as one of the pseudo-member names.

#### `New` pseudo-method

This pseudo-method invokes a constructor of the proxy object's type. There are four names: `New`, `Create`, `NewInstance`, and `CreateInstance`.

```csharp
public class Foo
{
    private Foo(int bar, string baz) { }
}

void Main()
{
    Foo foo1 = typeof(Foo).New(123, "abc");
    Foo foo2 = typeof(Foo).Create(123, "abc");
    Foo foo3 = typeof(Foo).NewInstance(123, "abc");
    Foo foo4 = typeof(Foo).CreateInstance(123, "abc");
}
```

#### `Instance` pseudo-property

This pseudo-property returns the underlying object of the dynamic proxy, i.e. the object that the `Unlock` extension method was called on. It has three names: `Instance`, `Object`, and `Value`.

```csharp
public class Foo
{
    private Foo()
    {
    }
}

public void Bar(Foo foo)
{
}

void Main()
{
    var foo = typeof(Foo).New();

    Bar(foo.Instance);
    Bar(foo.Object);
    Bar(foo.Value);
}
```

#### `Base` pseudo-property

This pseudo-property returns the a dynamic proxy that unlocks the same object that the current dynamic proxy unlocks, except from the perspective of the base type of the current dynamic proxy's type. This allows access to the private members of a type's base type. It has three names: `Base`, `BaseType`, and `BaseClass`.

```csharp
public abstract class FooBase
{
    private void Bar(string baz) => Console.WriteLine($"Yes, call me. {baz}");
}

public class Foo : FooBase
{
    private void Bar(string baz) => Console.WriteLine($"No, don't call me! {baz}");
}

void Main()
{
    var foo = new Foo().Unlock();
    foo.Base.Bar("abc");
    foo.BaseType.Bar("xyz");
    foo.BaseClass.Bar("123");
}
```

### Use Cases

---

*Problem: You have an instance of an object and need to access its non-public members, instance or static.*

Solution: Add a using for "RockLib.Dynamic" and call the `Unlock()` extension method on the instance.

```csharp
dynamic proxy = someObject.Unlock();
// TODO: Use the proxy instance to access someObject's non-public members
```

---

*Problem: You have a `System.Type` and need to access the non-public static members of that type.*

Solution: Add a using for "RockLib.Dynamic" and call the `Unlock()` extension method on the type.

```csharp
dynamic proxy = someType.Unlock();
// TODO: Use the proxy instance to access the static members of the someType type
```

---

*Problem: You need to invoke the non-public constructor of a `System.Type`.*

Solution: Add a using for "RockLib.Dynamic" and call the `New()` extension method on the type. The extension method has a `params object[]` parameter.

```csharp
public class Foo
{
    private Foo(int bar, string baz) { }
}

void Main()
{
    Foo foo = typeof(Foo).New(123, "abc");
}
```

Alternate Solution: Call the `Unlock()` extension method on the type, then call the [`New` pseudo-method](#new-pseudo-method) with the constructor's parameters.

```csharp
public class Foo
{
    private Foo(int bar, string baz) { }
}

void Main()
{
    Foo foo1 = typeof(Foo).Unlock().New(123, "abc");
}
```

---

*Problem: You need to access a non-public nested type.*

```c#
public class Foo
{
    // We want to call this method
    private static void Bar(Baz baz)  => Console.WriteLine($"foo.Bar: {baz.Qux}");

    // Need to instantiate this class in order to call the Bar method
    private class Baz
    {
        public Baz(int qux) => Qux = qux;
        public int Qux { get; }
    }
}
```

Solution: Unlock the container type and access the nested type by name.

```c#
void Main()
{
    dynamic foo = typeof(Foo).Unlock();

    // Access the Baz type and call the New pseudo-method on it.
    dynamic baz = foo.Baz.New(123);

    foo.Bar(baz);
}
```

---

*Problem: You need to access the private member of an object's base class but the derived class defines a member that hides it.*

```c#
public abstract class FooBase
{
    private void Bar(string baz) => Console.WriteLine($"Yes, call me. {baz}");
}

public class Foo : FooBase
{
    private void Bar(string baz) => Console.WriteLine($"No, don't call me! {baz}");
}

void Main()
{
    var foo = new Foo().Unlock();
    foo.Bar("abc"); // This calls the wrong Bar method!
}
```

Solution: Use the [`Base` pseudo-property](#base-pseudo-property).

```c#
void Main()
{
    var foo = new Foo().Unlock();
    foo.Base.Bar("abc"); // Calls the right Bar method.
}
```

*Problem: You get a 'RuntimeBinderException: The best overloaded method has some invalid arguments' error when you try to pass the dynamic proxy object to a method, even though it is implicitly convertable to the paramter type.*

```c#
public class Foo
{
    private Foo()
    {
    }
}

public void Bar(Foo foo)
{
}

void Main()
{
    var foo = typeof(Foo).New();
    Bar(foo); // Throws 'The best overloaded method match for 'UserQuery.Bar(UserQuery.Foo)' has some invalid arguments'
}
```

Solution: There are two solutions: get the [Instance pseudo-property](#instance-pseudo-property), or cast the dynamic proxy object to the target type.

```c#
void Main()
{
    var foo = typeof(Foo).New();

    // Get the Instance pseudo-property
    Bar(foo.Instance);

    // Cast to target type
    Bar((Foo)foo);
}
```
