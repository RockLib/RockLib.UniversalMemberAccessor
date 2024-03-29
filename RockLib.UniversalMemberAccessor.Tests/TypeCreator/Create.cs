using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RockLib.Dynamic.UnitTests.TypeCreator
{
    public static class Create
    {
        private static readonly Random _random = new Random();

        public static Type Class(params MemberDefinition[] memberDefinitions)
        {
            return Class(RandomName(), Array.Empty<Type>(), memberDefinitions);
        }

        public static Type Class(Type[] interfaces, params MemberDefinition[] memberDefinitions)
        {
            return Class(RandomName(), interfaces, memberDefinitions);
        }

        public static Type Class(string name, params MemberDefinition[] memberDefinitions)
        {
            return Class(name, Array.Empty<Type>(), memberDefinitions);
        }

        public static Type Class(string name, Type[] interfaces, params MemberDefinition[] memberDefinitions)
        {
            const TypeAttributes classTypeAttributes =
                TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.AutoClass
                | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout;

            return Type(name, interfaces, memberDefinitions, classTypeAttributes);
        }

        public static Type Struct(params MemberDefinition[] memberDefinitions)
        {
            return Struct(RandomName(), Array.Empty<Type>(), memberDefinitions);
        }

        public static Type Struct(Type[] interfaces, params MemberDefinition[] memberDefinitions)
        {
            return Struct(RandomName(), interfaces, memberDefinitions);
        }

        public static Type Struct(string name, params MemberDefinition[] memberDefinitions)
        {
            return Struct(name, Array.Empty<Type>(), memberDefinitions);
        }

        public static Type Struct(string name, Type[] interfaces, params MemberDefinition[] memberDefinitions)
        {
            const TypeAttributes structTypeAttributes =
                TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.SequentialLayout | TypeAttributes.Sealed
                | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.AutoLayout;

            return Type(name, interfaces, memberDefinitions, structTypeAttributes, typeof(ValueType));
        }

        private static Type Type(
            string name, Type[] interfaces, MemberDefinition[] memberDefinitions,
            TypeAttributes typeAttributes, Type? baseType = null)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (interfaces is null) throw new ArgumentNullException(nameof(interfaces));
            if (memberDefinitions is null) throw new ArgumentNullException(nameof(memberDefinitions));

            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(name), AssemblyBuilderAccess.Run);

            var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

            var typeBuilder = moduleBuilder.DefineType(name, typeAttributes, baseType, interfaces);

            var fields = new Collection<FieldBuilder>();

            foreach (var definition in memberDefinitions.OrderBy(d => d is ConstructorDefinition))
            {
                definition.EmitTo(typeBuilder, fields);
            }

            if (baseType != typeof(ValueType)
                && !memberDefinitions.Any(d => d is ConstructorDefinition))
            {
                Define.Constructor().EmitTo(typeBuilder, fields);
            }

            return typeBuilder.CreateType()!;
        }

        internal static string RandomName()
        {
            const int min = 97;
            const int max = 123;

            return new string(new[]
            {
#pragma warning disable CA5394
                (char)_random.Next(65, 92),
                (char)_random.Next(min, max),
                (char)_random.Next(min, max),
                (char)_random.Next(min, max),
                (char)_random.Next(min, max),
                (char)_random.Next(min, max),
                (char)_random.Next(min, max),
                (char)_random.Next(min, max)
#pragma warning restore CA5394
            });
        }
    }
}