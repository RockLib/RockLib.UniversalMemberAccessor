using FluentAssertions;
using Microsoft.CSharp.RuntimeBinder;
using RockLib.Dynamic.UnitTests.TypeCreator;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;

namespace RockLib.Dynamic.UnitTests
{
    public class UniversalMemberAccessorTests
    {
        [Fact]
        public void TestSomeIndexerStuff()
        {
            //TODO: This just makes sure nothing blows up, still need to add legit tests.
            var testInstance1 = new HasIndexedProperty();
            var testInstance2 = new Dictionary<string, string> { { "first", "one" }, { "second", "two" }, { "third", "three" } };
#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
            var testInstance3 = new string[,] { { "one", "two", "three" }, { "two", "three", "four" }, { "three", "four", "five" } };
#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional
            var testInstance4 = new string[] { "one", "two", "three" };

            var unlocked1 = testInstance1.Unlock();
            var unlocked2 = testInstance2.Unlock();
            var unlocked3 = testInstance3.Unlock();
            var unlocked4 = testInstance4.Unlock();

            var unlocked1Test1 = unlocked1[1];
            var unlocked1Test2 = unlocked1["something"];
            var unlocked1Test3 = unlocked1[1, "something"];
            unlocked1[1, "stuff"] = "5";

            var unlocked2Test1 = unlocked2["third"];
            unlocked2["first"] = "not_first";

            var unlocked3Test1 = unlocked3[1, 2];
            unlocked3[2, 1] = "not_two";

            var unlocked4Test1 = unlocked4[2];
            unlocked4[0] = "not_one";

            //NOTE: For now, if we got here, indexing is working as expected.
        }

        private class HasIndexedProperty
        {
            // Disabling for testing purposes
#pragma warning disable CA1819 // Properties should not return arrays
            public string[] StringArray { get; set; } = { "one", "two", "three" };
#pragma warning restore CA1819 // Properties should not return arrays

#pragma warning disable CA1805 // Do not initialize unnecessarily
            private string? _setStringIndex = null;
            private int _setIntIndex = 0;
#pragma warning restore CA1805 // Do not initialize unnecessarily


            public string this[string index]
            {
                get
                {
                    return index;
                }
                set
                {
                    _setStringIndex = value;
                }
            }

            public int this[int index]
            {
                get
                {
                    return index;
                }
                set
                {
                    _setIntIndex = value;
                }
            }

            public string this[int index, string stringIndex]
            {
                get
                {
                    return stringIndex + index;
                }
                set
                {
                    _setIntIndex = int.Parse(value, CultureInfo.InvariantCulture.NumberFormat);
                    _setStringIndex = value;
                }
            }
        }

        [Fact]
        public void ObjectVirtualMethodsAreInvokedAsExpectedForInstanceProxy()
        {
            var instance = new SpiesOnObjectMethods();

            // Assumptions
            instance.ToStringInvocations.Should().Be(0);
            instance.GetHashCodeInvocations.Should().Be(0);
            instance.EqualsInvocations.Should().Be(0);

            string toStringDirect = instance.ToString();
            int getHashCodeDirect = instance.GetHashCode();
            bool equalsDirect = instance.Equals(instance);

            // Assumptions
            instance.ToStringInvocations.Should().Be(1);
            instance.GetHashCodeInvocations.Should().Be(1);
            instance.EqualsInvocations.Should().Be(1);

            var proxy = instance.Unlock();

            string toStringProxy = proxy.ToString();
            int getHashCodeProxy = proxy.GetHashCode();
            bool equalsProxy = proxy.Equals(proxy);

            instance.ToStringInvocations.Should().Be(2);
            instance.GetHashCodeInvocations.Should().Be(2);
            instance.EqualsInvocations.Should().Be(2);

            toStringProxy.Should().Be(toStringDirect);
            getHashCodeProxy.Should().Be(getHashCodeDirect);
            equalsProxy.Should().Be(equalsDirect);
        }

        [Fact]
        public void ObjectVirtualMethodsAreInvokedOnTypeForStaticProxy()
        {
            var type = Create.Class();

            var proxy = type.Unlock();

            var actualToString = type.ToString();
            string proxyToString = proxy.ToString();

            var actualGetHashCode = type.GetHashCode();
            int proxyGetHashCode = proxy.GetHashCode();

            var actualEquals = type.Equals(type);
            bool proxyEquals = proxy.Equals(proxy);

            proxyToString.Should().Be(actualToString);
            proxyGetHashCode.Should().Be(actualGetHashCode);
            proxyEquals.Should().Be(actualEquals);
        }

        [Fact]
        public void UnlockExtensionMethodAndGetMethodReturnTheSameObject()
        {
            var type = Create.Class();

            var instance = Activator.CreateInstance(type);

            object unlock = instance!.Unlock();
            object get = UniversalMemberAccessor.Get(instance!);

            unlock.Should().BeSameAs(get);
        }

        [Fact]
        public void UnlockExtensionMethodAndGetStaticMethodReturnTheSameObject()
        {
            var type = Create.Class();

            object unlock = type.Unlock();
            object getStatic = UniversalMemberAccessor.GetStatic(type);

            unlock.Should().BeSameAs(getStatic);
        }

        [Fact]
        public void GetDynamicMemberNamesReturnsAllTheMemberNamesOfTheType()
        {
            var type = Create.Class();

            var instance = type.New();

            var allMemberNames = type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => !(m is ConstructorInfo))
                .Select(m => m.Name);

            IEnumerable<string> dynamicMemberNames = instance.GetDynamicMemberNames();

            dynamicMemberNames.Should().BeEquivalentTo(allMemberNames);
        }

        [Fact]
        public void CannotCallGetStaticWithNullType()
        {
            Action act = () => UniversalMemberAccessor.GetStatic((Type)null!);

            act.Should().ThrowExactly<ArgumentNullException>();
        }

        [Fact]
        public void CanGetBackingInstanceWithInstance()
        {
            var foo = new Foo(123).Unlock();

            object backingFoo = foo.Instance;

            ((object)foo).Should().BeOfType<UniversalMemberAccessor>();
            backingFoo.Should().BeOfType<Foo>();
        }

        [Fact]
        public void CanGetBackingInstanceWithObject()
        {
            var foo = new Foo(123).Unlock();

            object backingFoo = foo.Object;

            ((object)foo).Should().BeOfType<UniversalMemberAccessor>();
            backingFoo.Should().BeOfType<Foo>();
        }

        [Fact]
        public void CanGetBackingInstanceWithValue()
        {
            var foo = new Foo(123).Unlock();

            object backingFoo = foo.Value;

            ((object)foo).Should().BeOfType<UniversalMemberAccessor>();
            backingFoo.Should().BeOfType<Foo>();
        }

        [Fact]
        public void CanImplicitlyConvert()
        {
            var foo = new Foo(123).Unlock();

            Foo backingFoo = foo;

            ((object)foo).Should().BeOfType<UniversalMemberAccessor>();
            backingFoo.Should().BeOfType<Foo>();
        }

        [Fact]
        public void CannotImplicitlyConvertToWrongType()
        {
            var foo = new Foo(123).Unlock();

            Bar bar;

            Action act = () => bar = foo;

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "Cannot implicitly convert type 'RockLib.Dynamic.UnitTests.Foo' to 'RockLib.Dynamic.UnitTests.Bar'");
        }

        [Fact]
        public void CannotGetBackingInstanceFromStaticAccessorWithInstance()
        {
            var foo = typeof(Foo).Unlock();

            Action act = () => _ = foo.Instance;

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Fact]
        public void CannotGetBackingInstanceFromStaticAccessorWithObject()
        {
            var foo = typeof(Foo).Unlock();

            Action act = () => _ = foo.Object;

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Fact]
        public void CannotGetBackingInstanceFromStaticAccessorWithValue()
        {
            var foo = typeof(Foo).Unlock();

            Action act = () => _ = foo.Value;

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Fact]
        public void CanGetValueOfPrivateInstanceField()
        {
            var foo = new Foo(123).Unlock();

            int bar = foo._bar;

            bar.Should().Be(123);
        }

        [Fact]
        public void CanGetValueOfPrivateInstanceProperty()
        {
            var foo = new Foo(123).Unlock();

            int bar = foo.Bar;

            bar.Should().Be(123);
        }

        [Fact]
        public void CanSetValueOfPrivateInstanceField()
        {
            var foo = new Foo().Unlock();

            foo._bar = 123;

            ((int)foo._bar).Should().Be(123);
        }

        [Fact]
        public void CanSetValueOfPrivateInstanceProperty()
        {
            var foo = new Foo().Unlock();

            foo.Bar = 123;

            ((int)foo.Bar).Should().Be(123);
        }

        [Fact]
        public void CannotSetValueOfPropertyThatDoesNotExist()
        {
            var foo = new Foo().Unlock();

            Action act = () => foo.DoesNotExist = "abc";

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "'RockLib.Dynamic.UniversalMemberAccessor' does not contain a definition for 'DoesNotExist'");
        }

        [Fact]
        public void CannotSetWrongTypeForField()
        {
            var type = Create.Class("Foo", Define.Field("_bar", typeof(int)));

            var foo = type.New();

            Action act = () => foo._bar = "abc";

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "Cannot implicitly convert type 'System.String' to 'System.Int32'");
        }

        [Fact]
        public void CannotSetWrongTypeForProperty()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(int)));

            var foo = type.New();

            Action act = () => foo.Bar = "abc";

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "Cannot implicitly convert type 'System.String' to 'System.Int32'");
        }

        [Fact]
        public void CannotSetNullForNonNullableValueTypeField()
        {
            var type = Create.Class("Foo", Define.Field("_bar", typeof(int)));

            var foo = type.New();

            Action act = () => foo._bar = null;

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "Cannot convert null to 'System.Int32' because it is a non-nullable value type");
        }

        [Fact]
        public void CannotSetNullForNonNullableValueTypeProperty()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(int)));

            var foo = type.New();

            Action act = () => foo.Bar = null;

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "Cannot convert null to 'System.Int32' because it is a non-nullable value type");
        }

        [Fact]
        public void CanSetFieldValueWhenValueIsWrappedInUniversalMemberAccessor()
        {
            var barType = Create.Class("Bar", Define.Field("_baz", typeof(int)));
            var fooType = Create.Class("Foo", Define.Field("_bar", barType));

            var bar = barType.New();
            bar._baz = 123;

            var foo = fooType.New();
            foo._bar = bar;

            int baz = foo._bar._baz;

            baz.Should().Be(123);
        }

        [Fact]
        public void CanCreateInstanceWhenConstructorArgIsUniversalMemberAccessor()
        {
            var barType = Create.Class("Bar", Define.Field("_baz", typeof(int)),
                Define.Constructor(new Parameter(typeof(int), "_baz")));
            var fooType = Create.Class("Foo", Define.Field("_bar", barType),
                Define.Constructor(new Parameter(barType, "_bar")));

            object bar = barType.New(123);

            var foo = fooType.New(bar);

            int baz = foo._bar._baz;

            baz.Should().Be(123);
        }

        [Fact]
        public void CanCreateInstanceOfStruct()
        {
            var type = Create.Struct("FooStruct");

            var foo = type.New();

            ((object)foo.Value).GetType().Should().Be(type);
        }

        [Fact]
        public void CanCreateInstanceOfStructWithDeclaredConstructor()
        {
            var type = Create.Struct("FooStruct", Define.Constructor(typeof(int)));

            var foo = type.New(123);

            ((object)foo.Value).GetType().Should().Be(type);
        }

        [Fact]
        public void CanCreateInstanceOfString()
        {
            var type = typeof(string);

            string foo = type.New('a', 3);

            foo.Should().Be("aaa");
        }

        [Fact]
        public void CanSetNullableFieldToValue()
        {
            var type = Create.Class("Foo", Define.Field("_bar", typeof(int?)));

            var foo = type.New();

            foo._bar = 123;

            int bar = foo._bar;
            bar.Should().Be(123);
        }

        [Fact]
        public void CanSetNullableFieldToNull()
        {
            var type = Create.Class("Foo", Define.Field("_bar", typeof(int?)));

            var foo = type.New();

            foo._bar = null;

            object bar = foo._bar;
            bar.Should().BeNull();
        }

        [Fact]
        public void CanSetNullablePropertyToValue()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(int?)));

            var foo = type.New();

            foo.Bar = 123;

            int bar = foo.Bar;
            bar.Should().Be(123);
        }

        [Fact]
        public void CanSetNullablePropertyToNull()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(int?)));

            var foo = type.New();

            foo.Bar = null;

            object bar = foo.Bar;
            bar.Should().BeNull();
        }

        [Fact]
        public void CanSetMoreSpecificNumericType()
        {
            var foo = new Foo().Unlock();

            Action act = () => foo.Bar = (byte)123;

            act.Should().NotThrow();
        }

        [Fact]
        public void CanGetValueOfPrivateStaticFieldThroughInstanceAccessor()
        {
            Foo.Reset();

            var foo = new Foo().Unlock();

            int baz = foo._baz;

            baz.Should().Be(-1);
        }

        [Fact]
        public void CanGetValueOfPrivateStaticPropertyThroughInstanceAccessor()
        {
            Foo.Reset();

            var foo = new Foo().Unlock();

            int baz = foo.Baz;

            baz.Should().Be(-1);
        }

        [Fact]
        public void CanSetValueOfPrivateStaticFieldThroughInstanceAccessor()
        {
            var foo = new Foo().Unlock();

            foo._baz = 123;

            ((int)foo._baz).Should().Be(123);
        }

        [Fact]
        public void CanSetValueOfPrivateStaticPropertyThroughInstanceAccessor()
        {
            var foo = new Foo().Unlock();

            foo.Baz = 123;

            ((int)foo.Baz).Should().Be(123);
        }

        [Fact]
        public void CanGetValueOfPrivateStaticFieldThroughStaticAccessor()
        {
            Foo.Reset();

            var foo = typeof(Foo).Unlock();

            int baz = foo._baz;

            baz.Should().Be(-1);
        }

        [Fact]
        public void CanGetValueOfPrivateStaticPropertyThroughStaticAccessor()
        {
            Foo.Reset();

            var foo = typeof(Foo).Unlock();

            int baz = foo.Baz;

            baz.Should().Be(-1);
        }

        [Fact]
        public void CanSetValueOfPrivateStaticFieldThroughStaticAccessor()
        {
            var foo = typeof(Foo).Unlock();

            foo._baz = 123;

            ((int)foo._baz).Should().Be(123);
        }

        [Fact]
        public void CanSetValueOfPrivateStaticPropertyThroughStaticAccessor()
        {
            var foo = typeof(Foo).Unlock();

            foo.Baz = 123;

            ((int)foo.Baz).Should().Be(123);
        }

        [Fact]
        public void CanCreateInstanceWithNewExtensionMethodWithNoParameters()
        {
            var type = Create.Class("Foo");

            var foo = type.New();

            ((object)foo.Value).GetType().Name.Should().Be("Foo");
        }

        [Fact]
        public void CanCreateInstanceWithNewExtensionMethodWithParameters()
        {
            var type = Create.Class("Foo", Define.Constructor(typeof(int), typeof(string)));

            var foo = type.New(123, "abc");

            ((object)foo.Value).GetType().Name.Should().Be("Foo");
        }

        [Fact]
        public void CannotCreateInstanceOfObjectWithWrongNumberOfParameters()
        {
            var type = Create.Class("Foo");

            Action act = () => type.New(123, "abc");

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "'Foo' does not contain a constructor that takes 2 arguments");
        }

        [Fact]
        public void CannotCreateInstanceOfObjectWithWrongParameterType()
        {
            var type = Create.Class("Foo", Define.Constructor(typeof(int)));

            Action act = () => type.New("abc");

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "The best overloaded constructor match for 'Foo.Foo(Int32)' has some invalid arguments");
        }

        [Fact]
        public void CanChooseTheBestConstructor()
        {
            var type = Create.Class("Foo",
                Define.Constructor(new Parameter(typeof(long), "_long")),
                Define.Constructor(new Parameter(typeof(short), "_short")),
                Define.Field("_long", typeof(long), isReadOnly: true),
                Define.Field("_short", typeof(short), isReadOnly: true));

            long longValue;
            short shortValue;

            var foo1 = type.New((long)123); // exact match to long ctor
            longValue = foo1._long;
            shortValue = foo1._short;
            longValue.Should().Be(123);
            shortValue.Should().Be(0);

            var foo2 = type.New((short)123); // exact match to short ctor
            longValue = foo2._long;
            shortValue = foo2._short;
            longValue.Should().Be(0);
            shortValue.Should().Be(123);

            var foo3 = type.New((uint)123); // only long ctor is legal
            longValue = foo3._long;
            shortValue = foo3._short;
            longValue.Should().Be(123);
            shortValue.Should().Be(0);

            var foo4 = type.New((byte)123); // short is "closer" to byte than long
            longValue = foo4._long;
            shortValue = foo4._short;
            longValue.Should().Be(0);
            shortValue.Should().Be(123);
        }

        [Fact]
        public void CannotChoseBestConstructorWhenChoiceIsAmbiguous()
        {
            var type = Create.Class(
                   Define.Constructor(typeof(ICountryHam)),
                   Define.Constructor(typeof(Ham)));

            Action act = () => type.New(new CountryHam());

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Fact]
        public void CannotCallMethodWithWrongNumberOfParameters()
        {
            var type = Create.Class("Foo", Define.EchoMethod("Bar", typeof(int)));

            var foo = type.New();

            Action act = () => foo.Bar(123, 456);

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "No overload for method 'Bar' takes 2 arguments");
        }

        [Fact]
        public void CannotCallMethodWithWrongParameters()
        {
            var type = Create.Class("Foo", Define.EchoMethod("Bar", typeof(int)));

            var foo = type.New();

            Action act = () => foo.Bar("abc");

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "The best overloaded method match for 'Foo.Bar(Int32)' has some invalid arguments");
        }

        [Fact]
        public void CanCallMethodWithValueTypeRefParameterReturningVoid()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(int), true));

            var foo = type.New();

            var i = -1;
            foo.Bar(123, ref i);

            i.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithValueTypeRefParameterReturningValue()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(int), true));

            var foo = type.New();

            var i = -1;
            int dummy = foo.Bar(123, ref i);

            i.Should().Be(123);
            dummy.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithValueTypeOutParameterReturningVoid()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(int), true));

            var foo = type.New();

            int i;
            foo.Bar(123, out i);

            i.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithValueTypeOutParameterReturningValue()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(int)));

            var foo = type.New();

            int i;
            int dummy = foo.Bar(123, out i);

            i.Should().Be(123);
            dummy.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithNullableTypeRefParameterReturningVoid()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(int?), true));

            var foo = type.New();

            int? i = -1;
            foo.Bar(123, ref i);

            i.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithNullableTypeRefParameterReturningValue()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(int?), true));

            var foo = type.New();

            int? i = -1;
            int dummy = foo.Bar(123, ref i);

            i.Should().Be(123);
            dummy.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithNullableTypeOutParameterReturningVoid()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(int?), true));

            var foo = type.New();

            int? i;
            foo.Bar(123, out i);

            i.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithNullableTypeOutParameterReturningValue()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(int?)));

            var foo = type.New();

            int? i;
            int dummy = foo.Bar(123, out i);

            i.Should().Be(123);
            dummy.Should().Be(123);
        }

        [Fact]
        public void CanCallMethodWithNullableParameterWithNull()
        {
            var type = Create.Class("Foo", Define.EchoMethod("Bar", typeof(int?)));

            var foo = type.New();

            int? value = foo.Bar(null);

            value.Should().BeNull();
        }

        [Fact]
        public void CanCallMethodWithReferenceTypeRefParameterReturningVoid()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(string), true));

            var foo = type.New();

            var s = "";
            foo.Bar("abc", ref s);

            s.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithReferenceTypeRefParameterReturningValue()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(string)));

            var foo = type.New();

            var s = "";
            string dummy = foo.Bar("abc", ref s);

            s.Should().Be("abc");
            dummy.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithReferenceTypeOutParameterReturningVoid()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(string), true));

            var foo = type.New();

            string s;
            foo.Bar("abc", out s);

            s.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithReferenceTypeOutParameterReturningValue()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(string)));

            var foo = type.New();

            string s;
            string dummy = foo.Bar("abc", out s);

            s.Should().Be("abc");
            dummy.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithRefParameterUsingOutKeyword()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(string)));

            var foo = type.New();

            string s;
            string dummy = foo.Bar("abc", out s);

            s.Should().Be("abc");
            dummy.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithOutParameterUsingRefKeyword()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(string)));

            var foo = type.New();

            string s = "";
            string dummy = foo.Bar("abc", ref s);

            s.Should().Be("abc");
            dummy.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithRefParameterWithoutUsingRefKeywordButVariableIsNotChanged()
        {
            var type = Create.Class("Foo", Define.EchoRefMethod("Bar", typeof(string)));

            var foo = type.New();

            string? s = null;
            string dummy = foo.Bar("abc", s);

            s.Should().BeNull();
            dummy.Should().Be("abc");
        }

        [Fact]
        public void CanCallMethodWithOutParameterWithoutUsingOutKeywordButVariableIsNotChanged()
        {
            var type = Create.Class("Foo", Define.EchoOutMethod("Bar", typeof(string)));

            var foo = type.New();

            string? s = null;
            string dummy = foo.Bar("abc", s);

            s.Should().BeNull();
            dummy.Should().Be("abc");
        }

        [Fact]
        public void CanCallComplexMethodWithMixtureOfRefOutAndRegularParameters()
        {
            var foo = new ComplexMethodWithMixtureOfRefOutAndRegularParameters().Unlock();

            double d;
            var s = "Hello, world!";
            int result = foo.Bar(123, out d, ref s);

            d.Should().Be(184.5);
            s.Should().Be("Hello, world! 5.481");
            result.Should().Be(246);
        }

        private class ComplexMethodWithMixtureOfRefOutAndRegularParameters
        {
#pragma warning disable CA1822 // Mark members as static
            private int Bar(int i, out double d, ref string s)
#pragma warning restore CA1822 // Mark members as static
            {
                d = i * 1.5;

                s += " " + new string(d.ToString(CultureInfo.InvariantCulture.NumberFormat).Reverse().ToArray());

                return i * 2;
            }
        }

        [Fact]
        public void CanChooseTheBestMethodOverload()
        {
            var type = Create.Class("Foo",
                Define.EchoMethod("Bar", typeof(long)),
                Define.EchoMethod("Bar", typeof(short)));

            var foo = type.New();

            object result;

            result = foo.Bar((long)123); // exact match to long overload
            result.Should().BeOfType<long>();

            result = foo.Bar((short)123); // exact match to short overload
            result.Should().BeOfType<short>();

            result = foo.Bar((uint)123); // only long overload is legal
            result.Should().BeOfType<long>();

            result = foo.Bar((byte)123); // short is "closer" to byte than long
            result.Should().BeOfType<short>();
        }

        [Fact]
        public void CannotChoseBestMethodOverloadWhenChoiceIsAmbiguous()
        {
            var type = Create.Class("Foo",
                Define.EchoMethod("Bar", typeof(ICountryHam)),
                Define.EchoMethod("Bar", typeof(Ham)));

            var foo = type.New();

            Action act = () => foo.Bar(new CountryHam());

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Theory]
        [InlineData(typeof(int), 123)]
        [InlineData(typeof(int), (short)456)]
        [InlineData(typeof(string), "abc")]
        public void CanSetReadonlyInstanceField(Type fieldType, object fieldValue)
        {
            var type = Create.Class("Foo", Define.Field("_bar", fieldType, false, true));

            var foo = type.New();

            foo._bar = fieldValue;

            object bar = foo._bar;
            bar.Should().Be(fieldValue);
        }

        [Fact]
        public void CannotSetReadonlyReferenceTypeStaticField()
        {
            var type = Create.Class("Foo", Define.Field("_bar", typeof(string), true, true));

            var foo = type.Unlock();

            Action act = () => foo._bar = "abc";

            act.Should().ThrowExactly<NotSupportedException>().Which.Message.Should().Be(
                "The current runtime does not allow the (illegal) changing of readonly static reference-type fields.");
        }

        [Fact]
        public void CannotSetReadonlyValueTypeStaticField()
        {
            var type = Create.Class("Foo", Define.Field("_bar", typeof(int), true, true));

            var foo = type.Unlock();

            Action act = () => foo._bar = 123;

            act.Should().ThrowExactly<NotSupportedException>().Which.Message.Should().Be(
                "The current runtime does not allow the (illegal) changing of readonly static value-type fields.");
        }

        [Fact]
        public void CanReadFieldWithIllegalCSharpName()
        {
            var type = Create.Class("Foo", Define.Field("<Bar>", typeof(int)));

            var foo = type.New();

            int bar = foo["<Bar>"];
            bar.Should().Be(0);
        }

        [Fact]
        public void CanWriteFieldWithIllegalCSharpName()
        {
            var type = Create.Class("Foo", Define.Field("<Bar>", typeof(int)));

            var foo = type.New();

            foo["<Bar>"] = 123;

            int bar = foo["<Bar>"];
            bar.Should().Be(123);
        }

        [Fact]
        public void CanReadFieldWithIllegalCSharpNameThatDoesNotExist()
        {
            var type = Create.Class("Foo");

            var foo = type.New();

            int bar;
            Action act = () => bar = foo["<Bar>"];

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Fact]
        public void CanWriteFieldWithIllegalCSharpNameThatDoesNotExist()
        {
            var type = Create.Class("Foo");

            var foo = type.New();

            Action act = () => foo["<Bar>"] = 123;

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Theory]
        [InlineData(typeof(int), typeof(int))]

        [InlineData(typeof(int?), typeof(int))]
        [InlineData(typeof(int?), typeof(byte))]

        [InlineData(typeof(object), typeof(int))]

        [InlineData(typeof(short), typeof(sbyte))]
        [InlineData(typeof(int), typeof(sbyte))]
        [InlineData(typeof(long), typeof(sbyte))]
        [InlineData(typeof(float), typeof(sbyte))]
        [InlineData(typeof(double), typeof(sbyte))]
        [InlineData(typeof(decimal), typeof(sbyte))]

        [InlineData(typeof(short), typeof(byte))]
        [InlineData(typeof(ushort), typeof(byte))]
        [InlineData(typeof(int), typeof(byte))]
        [InlineData(typeof(uint), typeof(byte))]
        [InlineData(typeof(long), typeof(byte))]
        [InlineData(typeof(ulong), typeof(byte))]
        [InlineData(typeof(float), typeof(byte))]
        [InlineData(typeof(double), typeof(byte))]
        [InlineData(typeof(decimal), typeof(byte))]

        [InlineData(typeof(int), typeof(short))]
        [InlineData(typeof(long), typeof(short))]
        [InlineData(typeof(float), typeof(short))]
        [InlineData(typeof(double), typeof(short))]
        [InlineData(typeof(decimal), typeof(short))]

        [InlineData(typeof(int), typeof(ushort))]
        [InlineData(typeof(uint), typeof(ushort))]
        [InlineData(typeof(long), typeof(ushort))]
        [InlineData(typeof(ulong), typeof(ushort))]
        [InlineData(typeof(float), typeof(ushort))]
        [InlineData(typeof(double), typeof(ushort))]
        [InlineData(typeof(decimal), typeof(ushort))]

        [InlineData(typeof(long), typeof(int))]
        [InlineData(typeof(float), typeof(int))]
        [InlineData(typeof(double), typeof(int))]
        [InlineData(typeof(decimal), typeof(int))]

        [InlineData(typeof(long), typeof(uint))]
        [InlineData(typeof(ulong), typeof(uint))]
        [InlineData(typeof(float), typeof(uint))]
        [InlineData(typeof(double), typeof(uint))]
        [InlineData(typeof(decimal), typeof(uint))]

        [InlineData(typeof(float), typeof(long))]
        [InlineData(typeof(double), typeof(long))]
        [InlineData(typeof(decimal), typeof(long))]

        [InlineData(typeof(float), typeof(ulong))]
        [InlineData(typeof(double), typeof(ulong))]
        [InlineData(typeof(decimal), typeof(ulong))]

        [InlineData(typeof(ushort), typeof(char))]
        [InlineData(typeof(int), typeof(char))]
        [InlineData(typeof(uint), typeof(char))]
        [InlineData(typeof(long), typeof(char))]
        [InlineData(typeof(ulong), typeof(char))]
        [InlineData(typeof(float), typeof(char))]
        [InlineData(typeof(double), typeof(char))]
        [InlineData(typeof(decimal), typeof(char))]

        [InlineData(typeof(double), typeof(float))]

        [InlineData(typeof(MyBase), typeof(MyDerived))]

        [InlineData(typeof(IMyInterface), typeof(MyDerived))]
        public void CanAssign(Type propertyType, Type valueType)
        {
            var value = Activator.CreateInstance(valueType);

            var type = Create.Class("Foo", Define.AutoProperty("Bar", propertyType));

            var foo = type.New();

            Action act = () => foo.Bar = value;

            act.Should().NotThrow();
        }

        [Fact]
        public void CanAssignSpecificArrayToArray()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(Array)));

            var foo = type.New();

            Action act = () => foo.Bar = Array.Empty<int>();

            act.Should().NotThrow();
        }

        [Fact]
        public void CanAssignSpecificDelegateToDelegate()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(Delegate)));

            var foo = type.New();

            Action act = () => foo.Bar = (Action)(() => { });

            act.Should().NotThrow();
        }

        [Fact]
        public void CanAssignSpecificEnumToEnum()
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(Enum)));

            var foo = type.New();

            Action act = () => foo.Bar = MyEnum.First;
            act.Should().NotThrow();
        }

        [Theory]
        [InlineData(typeof(IMyInterface[]))]

        [InlineData(typeof(IList<IMyInterface>))]
        [InlineData(typeof(ICollection<IMyInterface>))]
        [InlineData(typeof(IEnumerable<IMyInterface>))]

        [InlineData(typeof(IList))]
        [InlineData(typeof(ICollection))]
        [InlineData(typeof(IEnumerable))]
        public void CanAssignArrayToAllOfItsInterfaces(Type propertyType)
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", propertyType));

            var foo = type.New();

            Action act = () => foo.Bar = Array.Empty<MyDerived>();
            act.Should().NotThrow();
        }

        [Fact]
        public void CanAssignCovariant() // covariance
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(IEnumerable<IMyInterface>)));

            var foo = type.New();

            Action act = () => foo.Bar = new List<MyDerived>();
            act.Should().NotThrow();
        }

        [Fact]
        public void CanAssignContravariant() // contravariance
        {
            var type = Create.Class("Foo", Define.AutoProperty("Bar", typeof(Action<MyDerived>)));

            var foo = type.New();

            Action act = () => foo.Bar = (Action<MyBase>)(myBase => { });
            act.Should().NotThrow();
        }

        [Fact]
        public void CanRegisterAndDeregisterPrivateInstanceEvent()
        {
            var bar = new Bar().Unlock();

            var invocationCount = 0;

            EventHandler eventHandler = (sender, args) => invocationCount++;

            bar.Foo += eventHandler;

            bar.InvokeFoo();

            invocationCount.Should().Be(1);

            bar.Foo -= eventHandler;

            bar.InvokeFoo();

            invocationCount.Should().Be(1);
        }

        [Fact]
        public void CanRegisterAndDeregisterPrivateStaticEventThroughInstanceAccessor()
        {
            var bar = new Bar().Unlock();

            var invocationCount = 0;

            EventHandler eventHandler = (sender, args) => invocationCount++;

            bar.Baz += eventHandler;

            Bar.InvokeBaz();

            invocationCount.Should().Be(1);

            bar.Baz -= eventHandler;

            Bar.InvokeBaz();

            invocationCount.Should().Be(1);
        }

        [Fact]
        public void CanRegisterAndDeregisterPrivateStaticEventThroughStaticAccessor()
        {
            var bar = typeof(Bar).Unlock();

            var invocationCount = 0;

            EventHandler eventHandler = (sender, args) => invocationCount++;

            bar.Baz += eventHandler;

            Bar.InvokeBaz();

            invocationCount.Should().Be(1);

            bar.Baz -= eventHandler;

            Bar.InvokeBaz();

            invocationCount.Should().Be(1);
        }

        [Fact]
        public void CanCallPrivateInstanceMethods()
        {
            var foo = new Foo().Unlock();

            ((string)foo.Qux(123, "abc")).Should().Be("Qux(int i, string s)");
        }

        [Fact]
        public void CanCallPrivateStaticMethodsThroughInstanceAccessor()
        {
            var foo = new Foo().Unlock();

            ((string)foo.Grault(123)).Should().Be("Grault(int i)");
        }

        [Fact]
        public void CanCallPrivateStaticMethodsThroughStaticAccessor()
        {
            var foo = typeof(Foo).Unlock();

            ((string)foo.Grault(123)).Should().Be("Grault(int i)");
        }

        [Fact]
        public void CanResolveOverloadedMethods()
        {
            var foo = new Foo().Unlock();

            ((string)foo.Garply()).Should().Be("Garply()");
            ((string)foo.Garply(123)).Should().Be("Garply(int i)");
            ((string)foo.Garply("abc")).Should().Be("Garply(string s)");
            ((string)foo.Garply(new Baz())).Should().Be("Garply(IBaz b)");
            ((string)foo.Garply(123, null)).Should().Be("Garply(int i, string s)");
        }

        [Fact]
        public void AmbiguousInvocationThrowsRuntimeBinderException()
        {
            var foo = new Foo().Unlock();

            Action act = () => foo.Garply(null);

            act.Should().ThrowExactly<RuntimeBinderException>().Which.Message.Should().Be(
                "The call is ambiguous between the following methods or properties: 'Garply(System.String)' and 'Garply(RockLib.Dynamic.UnitTests.IBaz)'");
        }

        [Fact]
        public void CanInvokePrivateConstructorsWithNew()
        {
            var quxFactory = typeof(Qux).Unlock();

            Qux qux = quxFactory.New();

            qux.Should().BeOfType<Qux>();
        }

        [Fact]
        public void CanInvokePrivateConstructorsWithCreate()
        {
            var quxFactory = typeof(Qux).Unlock();

            Qux qux = quxFactory.Create();

            qux.Should().BeOfType<Qux>();
        }

        [Fact]
        public void CanInvokePrivateConstructorsWithNewInstance()
        {
            var quxFactory = typeof(Qux).Unlock();

            Qux qux = quxFactory.NewInstance();

            qux.Should().BeOfType<Qux>();
        }

        [Fact]
        public void CanInvokePrivateConstructorsWithCreateInstance()
        {
            var quxFactory = typeof(Qux).Unlock();

            Qux qux = quxFactory.CreateInstance();

            qux.Should().BeOfType<Qux>();
        }

        [Fact]
        public void CannotInvokeConstructorsWithAliasWithIncorrectArgs()
        {
            var quxFactory = typeof(Qux).Unlock();

            Action act = () => quxFactory.CreateInstance("wrong", "args");

            act.Should().ThrowExactly<RuntimeBinderException>();
        }

        [Fact]
        public void CanResolveMultipleConstructors()
        {
            var garplyFactory = typeof(Garply).Unlock();

            ((string)garplyFactory.New().Value).Should().Be("Garply()");
            ((string)garplyFactory.New(123).Value).Should().Be("Garply(int i)");
            ((string)garplyFactory.New("abc").Value).Should().Be("Garply(string s)");
        }

        [Fact]
        public void CanGetAndSetDelegateValue()
        {
            var waldo = new Waldo().Unlock();

            object foo = waldo._foo;

            foo.Should().BeOfType<EventHandler>();
        }

        [Fact]
        public void CanGetAndSetEnumValue()
        {
            var waldo = new Waldo().Unlock();

            object wobble = waldo._wobble;

            wobble.Should().BeOfType<Waldo.Wobble>();
            wobble.Should().Be(Waldo.Wobble.Wubble);

            waldo._wobble = Waldo.Wobble.Wibble;
            wobble = waldo._wobble;

            wobble.Should().Be(Waldo.Wobble.Wibble);
        }

        [Fact]
        public void CanGetPrivateEnumValue()
        {
            var fooType = Create.Class("Foo", Define.NestedEnum("Bar", "Baz", "Qux"));
            var Foo = fooType.Unlock();

            // Note that these variables are declared as object. (see below)
            object baz = Foo.Bar.Baz;
            object qux = Foo.Bar.Qux;

            // If the baz and qux variables had been declared dynamic,
            // then these conversions would fail.
            var bazInt = (int)baz;
            var quxInt = (int)qux;

            bazInt.Should().Be(0);
            quxInt.Should().Be(1);
        }

        [Fact]
        public void CanGetDefaultValueOfPrivateEnum()
        {
            var fooType = Create.Class("Foo", Define.NestedEnum("Bar", "Baz", "Qux"));
            var Foo = fooType.Unlock();

            // Note that the variable is declared as object. (see below)
            object defaultBar = Foo.Bar.New();

            // If the defaultBar variable had been declared dynamic,
            // then this conversion would fail.
            var defaultInt = (int)defaultBar;

            defaultInt.Should().Be(0);
        }

        [Fact]
        public void CanAccessNestedClass()
        {
            var fooType = Create.Class("Foo", Define.NestedClass("Bar"));

            var foo = fooType.Unlock();

            var bar = foo.Bar.New();

            ((object)bar.Value).GetType().Name.Should().Be("Bar");
        }

        [Fact]
        public void CanAccessDeeplyNestedClass()
        {
            var fooType =
                Create.Class("Foo",
                    Define.NestedClass("Bar",
                        Define.NestedClass("Baz",
                            Define.NestedClass("Qux",
                                Define.NestedClass("Grault",
                                    Define.NestedClass("Garply"))))));

            var foo = fooType.Unlock();

            var garply = foo.Bar.Baz.Qux.Grault.Garply.New();

            ((object)garply.Value).GetType().Name.Should().Be("Garply");
        }

        [Fact]
        public void CanAccessNestedStruct()
        {
            var fooType = Create.Class("Foo", Define.NestedStruct("Bar"));

            var foo = fooType.Unlock();

            var bar = foo.Bar.New();

            ((object)bar.Value).GetType().Name.Should().Be("Bar");
        }

        [Theory]
        [InlineData(typeof(Pork), typeof(Pork), 0)]
        [InlineData(typeof(Pork), typeof(IPork), 1)]
        [InlineData(typeof(Ham), typeof(Ham), 0)]
        [InlineData(typeof(Ham), typeof(Pork), 1)] // «═╗ Potential
        [InlineData(typeof(Ham), typeof(IHam), 1)] // «═╝ conflict
        [InlineData(typeof(Ham), typeof(IPork), 2)]
        [InlineData(typeof(CountryHam), typeof(CountryHam), 0)]
        [InlineData(typeof(CountryHam), typeof(ICountryHam), 1)] // «═╗ Potential
        [InlineData(typeof(CountryHam), typeof(Ham), 1)] // «═════════╝ conflict
        [InlineData(typeof(CountryHam), typeof(Pork), 2)] // «═╗ Potential
        [InlineData(typeof(CountryHam), typeof(IHam), 2)] // «═╝ conflict
        [InlineData(typeof(CountryHam), typeof(IPork), 3)]

        [InlineData(typeof(string), typeof(IHam), ushort.MaxValue)]
        [InlineData(typeof(string), typeof(Ham), ushort.MaxValue)]
        [InlineData(typeof(string), typeof(int), ushort.MaxValue)]
        public void AncestorDistanceIsCalculatedCorrectlyForInterfacesAndClasses(Type type, Type ancestorType, int expectedDistance)
        {
            var candidate = typeof(UniversalMemberAccessor).Unlock().Candidate;

            int distance = candidate.GetAncestorDistance(type, ancestorType);

            distance.Should().Be(expectedDistance);
        }

        [Theory]
        [InlineData(typeof(sbyte), typeof(sbyte), 0)]
        [InlineData(typeof(sbyte), typeof(short), 1)]
        [InlineData(typeof(sbyte), typeof(int), 2)]
        [InlineData(typeof(sbyte), typeof(long), 3)]
        [InlineData(typeof(sbyte), typeof(float), 4)]
        [InlineData(typeof(sbyte), typeof(double), 5)]
        [InlineData(typeof(sbyte), typeof(decimal), 6)]

        [InlineData(typeof(byte), typeof(byte), 0)]
        [InlineData(typeof(byte), typeof(short), 1)]
        [InlineData(typeof(byte), typeof(ushort), 2)]
        [InlineData(typeof(byte), typeof(int), 3)]
        [InlineData(typeof(byte), typeof(uint), 4)]
        [InlineData(typeof(byte), typeof(long), 5)]
        [InlineData(typeof(byte), typeof(ulong), 6)]
        [InlineData(typeof(byte), typeof(float), 7)]
        [InlineData(typeof(byte), typeof(double), 8)]
        [InlineData(typeof(byte), typeof(decimal), 9)]

        [InlineData(typeof(short), typeof(short), 0)]
        [InlineData(typeof(short), typeof(int), 1)]
        [InlineData(typeof(short), typeof(long), 2)]
        [InlineData(typeof(short), typeof(float), 3)]
        [InlineData(typeof(short), typeof(double), 4)]
        [InlineData(typeof(short), typeof(decimal), 5)]

        [InlineData(typeof(ushort), typeof(ushort), 0)]
        [InlineData(typeof(ushort), typeof(int), 1)]
        [InlineData(typeof(ushort), typeof(uint), 2)]
        [InlineData(typeof(ushort), typeof(long), 3)]
        [InlineData(typeof(ushort), typeof(ulong), 4)]
        [InlineData(typeof(ushort), typeof(float), 5)]
        [InlineData(typeof(ushort), typeof(double), 6)]
        [InlineData(typeof(ushort), typeof(decimal), 7)]

        [InlineData(typeof(char), typeof(ushort), 1)]
        [InlineData(typeof(char), typeof(int), 2)]
        [InlineData(typeof(char), typeof(uint), 3)]
        [InlineData(typeof(char), typeof(long), 4)]
        [InlineData(typeof(char), typeof(ulong), 5)]
        [InlineData(typeof(char), typeof(float), 6)]
        [InlineData(typeof(char), typeof(double), 7)]
        [InlineData(typeof(char), typeof(decimal), 8)]

        [InlineData(typeof(int), typeof(int), 0)]
        [InlineData(typeof(int), typeof(long), 1)]
        [InlineData(typeof(int), typeof(float), 2)]
        [InlineData(typeof(int), typeof(double), 3)]
        [InlineData(typeof(int), typeof(decimal), 4)]

        [InlineData(typeof(uint), typeof(uint), 0)]
        [InlineData(typeof(uint), typeof(long), 1)]
        [InlineData(typeof(uint), typeof(ulong), 2)]
        [InlineData(typeof(uint), typeof(float), 3)]
        [InlineData(typeof(uint), typeof(double), 4)]
        [InlineData(typeof(uint), typeof(decimal), 5)]

        [InlineData(typeof(long), typeof(long), 0)]
        [InlineData(typeof(long), typeof(float), 1)]
        [InlineData(typeof(long), typeof(double), 2)]
        [InlineData(typeof(long), typeof(decimal), 3)]

        [InlineData(typeof(ulong), typeof(ulong), 0)]
        [InlineData(typeof(ulong), typeof(float), 1)]
        [InlineData(typeof(ulong), typeof(double), 2)]
        [InlineData(typeof(ulong), typeof(decimal), 3)]

        [InlineData(typeof(float), typeof(float), 0)]
        [InlineData(typeof(float), typeof(double), 1)]
        [InlineData(typeof(float), typeof(decimal), 2)]

        [InlineData(typeof(double), typeof(double), 0)]
        [InlineData(typeof(double), typeof(decimal), 1)]

        [InlineData(typeof(decimal), typeof(decimal), 0)]
        public void AncestorDistanceIsCalculatedCorrectlyForNumericTypes(Type type, Type ancestorType, int expectedDistance)
        {
            var candidate = typeof(UniversalMemberAccessor).Unlock().Candidate;

            int distance = candidate.GetAncestorDistance(type, ancestorType);

            distance.Should().Be(expectedDistance);
        }

        [Theory]
        [InlineData(typeof(string), typeof(object), false/*, TestName="object ancestor returns false"*/)]
        [InlineData(typeof(string), typeof(int), false/*, TestName="struct ancestor returns false"*/)]
        [InlineData(typeof(Foo), typeof(string), false/*, TestName="unrelated types returns false"*/)]
        [InlineData(typeof(Ham), typeof(IHam), true/*, TestName="implemented interface returns true"*/)]
        [InlineData(typeof(Ham), typeof(Pork), true/*, TestName="inherited class returns true"*/)]
        public void HasAncestorReturnsTheCorrectValue(Type type, Type ancestorType, bool expectedValue)
        {
            var candidate = typeof(UniversalMemberAccessor).Unlock().Candidate;

            bool hasAncestor = candidate.HasAncestor(type, ancestorType);

            hasAncestor.Should().Be(expectedValue);
        }

        [Theory]
        [InlineData(typeof(int), typeof(int), typeof(int), 0)]
        [InlineData(typeof(int), typeof(short), typeof(int), 1)]
        [InlineData(typeof(short), typeof(int), typeof(int), -32767)]
        [InlineData(typeof(int), typeof(object), typeof(int), 1)]
        [InlineData(typeof(object), typeof(int), typeof(int), -32767)]
        [InlineData(typeof(Ham), typeof(object), typeof(CountryHam), 1)]
        [InlineData(typeof(object), typeof(Ham), typeof(CountryHam), -32767)]
        [InlineData(typeof(short), typeof(int), typeof(byte), 1)]
        [InlineData(typeof(int), typeof(short), typeof(byte), -32767)]
        [InlineData(typeof(Ham), typeof(Pork), typeof(CountryHam), 1)]
        [InlineData(typeof(Pork), typeof(Ham), typeof(CountryHam), -32767)]
        [InlineData(typeof(Ham), typeof(string), typeof(CountryHam), 1)]
        [InlineData(typeof(string), typeof(Ham), typeof(CountryHam), -32767)]
        public void AccumulateScoreModifiesScoreCorrectly(Type thisType, Type otherType, Type argType, int expectedScore)
        {
            var candidate = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var score = 0;
            candidate.AccumulateScore(thisType, otherType, argType, ref score);

            score.Should().Be(expectedScore);
        }

        [Fact]
        public void IsLegalReturnsFalseIfNotEnoughRequiredArgumentsAreSupplied()
        {
            var type = Create.Class("Foo",
                Define.Constructor(
                    new Parameter(typeof(string)),
                    new Parameter(-1, typeof(int)),
                    new Parameter(true, typeof(bool))));

            var constructor = type.GetConstructors()[0];

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(constructor);

            bool isLegal = candidate.IsLegal(Array.Empty<Type>(), Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void IsLegalReturnsTrueIfAllArgumentsAreSupplied()
        {
            var type = Create.Class("Foo",
                Define.Constructor(
                    new Parameter(typeof(string)),
                    new Parameter(-1, typeof(int)),
                    new Parameter(true, typeof(bool))));

            var constructor = type.GetConstructors()[0];

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(constructor);

            bool isLegal = candidate.IsLegal(new[] { typeof(string), typeof(int), typeof(bool) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfNoOptionalArgumentsAreSupplied()
        {
            var type = Create.Class("Foo",
                Define.Constructor(
                    new Parameter(typeof(string)),
                    new Parameter(-1, typeof(int)),
                    new Parameter(true, typeof(bool))));

            var constructor = type.GetConstructors()[0];

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(constructor);

            bool isLegal = candidate.IsLegal(new[] { typeof(string) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfSomeOptionalArgumentsAreSupplied()
        {
            var type = Create.Class("Foo",
                Define.Constructor(
                    new Parameter(typeof(string)),
                    new Parameter(-1, typeof(int)),
                    new Parameter(true, typeof(bool))));

            var constructor = type.GetConstructors()[0];

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(constructor);

            bool isLegal = candidate.IsLegal(new[] { typeof(string), typeof(int) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

#pragma warning disable CA1822 // Mark members as static
#pragma warning disable xUnit1013 // Public method should be marked as test
        public void Generic<T>(T t)
#pragma warning restore CA1822 // Mark members as static
        {
        }

        [Fact]
        public void IsLegalReturnsFalseIfTheArgTypeIsNotAssignableToTheTypeArgument()
        {
            var method = GetType().GetMethod("Generic");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(int) }, new[] { typeof(byte) });

            isLegal.Should().BeFalse();
        }

#pragma warning disable CA1822 // Mark members as static
        public void NewConstraint<T>(T t)
#pragma warning restore CA1822 // Mark members as static
            where T : new()
        {
        }

        [Fact]
        public void IsLegalReturnsTrueIfAnObjectWithADefaultConstructorIsMatchedWithTheNewContraint()
        {
            var method = GetType().GetMethod("NewConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Ham) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfATypeParameterClassWithADefaultConstructorIsMatchedWithTheNewContraint2()
        {
            var method = GetType().GetMethod("NewConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(CountryHam) }, new[] { typeof(Ham) });

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfAValueTypeValueIsMatchedWithTheNewContraint()
        {
            var method = GetType().GetMethod("NewConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(int) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfATypeParameterValueTypeIsMatchedWithTheNewContraint()
        {
            var method = GetType().GetMethod("NewConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(int) }, new[] { typeof(long) });

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAnObjectWithoutADefaultConstructorIsMatchedWithTheNewContraint()
        {
            var method = GetType().GetMethod("NewConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Lard) }, Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void IsLegalReturnsFalseIfATypeParameterClassWithoutADefaultConstructorIsMatchedWithTheNewContraint()
        {
            var method = GetType().GetMethod("NewConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Bacon) }, new[] { typeof(Lard) });

            isLegal.Should().BeFalse();
        }

#pragma warning disable CA1822 // Mark members as static
        public void ClassConstraint<T>(T t)
#pragma warning restore CA1822 // Mark members as static
            where T : class
        {
        }

        [Fact]
        public void IsLegalReturnsTrueIfAnObjectIsMatchedWithTheClassContraint()
        {
            var method = GetType().GetMethod("ClassConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(string) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfATypeParameterClassIsMatchedWithTheClassContraint()
        {
            var method = GetType().GetMethod("ClassConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(string) }, new[] { typeof(string) });

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAValueTypeValueIsMatchedWithTheClassContraint()
        {
            var method = GetType().GetMethod("ClassConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(int) }, Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void IsLegalReturnsFalseIfTypeParameterStructIsMatchedWithTheClassContraint()
        {
            var method = GetType().GetMethod("ClassConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(byte) }, new[] { typeof(int) });

            isLegal.Should().BeFalse();
        }

#pragma warning disable CA1822 // Mark members as static
        public void StructConstraint<T>(T t)
#pragma warning restore CA1822 // Mark members as static
            where T : struct
        {
        }

        [Fact]
        public void IsLegalReturnsTrueIfAValueTypeValueIsMatchedWithTheStructContraint()
        {
            var method = GetType().GetMethod("StructConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(int) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfATypeParameterValueTypeIsMatchedWithTheStructContraint()
        {
            var method = GetType().GetMethod("StructConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(int) }, new[] { typeof(int) });

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAnObjectIsMatchedWithTheStructContraint()
        {
            var method = GetType().GetMethod("StructConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(string) }, Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAnTypeParameterClassIsMatchedWithTheStructContraint()
        {
            var method = GetType().GetMethod("StructConstraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(string) }, new[] { typeof(string) });

            isLegal.Should().BeFalse();
        }

#pragma warning disable CA1822 // Mark members as static
        public void BaseClassContraint<THam>(THam tHam)
#pragma warning restore CA1822 // Mark members as static
            where THam : Ham
        {
        }

        [Fact]
        public void IsLegalReturnsTrueIfADerivedObjectIsMatchedWithItsBaseClassContraint()
        {
            var method = GetType().GetMethod("BaseClassContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(CountryHam) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfATypeParameterDerivedClassIsMatchedWithItsBaseClassContraint()
        {
            var method = GetType().GetMethod("BaseClassContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(CountryHam) }, new[] { typeof(CountryHam) });

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAnUnrelatedObjectIsMatchedWithABaseClassContraint()
        {
            var method = GetType().GetMethod("BaseClassContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Spam) }, Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAnUnrelatedTypeParameterClassIsMatchedWithABaseClassContraint()
        {
            var method = GetType().GetMethod("BaseClassContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Spam) }, new[] { typeof(Spam) });

            isLegal.Should().BeFalse();
        }

#pragma warning disable CA1822 // Mark members as static
        public void InterfaceContraint<THam>(THam tHam)
#pragma warning restore CA1822 // Mark members as static
            where THam : IHam
        {
        }

        [Fact]
        public void IsLegalReturnsTrueIfAnInterfaceImplementationObjectIsMatchedWithItsInterfaceContraint()
        {
            var method = GetType().GetMethod("InterfaceContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Ham) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueIfATypeParameterInterfaceImplementationIsMatchedWithItsInterfaceContraint()
        {
            var method = GetType().GetMethod("InterfaceContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Ham) }, new[] { typeof(Ham) });

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsFalseIfAnUnrelatedClassIsMatchedWithAnInterfaceContraint()
        {
            var method = GetType().GetMethod("InterfaceContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Spam) }, Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void IsLegalReturnsFalseIfATypeParameterUnrelatedClassIsMatchedWithAnInterfaceContraint()
        {
            var method = GetType().GetMethod("InterfaceContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Spam) }, new[] { typeof(Spam) });

            isLegal.Should().BeFalse();
        }

#pragma warning disable CA1822 // Mark members as static
        public void TypeParameterContraint<TBase, TDerived>(TBase tBase, TDerived tDerived)
#pragma warning restore CA1822 // Mark members as static
            where TDerived : TBase
        {
        }

        [Fact]
        public void IsLegalReturnsTrueInATypeParameterConstraintScenario1()
        {
            var method = GetType().GetMethod("TypeParameterContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Ham), typeof(CountryHam) }, Type.EmptyTypes);

            isLegal.Should().BeTrue();
        }

        [Fact]
        public void IsLegalReturnsTrueInATypeParameterConstraintScenario2()
        {
            var method = GetType().GetMethod("TypeParameterContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Ham), typeof(CountryHam) }, new[] { typeof(Ham), typeof(CountryHam) });

            isLegal.Should().BeTrue();
        }

        [Fact(Skip = "Gives a false positive - no good (read: simple) way of knowing the relationship of one generic paramter to another.")]
        public void IsLegalReturnsFalseInANonTypeParameterConstraintScenario()
        {
            var method = GetType().GetMethod("TypeParameterContraint");

            var candidateFactory = typeof(UniversalMemberAccessor).Unlock().Candidate;

            var candidate = candidateFactory.New(method);

            bool isLegal = candidate.IsLegal(new[] { typeof(Ham), typeof(Spam) }, Type.EmptyTypes);

            isLegal.Should().BeFalse();
        }

        [Fact]
        public void CanCallConstructorWithDefaultValueParameterWithAndWithoutSpecifyingIt()
        {
            var type = Create.Class("Foo",
                Define.Constructor(typeof(string), new Parameter(-1, typeof(int))));

            Action act = () => type.New("abc", 123);
            act.Should().NotThrow();

            act = () => type.New("abc");
            act.Should().NotThrow();
        }

        [Fact]
        public void CanFindTheBestConstructorWhenDefaultParametersAreInvolved()
        {
            var type = Create.Class("Foo",
                Define.Constructor(
                    new Parameter(typeof(string), "_bar"),
                    new Parameter(-1, typeof(int)),
                    new Parameter(true, typeof(bool))),
                Define.Constructor(
                    new Parameter(typeof(object), "_baz"),
                    new Parameter(-1, typeof(int))),
                Define.AutoProperty("Bar", typeof(string), backingFieldName: "_bar"),
                Define.AutoProperty("Baz", typeof(object), backingFieldName: "_baz"));

            object parameter = "abc";
            var foo1 = type.New(parameter);

            ((object)foo1.Bar).Should().Be(parameter);
            ((object)foo1.Baz).Should().BeNull();

            parameter = 123.45;
            var foo2 = type.New(parameter);

            ((object)foo2.Bar).Should().BeNull();
            ((object)foo2.Baz.Value).Should().Be(parameter);
        }

        [Fact]
        public void CanCallMethodWithDefaultValueParameterWithAndWithoutSpecifyingIt()
        {
            var type = Create.Class("Foo",
                Define.Method("Bar", typeof(string), new Parameter(-1, typeof(int))));

            var foo = type.New();

            Action act = () => foo.Bar("abc", 123);
            act.Should().NotThrow();

            act = () => foo.Bar("abc");
            act.Should().NotThrow();
        }

        [Fact]
        public void CanFindTheBestOverloadedMethodWhenDefaultParametersAreInvolved()
        {
            var type = Create.Class("Foo",
                Define.Method("Qux",
                    new Parameter(typeof(string), "_bar"),
                    new Parameter(-1, typeof(int)),
                    new Parameter(true, typeof(bool))),
                Define.Method("Qux",
                    new Parameter(typeof(object), "_baz"),
                    new Parameter(-1, typeof(int))),
                Define.AutoProperty("Bar", typeof(string), backingFieldName: "_bar"),
                Define.AutoProperty("Baz", typeof(object), backingFieldName: "_baz"));

            var foo = type.New();

            object parameter = "abc";
            foo.Qux(parameter);

            ((object)foo.Bar).Should().Be(parameter);
            ((object)foo.Baz).Should().BeNull();

            foo = type.New();

            parameter = 123.45;
            foo.Qux(parameter);

            ((object)foo.Bar).Should().BeNull();
            ((object)foo.Baz.Value).Should().Be(parameter);
        }

        [Fact]
        public void WhenResolvingMethodsAnExceptionIsThrownWhenTheAncestorDistanceIsTheSame1()
        {
            var spam = new Spam();

            // Demonstrate behavior in dynamic variable that points to a regular object.
            dynamic lockedSpam = spam;

            // Call each method with a good match.
            Action act = () => lockedSpam.PublicFoo(new Pork());
            act.Should().NotThrow();

            act = () => lockedSpam.PublicFoo(new BadActor());
            act.Should().NotThrow();


            // Ambiguous match - Ham has the same ancestor distance to Pork and IHam.
            act = () => lockedSpam.PublicFoo(new Ham());
            act.Should().ThrowExactly<RuntimeBinderException>();


            // Unlock the object and verify that calling its private methods exhibits identical behavior.
            dynamic unlockedSpam = spam.Unlock();

            act = () => unlockedSpam.PrivateFoo(new Ham());
            act.Should().ThrowExactly<RuntimeBinderException>();

            act = () => unlockedSpam.PrivateFoo(new Pork());
            act.Should().NotThrow();

            act = () => unlockedSpam.PrivateFoo(new BadActor());
            act.Should().NotThrow();
        }

        [Fact]
        public void WhenResolvingMethodsAnExceptionIsThrownWhenTheAncestorDistanceIsTheSame2()
        {
            var spam = new Spam();

            // Demonstrate behavior in dynamic variable that points to a regular object.
            dynamic lockedSpam = spam;

            Action act = () => lockedSpam.PublicFoo(new CountryHam());
            act.Should().ThrowExactly<RuntimeBinderException>();

            act = () => lockedSpam.PublicFoo(new Pork());
            act.Should().NotThrow();

            act = () => lockedSpam.PublicFoo(new BadActor());
            act.Should().NotThrow();

            // Unlock the object and verify that calling its private methods exhibits identical behavior.
            dynamic unlockedSpam = new Spam().Unlock();

            act = () => unlockedSpam.PrivateFoo(new CountryHam());
            act.Should().ThrowExactly<RuntimeBinderException>();

            act = () => unlockedSpam.PrivateFoo(new Pork());
            act.Should().NotThrow();

            act = () => unlockedSpam.PrivateFoo(new BadActor());
            act.Should().NotThrow();
        }

        [Fact]
        public void WhenResolvingMethodsAnExceptionIsThrownWhenTheAncestorDistanceIsTheSame3()
        {
            var spam = new Spam();

            // Demonstrate behavior in dynamic variable that points to a regular object.
            dynamic lockedSpam = spam;

            Action act = () => lockedSpam.PublicBar(new CountryHam());
            act.Should().ThrowExactly<RuntimeBinderException>();

            act = () => lockedSpam.PublicBar(new Ham());
            act.Should().NotThrow();

            act = () => lockedSpam.PublicBar(new Prosciutto());
            act.Should().NotThrow();

            // Unlock the object and verify that calling its private methods exhibits identical behavior.
            dynamic unlockedSpam = new Spam().Unlock();

            act = () => unlockedSpam.PrivateBar(new CountryHam());
            act.Should().ThrowExactly<RuntimeBinderException>();

            act = () => unlockedSpam.PrivateBar(new Ham());
            act.Should().NotThrow();

            act = () => unlockedSpam.PrivateBar(new Prosciutto());
            act.Should().NotThrow();
        }

        [Fact]
        public void CreateInstanceDefinitionEqualsReturnsTrueWhenReferencesAreEqual()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = definition1;

            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void CreateInstanceDefinitionEqualsReturnsFalseWhenOtherTypeIsNotCreateInstanceDefinition()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = "abcd";

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void CreateInstanceDefinitionEqualsReturnsFalseWhenOtherHasDifferentType()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = type.New(typeof(string), new[] { typeof(string), typeof(bool) }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void CreateInstanceDefinitionEqualsReturnsFalseWhenOtherHasDifferentNumberOfArgTypes()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = type.New(typeof(int), new[] { typeof(string) }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void CreateInstanceDefinitionEqualsReturnsFalseWhenOtherHasDifferentArgTypes()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = type.New(typeof(int), new[] { typeof(string), typeof(DateTime) }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void CreateInstanceDefinitionEqualsReturnsTrueWhenTypeAndArgTypesAreTheSame()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;

            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void CreateInstanceDefinitionGetHashCodeIsTheSameForEqualInstances()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;

            definition1.GetHashCode().Should().Be(definition2.GetHashCode());
            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void CreateInstanceDefinitionGetHashCodeIsTheSameForReferenceEqualInstances()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().CreateInstanceDefinition;

            object definition1 = type.New(typeof(int), new[] { typeof(string), typeof(bool) }).Value;
            object definition2 = definition1;

            definition1.GetHashCode().Should().Be(definition2.GetHashCode());
            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsTrueWhenReferencesAreEqual()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;
            object definition2 = definition1;

            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherTypeIsNotInvokeMethodDefinition()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;
            object definition2 = "abcd";

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherHasDifferentType()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(string), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherHasDifferentName()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Bar", Type.EmptyTypes, new object[] { "abc", true }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherHasDifferentNumberOfArgTypes()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc" }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherHasDifferentArgTypes()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Foo", Type.EmptyTypes, new object[] { "abc", DateTime.Now }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherHasDifferentNumberOfTypeArguments()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", new[] { typeof(int) }, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc" }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsFalseWhenOtherHasDifferentTypeArguments()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(int) }, new object[] { "abc", DateTime.Now }).Value;

            definition1.Equals(definition2).Should().BeFalse();
        }

        [Fact]
        public void InvokeMethodDefinitionEqualsReturnsTrueWhenTypeAndArgTypesAreTheSame()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc", true }).Value;

            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void InvokeMethodDefinitionGetHashCodeIsTheSameForEqualInstances()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc", true }).Value;
            object definition2 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc", true }).Value;

            definition1.GetHashCode().Should().Be(definition2.GetHashCode());
            definition1.Equals(definition2).Should().BeTrue();
        }

        [Fact]
        public void InvokeMethodDefinitionGetHashCodeIsTheSameForReferenceEqualInstances()
        {
            var type = typeof(UniversalMemberAccessor).Unlock().InvokeMethodDefinition;

            object definition1 = type.New(typeof(int), "Foo", new[] { typeof(int), typeof(string) }, new object[] { "abc", true }).Value;
            object definition2 = definition1;

            definition1.GetHashCode().Should().Be(definition2.GetHashCode());
            definition1.Equals(definition2).Should().BeTrue();
        }

        // TODO: Add "proper" support for nested type.
        // TODO: add feature: explicitly implemented interface members
        //       TODO: figure out how to deal with explicit member with same name as other member of same name
        //       http://stackoverflow.com/a/17854048/252004
        //       http://stackoverflow.com/questions/7379276/how-to-find-if-a-method-is-implementing-specific-interface
        // TODO: add feature: support for method hiding
        //       TODO: figure out how to deal with deciding which member (hiding or hidden) to select
        // TODO: add feature: support for calling extension methods dynamically. May need to have "specify namespace" functionality for this to work.

        [Fact]
        public void CanCallGenericMethodWithExplicitStructGenericArgument()
        {
            var foo = new GenericFoo().Unlock();

            int bar = foo.Bar<int>("foo", 123);

            bar.Should().Be(123);
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitClassGenericArgument()
        {
            var foo = new GenericFoo().Unlock();

            object bar = foo.Bar<Ham>("foo", new Ham()).Value;

            bar.Should().BeOfType<Ham>();
        }

        [Fact]
        public void CanCallGenericMethodWithImplicitStructGenericArgument()
        {
            var foo = new GenericFoo().Unlock();

            int bar = foo.Bar("foo", 123);

            bar.Should().Be(123);
        }

        [Fact]
        public void CanCallGenericMethodWithImplicitClassGenericArgument()
        {
            var foo = new GenericFoo().Unlock();

            object bar = foo.Bar("foo", new Ham()).Value;

            bar.Should().BeOfType<Ham>();
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitStructGenericArgumentAndNoGenericParameter()
        {
            var foo = new GenericFoo().Unlock();

            int ham = foo.Baz<int>();

            ham.Should().Be(0);
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitClassGenericArgumentAndNoGenericParameter()
        {
            var foo = new GenericFoo().Unlock();

            object ham = foo.Baz<Ham>().Value;

            ham.Should().BeOfType<Ham>();
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitStructGenericArgumentAndGenericOutParameter()
        {
            var foo = new GenericFoo().Unlock();

            int outBar;
            int bar = foo.Qux<int>("foo", 123, out outBar);

            bar.Should().Be(123);
            outBar.Should().Be(123);
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitStructGenericArgumentAndGenericRefParameter()
        {
            var foo = new GenericFoo().Unlock();

            var outBar = 456;
            int bar = foo.Qux<int>("foo", 123, ref outBar);

            bar.Should().Be(123);
            outBar.Should().Be(123);
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitClassGenericArgumentAndGenericOutParameter()
        {
            var foo = new GenericFoo().Unlock();

            var ham = new Ham();

            Ham outBar;
            object bar = foo.Qux<Ham>("foo", ham, out outBar).Value;

            bar.Should().BeSameAs(ham);
            outBar.Should().BeSameAs(ham);
        }

        [Fact]
        public void CanCallGenericMethodWithExplicitClassGenericArgumentAndGenericRefParameter()
        {
            var foo = new GenericFoo().Unlock();

            var ham = new Ham();

            var outBar = new Ham();
            object bar = foo.Qux<Ham>("foo", ham, ref outBar).Value;

            bar.Should().BeSameAs(ham);
            outBar.Should().BeSameAs(ham);
        }

        [Fact]
        public void CanCallGenericMethodWithImplicitStructGenericArgumentAndGenericOutParameter()
        {
            var foo = new GenericFoo().Unlock();

            int outBar;
            int bar = foo.Qux("foo", 123, out outBar);

            bar.Should().Be(123);
            outBar.Should().Be(123);
        }

        [Fact]
        public void CanCallGenericMethodWithImplicitStructGenericArgumentAndGenericRefParameter()
        {
            var foo = new GenericFoo().Unlock();

            var outBar = 456;
            int bar = foo.Qux("foo", 123, ref outBar);

            bar.Should().Be(123);
            outBar.Should().Be(123);
        }

        [Fact]
        public void CanCallGenericMethodWithImplicitClassGenericArgumentAndGenericOutParameter()
        {
            var foo = new GenericFoo().Unlock();

            var ham = new Ham();

            Ham outBar;
            object bar = foo.Qux("foo", ham, out outBar).Value;

            bar.Should().BeSameAs(ham);
            outBar.Should().BeSameAs(ham);
        }

        [Fact]
        public void CanCallGenericMethodWithImplicitClassGenericArgumentAndGenericRefParameter()
        {
            var foo = new GenericFoo().Unlock();

            var ham = new Ham();

            var outBar = new Ham();
            object bar = foo.Qux("foo", ham, ref outBar).Value;

            bar.Should().BeSameAs(ham);
            outBar.Should().BeSameAs(ham);
        }

        [Fact]
        public void CanAccessPrivateMembersOfBaseClass()
        {
            var obj = new DerivedClassFromBaseClassWithPrivateMembers();
            var proxy = obj.Unlock();

            int foo = proxy._foo;
            string bar = proxy.Bar();
            double baz = proxy.Baz;

            foo.Should().Be(obj.GetBaseFoo());
            bar.Should().Be(obj.GetBaseBar());
            baz.Should().Be(obj.GetBaseBaz());

            proxy._foo = -1;
            proxy.Baz = -543.21;

            obj.GetBaseFoo().Should().Be(-1);
            obj.GetBaseBaz().Should().Be(-543.21);
        }

        [Fact]
        public void CannotAccessPrivateMembersOfBaseClassWhenNameConflictsWithDerivedClass()
        {
            var obj = new DerivedClassWithConflictingPrivateMembers();
            var proxy = obj.Unlock();

            int foo = proxy._foo;
            string bar = proxy.Bar();
            double baz = proxy.Baz;

            foo.Should().NotBe(obj.GetBaseFoo());
            bar.Should().NotBe(obj.GetBaseBar());
            baz.Should().NotBe(obj.GetBaseBaz());

            foo.Should().Be(obj.GetDerivedFoo());
            bar.Should().Be(obj.GetDerivedBar());
            baz.Should().Be(obj.GetDerivedBaz());

            proxy._foo = -1;
            proxy.Baz = -543.21;

            obj.GetBaseFoo().Should().NotBe(-1);
            obj.GetBaseBaz().Should().NotBe(-543.21);

            obj.GetDerivedFoo().Should().Be(-1);
            obj.GetDerivedBaz().Should().Be(-543.21);
        }

        [Fact]
        public void CanAccessPrivateMembersOfBaseClassWhenNameConflictsWithDerivedClassIfBasePseudoPropertyIsUsed()
        {
            var obj = new DerivedClassWithConflictingPrivateMembers();
            var proxy = obj.Unlock();

            int foo = proxy.Base._foo;
            string bar = proxy.Base.Bar();
            double baz = proxy.Base.Baz;

            foo.Should().Be(obj.GetBaseFoo());
            bar.Should().Be(obj.GetBaseBar());
            baz.Should().Be(obj.GetBaseBaz());

            foo.Should().NotBe(obj.GetDerivedFoo());
            bar.Should().NotBe(obj.GetDerivedBar());
            baz.Should().NotBe(obj.GetDerivedBaz());

            proxy.Base._foo = -1;
            proxy.Base.Baz = -543.21;

            obj.GetBaseFoo().Should().Be(-1);
            obj.GetBaseBaz().Should().Be(-543.21);

            obj.GetDerivedFoo().Should().NotBe(-1);
            obj.GetDerivedBaz().Should().NotBe(-543.21);
        }

        [Fact]
        public void CanAccessPrivateMembersOfBaseClassWhenNameConflictsWithDerivedClassIfBaseTypePseudoPropertyIsUsed()
        {
            var obj = new DerivedClassWithConflictingPrivateMembers();
            var proxy = obj.Unlock();

            int foo = proxy.BaseType._foo;
            string bar = proxy.BaseType.Bar();
            double baz = proxy.BaseType.Baz;

            foo.Should().Be(obj.GetBaseFoo());
            bar.Should().Be(obj.GetBaseBar());
            baz.Should().Be(obj.GetBaseBaz());

            foo.Should().NotBe(obj.GetDerivedFoo());
            bar.Should().NotBe(obj.GetDerivedBar());
            baz.Should().NotBe(obj.GetDerivedBaz());

            proxy.BaseType._foo = -1;
            proxy.BaseType.Baz = -543.21;

            obj.GetBaseFoo().Should().Be(-1);
            obj.GetBaseBaz().Should().Be(-543.21);

            obj.GetDerivedFoo().Should().NotBe(-1);
            obj.GetDerivedBaz().Should().NotBe(-543.21);
        }

        [Fact]
        public void CanAccessPrivateMembersOfBaseClassWhenNameConflictsWithDerivedClassIfBaseClassPseudoPropertyIsUsed()
        {
            var obj = new DerivedClassWithConflictingPrivateMembers();
            var proxy = obj.Unlock();

            int foo = proxy.BaseClass._foo;
            string bar = proxy.BaseClass.Bar();
            double baz = proxy.BaseClass.Baz;

            foo.Should().Be(obj.GetBaseFoo());
            bar.Should().Be(obj.GetBaseBar());
            baz.Should().Be(obj.GetBaseBaz());

            foo.Should().NotBe(obj.GetDerivedFoo());
            bar.Should().NotBe(obj.GetDerivedBar());
            baz.Should().NotBe(obj.GetDerivedBaz());

            proxy.BaseClass._foo = -1;
            proxy.BaseClass.Baz = -543.21;

            obj.GetBaseFoo().Should().Be(-1);
            obj.GetBaseBaz().Should().Be(-543.21);

            obj.GetDerivedFoo().Should().NotBe(-1);
            obj.GetDerivedBaz().Should().NotBe(-543.21);
        }

        [Fact]
        public void GenericMethodsAreResolvedCorrectly()
        {
            var foo = new NonGenericFoo().Unlock();

            int bar1 = 123;
            int bar1ValueA = foo.Bar1<int>(bar1);
            int bar1ValueB = foo.Bar1(bar1);
            bar1ValueA.Should().Be(bar1);
            bar1ValueB.Should().Be(bar1);

            Lazy<int> baz1 = new Lazy<int>(() => 234);
            Lazy<int> baz1ValueA = foo.Baz1<int>(baz1);
            Lazy<int> baz1ValueB = foo.Baz1(baz1);
            baz1ValueA.Should().BeSameAs(baz1);
            baz1ValueB.Should().BeSameAs(baz1);

            Func<Lazy<int>> qux1 = () => new Lazy<int>(() => 345);
            Func<Lazy<int>> qux1ValueA = foo.Qux1<int>(qux1);
            Func<Lazy<int>> qux1ValueB = foo.Qux1(qux1);
            qux1ValueA.Should().BeSameAs(qux1);
            qux1ValueB.Should().BeSameAs(qux1);

            int bar2T = 123;
            string bar2U = "abc";
            Tuple<int, string> bar2A = foo.Bar2<int, string>(bar2T, bar2U);
            Tuple<int, string> bar2B = foo.Bar2(bar2T, bar2U);
            bar2A.Item1.Should().Be(bar2T);
            bar2A.Item2.Should().BeSameAs(bar2U);
            bar2B.Item1.Should().Be(bar2T);
            bar2B.Item2.Should().BeSameAs(bar2U);

            Lazy<int> baz2T = new Lazy<int>(() => 234);
            Lazy<string> baz2U = new Lazy<string>(() => "bcd");
            Tuple<Lazy<int>, Lazy<string>> baz2A = foo.Baz2<int, string>(baz2T, baz2U);
            Tuple<Lazy<int>, Lazy<string>> baz2B = foo.Baz2(baz2T, baz2U);
            baz2A.Item1.Should().BeSameAs(baz2T);
            baz2A.Item2.Should().BeSameAs(baz2U);
            baz2B.Item1.Should().BeSameAs(baz2T);
            baz2B.Item2.Should().BeSameAs(baz2U);

            Func<Lazy<int>> qux2T = () => new Lazy<int>(() => 345);
            Func<Lazy<string>> qux2U = () => new Lazy<string>(() => "cde");
            Tuple<Func<Lazy<int>>, Func<Lazy<string>>> qux2A = foo.Qux2<int, string>(qux2T, qux2U);
            Tuple<Func<Lazy<int>>, Func<Lazy<string>>> qux2B = foo.Qux2(qux2T, qux2U);
            qux2A.Item1.Should().BeSameAs(qux2T);
            qux2A.Item2.Should().BeSameAs(qux2U);
            qux2B.Item1.Should().BeSameAs(qux2T);
            qux2B.Item2.Should().BeSameAs(qux2U);

            Tuple<int, string> bar2ReversedA = foo.Bar2Reversed<int, string>(bar2U, bar2T);
            Tuple<int, string> bar2ReversedB = foo.Bar2Reversed(bar2U, bar2T);
            bar2ReversedA.Item1.Should().Be(bar2T);
            bar2ReversedA.Item2.Should().BeSameAs(bar2U);
            bar2ReversedB.Item1.Should().Be(bar2T);
            bar2ReversedB.Item2.Should().BeSameAs(bar2U);

            Tuple<Lazy<int>, Lazy<string>> baz2ReversedA = foo.Baz2Reversed<int, string>(baz2U, baz2T);
            Tuple<Lazy<int>, Lazy<string>> baz2ReversedB = foo.Baz2Reversed(baz2U, baz2T);
            baz2ReversedA.Item1.Should().BeSameAs(baz2T);
            baz2ReversedA.Item2.Should().BeSameAs(baz2U);
            baz2ReversedB.Item1.Should().BeSameAs(baz2T);
            baz2ReversedB.Item2.Should().BeSameAs(baz2U);

            Tuple<Func<Lazy<int>>, Func<Lazy<string>>> qux2ReversedA = foo.Qux2Reversed<int, string>(qux2U, qux2T);
            Tuple<Func<Lazy<int>>, Func<Lazy<string>>> qux2ReversedB = foo.Qux2Reversed(qux2U, qux2T);
            qux2ReversedA.Item1.Should().BeSameAs(qux2T);
            qux2ReversedA.Item2.Should().BeSameAs(qux2U);
            qux2ReversedB.Item1.Should().BeSameAs(qux2T);
            qux2ReversedB.Item2.Should().BeSameAs(qux2U);
        }

        [Fact]
        public void GenericMethodsWithNullValuesAreResolvedCorrectly()
        {
            var foo = new NonGenericFoo().Unlock();

            var graultT1 = "abc";
            var graultT2 = new Lazy<string>(() => "abc");
            var graultT3 = new Func<string>(() => "abc");
            var graultT4 = new Lazy<Func<string>>(() => () => "abc");

            var graultResult1 = foo.Grault(graultT1, graultT2, graultT3, graultT4);
            ((object)graultResult1.Item1).Should().BeSameAs(graultT1);
            ((object)graultResult1.Item2.Value).Should().BeSameAs(graultT2.Value);
            ((object)graultResult1.Item3).Should().BeSameAs(graultT3);
            ((object)graultResult1.Item4.Value).Should().BeSameAs(graultT4.Value);

            var graultResult2 = foo.Grault(graultT1, null, null, null);
            ((object)graultResult2.Item1).Should().BeSameAs(graultT1);
            ((object)graultResult2.Item2).Should().BeSameAs(null);
            ((object)graultResult2.Item3).Should().BeSameAs(null);
            ((object)graultResult2.Item4).Should().BeSameAs(null);

            var graultResult3 = foo.Grault(null, graultT2, null, null);
            ((object)graultResult3.Item1).Should().BeSameAs(null);
            ((object)graultResult3.Item2.Value).Should().BeSameAs(graultT2.Value);
            ((object)graultResult3.Item3).Should().BeSameAs(null);
            ((object)graultResult3.Item4).Should().BeSameAs(null);

            var graultResult4 = foo.Grault(null, null, graultT3, null);
            ((object)graultResult4.Item1).Should().BeSameAs(null);
            ((object)graultResult4.Item2).Should().BeSameAs(null);
            ((object)graultResult4.Item3).Should().BeSameAs(graultT3);
            ((object)graultResult4.Item4).Should().BeSameAs(null);

            var graultResult5 = foo.Grault(null, null, null, graultT4);
            ((object)graultResult5.Item1).Should().BeSameAs(null);
            ((object)graultResult5.Item2).Should().BeSameAs(null);
            ((object)graultResult5.Item3).Should().BeSameAs(null);
            ((object)graultResult5.Item4.Value).Should().BeSameAs(graultT4.Value);

            Action act = () => foo.Grault(null, null, null, null);
            act.Should().Throw<Exception>();

            var garplyT1 = "5/11/2020";
            var garplyT2 = new SimpleObject1();
            var garplyT3 = new SimpleObject2();
            var garplyT4 = new Func<SimpleObject1, SimpleObject2, string>(
                (one, two) => one is null || two is null
                    ? "Not Enough Info"
                    : "Had Enough Info");

            var garplyResult1 = foo.Garply(garplyT1, garplyT2, garplyT3, garplyT4);
            ((object)garplyResult1.Item1.Name).Should().Be(garplyT3.Name);
            ((object)garplyResult1.Item2).Should().BeSameAs(garplyT1);
            ((object)garplyResult1.Item3.Name).Should().BeSameAs(garplyT2.Name);
            ((object)garplyResult1.Item4).Should().BeSameAs(garplyT4);
            ((object)garplyResult1.Item4.Invoke((SimpleObject1)garplyResult1.Item3, (SimpleObject2)garplyResult1.Item1)).Should().BeSameAs(garplyT4(garplyT2, garplyT3));

            var garplyResult2 = foo.Garply(null, null, null, garplyT4);
            ((object)garplyResult2.Item1).Should().BeNull();
            ((object)garplyResult2.Item2).Should().BeNull();
            ((object)garplyResult2.Item3).Should().BeNull();
            ((object)garplyResult2.Item4).Should().BeSameAs(garplyT4);
            ((object)garplyResult2.Item4.Invoke((SimpleObject1)garplyResult1.Item3, (SimpleObject2)garplyResult1.Item1)).Should().BeSameAs(garplyT4(garplyT2, garplyT3));

            var garplyResult3 = foo.Garply(garplyT1, garplyT2, garplyT3, null);
            ((object)garplyResult1.Item1.Name).Should().Be(garplyT3.Name);
            ((object)garplyResult1.Item2).Should().BeSameAs(garplyT1);
            ((object)garplyResult1.Item3.Name).Should().BeSameAs(garplyT2.Name);
            ((object)garplyResult3.Item4).Should().BeNull();

            act = () => foo.Garply(null, null, null, null);
            act.Should().Throw<Exception>();
        }

        [Fact(Skip = "Nullable values fail in generic methods fail")]
        public void GenericMethodsWithNullableParametersShouldNotFail()
        {
            var foo = new NonGenericFoo().Unlock();

            string garplyT1 = "5/11/2020";
            DateTime? garplyT2 = new DateTime(2020, 2, 1);
            int? garplyT3 = 100;
            var garplyT4 = new Func<DateTime?, int?, string>(
                (date, offset) => date is null || offset is null
                    ? "Not Enough Info"
                    : date.Value.AddDays(offset.Value).ToShortDateString());

            var garplyResult1 = foo.Garply(garplyT1, garplyT2, garplyT3, garplyT4);
            garplyResult1.Item1.Value.Should().BeSameAs(garplyT3.Value);
            garplyResult1.Item2.Should().BeSameAs(garplyT1);
            garplyResult1.Item3.Value.Should().BeSameAs(garplyT2.Value);
            garplyResult1.Item4.Should().BeSameAs(garplyT4);
            garplyResult1.Item4.Invoke((DateTime?)garplyResult1.Item3, (int?)garplyResult1.Item1).Should().BeSameAs(garplyT4(garplyT2, garplyT3));
        }

        private class BaseClassWithPrivateMembers
        {
            private int _foo = 1;
#pragma warning disable CA1822 // Mark members as static
            private string Bar() => "abc";
#pragma warning restore CA1822 // Mark members as static
            private double Baz { get; set; } = 123.45;

            public int GetBaseFoo() => _foo;
            public string GetBaseBar() => Bar();
            public double GetBaseBaz() => Baz;
        }

        private class DerivedClassFromBaseClassWithPrivateMembers : BaseClassWithPrivateMembers
        {
        }

        private class BaseClassWithConflictingPrivateMembers
        {
            private int _foo = 1;
#pragma warning disable CA1822 // Mark members as static
            private string Bar() => "abc";
#pragma warning restore CA1822 // Mark members as static
            private double Baz { get; set; } = 123.45;

            public int GetBaseFoo() => _foo;
            public string GetBaseBar() => Bar();
            public double GetBaseBaz() => Baz;
        }

        private class DerivedClassWithConflictingPrivateMembers : BaseClassWithConflictingPrivateMembers
        {
            private int _foo = 2;
#pragma warning disable CA1822 // Mark members as static
            private string Bar() => "xyz";
#pragma warning restore CA1822 // Mark members as static
            private double Baz { get; set; } = 987.65;

            public int GetDerivedFoo() => _foo;
            public string GetDerivedBar() => Bar();
            public double GetDerivedBaz() => Baz;
        }

        private class GenericFoo
        {
            public static T Bar<T>(string s, T t)
            {
                return t;
            }

#pragma warning disable CA1822 // Mark members as static
            public T Baz<T>() where T : new()
            {
                return new T();
            }

            public T Qux<T>(string s, T tIn, ref T tOut)
#pragma warning restore CA1822 // Mark members as static
            {
                tOut = tIn;
                return tIn;
            }
        }

#pragma warning disable CA1034 // Nested types should not be visible
#pragma warning disable CA1040 // Avoid empty interfaces
        public interface IPork { }
        public interface IHam : IPork { }
        public interface ICountryHam : IHam { }

        public class Pork : IPork { }
        public class Ham : Pork, IHam { }
        public class CountryHam : Ham, ICountryHam { }

        public class BadActor : IHam { }
        public class Prosciutto : ICountryHam { }
#pragma warning restore CA1040 // Avoid empty interfaces
#pragma warning restore CA1034 // Nested types should not be visible

        private class Spam
        {
#pragma warning disable CA1822 // Mark members as static
            private string? PrivateFoo(Pork pork) { return null; }
            private string? PrivateFoo(IHam ham) { return null; }
            public string? PublicFoo(Pork pork) { return null; }
            public string? PublicFoo(IHam ham) { return null; }

            private string? PrivateBar(ICountryHam countryHam) { return null; }
            private string? PrivateBar(Ham ham) { return null; }
            public string? PublicBar(ICountryHam countryHam) { return null; }
            public string? PublicBar(Ham ham) { return null; }
#pragma warning restore CA1822 // Mark members as static
        }

        private class Lard
        {
            public Lard(bool gross)
            {
            }
        }

#pragma warning disable CA1812 // Avoid uninstantiated internal classes 
        private class Bacon : Lard
#pragma warning restore CA1812 // Avoid uninstantiated internal classes
        {
            public Bacon()
                : base(false)
            {
            }
        }
    }

    // ReSharper disable UnusedParameter.Local
    // ReSharper disable UnusedMember.Local
    // ReSharper disable ConvertToAutoProperty
    // ReSharper disable EventNeverSubscribedTo.Local
    public class Foo
    {
        private int _bar;
        private static int _baz = -1;

        public Foo()
        {
        }

        public Foo(int bar)
        {
            _bar = bar;
        }

        protected int Bar { get { return _bar; } set { _bar = value; } }
        protected static int Baz { get { return _baz; } set { _baz = value; } }

        public static void Reset()
        {
            _baz = -1;
        }

#pragma warning disable CA1822 // Mark members as static
        private string Qux(int i, string s)
        {
            return "Qux(int i, string s)";
        }

        private string Garply()
        {
            return "Garply()";
        }

        private string Garply(int i)
        {
            return "Garply(int i)";
        }

        private string Garply(string s)
        {
            return "Garply(string s)";
        }

        private string Garply(IBaz b)
        {
            return "Garply(IBaz b)";
        }

        private string Garply(int i, string s)
        {
            return "Garply(int i, string s)";
        }

        private static string Grault(int i)
        {
            return "Grault(int i)";
        }

        public void Fred(object o)
        {
        }

        public void Fred(IBaz b)
        {
        }

        public void Fred(Baz b)
        {
        }
#pragma warning restore CA1822 // Mark members as static

    }

    public class Bar
    {
        private event EventHandler? Foo;
        private static event EventHandler? Baz;
#pragma warning disable CS0067 // The event is never used
        public event EventHandler? Qux;
#pragma warning restore CS0067 // The event is never used


        public void InvokeFoo()
        {
            var handler = Foo;
            if (handler is not null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public static void InvokeBaz()
        {
            var handler = Baz;
            if (handler is not null)
            {
                handler(null, EventArgs.Empty);
            }
        }
    }

#pragma warning disable CA1040 // Avoid empty interfaces
    public interface IBaz
#pragma warning restore CA1040 // Avoid empty interfaces
    {
    }

    public class Baz : IBaz
    {
    }

    public class AnotherBaz : IBaz
    {
    }

    public class Qux
    {
        private Qux()
        {
        }
    }

    public class Garply
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly string Value;
#pragma warning restore CA1051 // Do not declare visible instance fields

        private Garply() => Value = "Garply()";

        private Garply(int i) => Value = "Garply(int i)";

        private Garply(string s) => Value = "Garply(string s)";
    }

    public static class Grault
    {
    }

    public class Waldo
    {
#pragma warning disable CA1823 // Avoid unused private fields
        private EventHandler? _foo = FooHandler!;
#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable CS0169 // Field is never used
        private Wobble _wobble;
#pragma warning restore CS0169 // Field is never used
#pragma warning restore IDE0044 // Add readonly modifier
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning restore CA1823 // Avoid unused private fields


        public enum Wobble
        {
            Wubble,
            Wibble
        }

        private static void FooHandler(object sender, EventArgs args)
        {
        }
    }

#pragma warning disable CA1716 // Identifiers should not match keywords
    public class MyBase
#pragma warning restore CA1716 // Identifiers should not match keywords
    {
    }

#pragma warning disable CA1040 // Avoid empty interfaces
    public interface IMyInterface
#pragma warning restore CA1040 // Avoid empty interfaces
    {
    }

    public class MyDerived : MyBase, IMyInterface
    {
    }

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public enum MyEnum
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        First
    }

    public class SpiesOnObjectMethods
    {
        public int EqualsInvocations { get; private set; }
        public int GetHashCodeInvocations { get; private set; }
        public int ToStringInvocations { get; private set; }

        public override bool Equals(object? obj)
        {
            EqualsInvocations++;
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            GetHashCodeInvocations++;
            return base.GetHashCode();
        }

        public override string ToString()
        {
            ToStringInvocations++;
            return base.ToString()!;
        }
    }

    public class NonGenericFoo
    {
#pragma warning disable CA1822 // Mark members as static
        private T Bar1<T>(T t) => t;
        private Lazy<T> Baz1<T>(Lazy<T> lazyOfT) => lazyOfT;
        private Func<Lazy<T>> Qux1<T>(Func<Lazy<T>> funcOfLazyOfT) => funcOfLazyOfT;
        private Tuple<T, U> Bar2<T, U>(T t, U u) => Tuple.Create(t, u);
        private Tuple<Lazy<T>, Lazy<U>> Baz2<T, U>(Lazy<T> lazyOfT, Lazy<U> lazyOfU) => Tuple.Create(lazyOfT, lazyOfU);
        private Tuple<Func<Lazy<T>>, Func<Lazy<U>>> Qux2<T, U>(Func<Lazy<T>> funcOfLazyOfT, Func<Lazy<U>> funcOfLazyOfU) => Tuple.Create(funcOfLazyOfT, funcOfLazyOfU);
        private Tuple<T, U> Bar2Reversed<T, U>(U u, T t) => Tuple.Create(t, u);
        private Tuple<Lazy<T>, Lazy<U>> Baz2Reversed<T, U>(Lazy<U> lazyOfU, Lazy<T> lazyOfT) => Tuple.Create(lazyOfT, lazyOfU);
        private Tuple<Func<Lazy<T>>, Func<Lazy<U>>> Qux2Reversed<T, U>(Func<Lazy<U>> funcOfLazyOfU, Func<Lazy<T>> funcOfLazyOfT) => Tuple.Create(funcOfLazyOfT, funcOfLazyOfU);
        private Tuple<T, Lazy<T>, Func<T>, Lazy<Func<T>>> Grault<T>(T t1, Lazy<T> t2, Func<T> t3, Lazy<Func<T>> t4) => Tuple.Create(t1, t2, t3, t4);
        private Tuple<T, U, V, Func<V, T, U>> Garply<T, U, V>(U t1, V t2, T t3, Func<V, T, U> t4) => Tuple.Create(t3, t1, t2, t4);
#pragma warning restore CA1822 // Mark members as static
    }

    public class SimpleObject1
    {
        public string Name { get; set; } = "One";
    }

    public class SimpleObject2
    {
        public string Name { get; set; } = "Two";
    }

    // ReSharper restore EventNeverSubscribedTo.Local
    // ReSharper restore ConvertToAutoProperty
    // ReSharper restore UnusedMember.Local
    // ReSharper restore UnusedParameter.Local
}
