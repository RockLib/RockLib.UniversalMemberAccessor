﻿using Microsoft.CSharp.RuntimeBinder;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace RockLib.Dynamic
{
    /// <summary>
    /// A proxy object that enables access to all members of a type, both public and non-public.
    /// </summary>
    /// <remarks>This is a very dangerous class - use with caution.</remarks>
    public class UniversalMemberAccessor : DynamicObject
    {
        const ParameterAttributes HasDefaultValue =
            ParameterAttributes.HasDefault | ParameterAttributes.Optional;

        private static readonly bool _canSetReadonlyStaticValueType;
        private static readonly bool _canSetReadonlyStaticReferenceType;
        private static readonly bool _canSetReadonlyInstanceValueType;
        private static readonly bool _canSetReadonlyInstanceReferenceType;

        private static readonly Func<InvokeMemberBinder, IList<Type>> _getCSharpTypeArguments;

        private static readonly ConditionalWeakTable<object, UniversalMemberAccessor> _instanceMap = new ConditionalWeakTable<object, UniversalMemberAccessor>();
        private static readonly ConcurrentDictionary<Type, UniversalMemberAccessor> _staticCache = new ConcurrentDictionary<Type, UniversalMemberAccessor>();
        private static readonly ConcurrentDictionary<Type, IEnumerable<string>> _instanceMemberNamesCache = new ConcurrentDictionary<Type, IEnumerable<string>>();
        private static readonly ConcurrentDictionary<Type, IEnumerable<string>> _staticMemberNamesCache = new ConcurrentDictionary<Type, IEnumerable<string>>();

        private static readonly ConcurrentDictionary<Type, bool> _canBeAssignedNullCache = new ConcurrentDictionary<Type, bool>();
        private static readonly ConcurrentDictionary<Tuple<Type, Type, Type>, bool> _canBeAssignedCache = new ConcurrentDictionary<Tuple<Type, Type, Type>, bool>();

        private static readonly ConcurrentDictionary<Tuple<Type, string>, Func<object, object>> _getMemberFuncs = new ConcurrentDictionary<Tuple<Type, string>, Func<object, object>>();
        private static readonly ConcurrentDictionary<Tuple<Type, string, Type>, Action<object, object>> _setMemberActions = new ConcurrentDictionary<Tuple<Type, string, Type>, Action<object, object>>();

        private static readonly ConcurrentDictionary<InvokeMethodDefinition, Func<object, object[], object>> _invokeMethodFuncs = new ConcurrentDictionary<InvokeMethodDefinition, Func<object, object[], object>>();
        private static readonly ConcurrentDictionary<CreateInstanceDefinition, Func<object[], object>> _createInstanceFuncs = new ConcurrentDictionary<CreateInstanceDefinition, Func<object[], object>>();

        private readonly object? _instance;
        private readonly Lazy<UniversalMemberAccessor> _base;
        private readonly Type _type;
        private readonly List<PropertyInfo> _indexers;
        private readonly IEnumerable<string> _memberNames;

#pragma warning disable CA1810 // Initialize reference type static fields inline
        static UniversalMemberAccessor()
#pragma warning restore CA1810 // Initialize reference type static fields inline
        {
            // Need to figure out if the current runtime supports setting readonly fields given two variables: 1) whether
            // the field is static or instance, and 2) whether the field's type is a value type or reference type.
            var staticValueTypeField = typeof(ReadonlyFields).GetField("_staticValueType", BindingFlags.NonPublic | BindingFlags.Static);
            var staticReferenceTypeField = typeof(ReadonlyFields).GetField("_staticReferenceType", BindingFlags.NonPublic | BindingFlags.Static);
            var instanceValueTypeField = typeof(ReadonlyFields).GetField("_instanceValueType", BindingFlags.NonPublic | BindingFlags.Instance);
            var instanceReferenceTypeField = typeof(ReadonlyFields).GetField("_instanceReferenceType", BindingFlags.NonPublic | BindingFlags.Instance);

            var readonlyFields = new ReadonlyFields();

            var initialStaticValueType = ReadonlyFields.StaticValueType;
            var initialStaticReferenceType = ReadonlyFields.StaticReferenceType;
            var initialInstanceValueType = readonlyFields.InstanceValueType;
            var initialInstanceReferenceType = readonlyFields.InstanceReferenceType;

            try
            {
                staticValueTypeField?.SetValue(null, 9);
                _canSetReadonlyStaticValueType = (ReadonlyFields.StaticValueType != initialStaticValueType);
            }
            catch (Exception ex) when (ex is FieldAccessException || ex is TargetException || ex is ArgumentException)
            {
                _canSetReadonlyStaticValueType = false;
            }

            try
            {
                staticReferenceTypeField?.SetValue(null, "Z");
                _canSetReadonlyStaticReferenceType = (ReadonlyFields.StaticReferenceType != initialStaticReferenceType);
            }
            catch (Exception ex) when (ex is FieldAccessException || ex is TargetException || ex is ArgumentException)
            {
                _canSetReadonlyStaticReferenceType = false;
            }

            try
            {
                instanceValueTypeField?.SetValue(readonlyFields, 8);
                _canSetReadonlyInstanceValueType = (readonlyFields.InstanceValueType != initialInstanceValueType);
            }
            catch (Exception ex) when (ex is FieldAccessException || ex is TargetException || ex is ArgumentException)
            {
                _canSetReadonlyInstanceValueType = false;
            }

            try
            {
                instanceReferenceTypeField?.SetValue(readonlyFields, "Y");
                _canSetReadonlyInstanceReferenceType = (readonlyFields.InstanceReferenceType != initialInstanceReferenceType);
            }
            catch (Exception ex) when (ex is FieldAccessException || ex is TargetException || ex is ArgumentException)
            {
                _canSetReadonlyInstanceReferenceType = false;
            }

            // Need to use some reflection in order to get the generic type arguments supplied by the caller.
            // Note that Visual Basic's late binding does not support generic type arguments.
            var cSharpBinderType = Type.GetType("Microsoft.CSharp.RuntimeBinder.ICSharpInvokeOrInvokeMemberBinder, Microsoft.CSharp, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

            if (cSharpBinderType is not null)
            {
                var property = cSharpBinderType.GetProperty("TypeArguments");

                if (property is not null && typeof(IList<Type>).IsAssignableFrom(property.PropertyType))
                {
                    var binderParameter = Expression.Parameter(typeof(InvokeMemberBinder), "binder");

                    var lambda = Expression.Lambda<Func<InvokeMemberBinder, IList<Type>>>(
                        Expression.Property(
                            Expression.Convert(binderParameter, cSharpBinderType),
                            property),
                        binderParameter);

                    var getTypeArguments = lambda.Compile();
                    var empty = Array.Empty<Type>();

                    _getCSharpTypeArguments = binder =>
                        cSharpBinderType.IsInstanceOfType(binder)
                            ? getTypeArguments(binder)
                            : empty;
                }
            }

            if (_getCSharpTypeArguments is null)
            {
                var empty = Array.Empty<Type>();
                _getCSharpTypeArguments = binder => empty;
            }
        }

        private UniversalMemberAccessor(Type type)
        {
            _type = type;
            _indexers = _type.GetProperties().Where(p => p.Name == "Item").ToList();

            if (_type.BaseType is not null)
            {
                _base = new Lazy<UniversalMemberAccessor>(() => new UniversalMemberAccessor(_instance!, _type.BaseType));
            }
            else
            {
                _base = new Lazy<UniversalMemberAccessor>(() => null!);
            }

            _memberNames = _staticMemberNamesCache.GetOrAdd(_type, t =>
                t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => !(m is ConstructorInfo) && !(m is EventInfo) && !(m is Type))
                    .Select(m => m.Name)
                    .ToList()
                    .AsReadOnly());
        }

        private UniversalMemberAccessor(object instance, Type type = null!)
        {
            _instance = instance!;
            _type = type ?? instance!.GetType();
            _indexers = _type.GetProperties().Where(p => p.Name == "Item").ToList();

            if (_type.BaseType is not null)
            {
                _base = new Lazy<UniversalMemberAccessor>(() => new UniversalMemberAccessor(_instance, _type.BaseType));
            }
            else
            {
                _base = new Lazy<UniversalMemberAccessor>(() => null!);
            }

            _memberNames = _instanceMemberNamesCache.GetOrAdd(_type, t =>
                t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => !(m is ConstructorInfo) && !(m is EventInfo) && !(m is Type))
                    .Select(m => m.Name)
                    .ToList()
                    .AsReadOnly());
        }

        /// <summary>
        /// Gets a dynamic proxy object (specifically, an instance of <see cref="UniversalMemberAccessor"/>)
        /// for the given object. Returns null if <paramref name="instance"/> is null.
        /// </summary>
        /// <remarks>
        /// If this method is called more than once with the same object, then the value returned
        /// is the same instance of <see cref="UniversalMemberAccessor"/> each time.
        /// </remarks>
        /// <param name="instance">An object.</param>
        /// <returns>
        /// A dynamic proxy object enabling access to all members of the given instance, or null
        /// if <paramref name="instance"/> is null.
        /// </returns>
        public static dynamic Get(object instance)
        {
            if (instance is null)
            {
                return null!;
            }

            if (instance.GetType().IsValueType)
            {
                return new UniversalMemberAccessor(instance);
            }

            return _instanceMap.GetValue(instance, obj => new UniversalMemberAccessor(obj));
        }

        /// <summary>
        /// Gets a dynamic proxy object (specifically, an instance of <see cref="UniversalMemberAccessor"/>)
        /// for the static members of the given type.
        /// </summary>
        /// <param name="type">The type whose static members will be exposed by the resulting dynamic proxy object.</param>
        /// <returns>A dynamic proxy object enabling access to all static members of the given type.</returns>
        public static dynamic GetStatic(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));

            return _staticCache.GetOrAdd(type, t => new UniversalMemberAccessor(t));
        }

        /// <summary>
        /// Gets a dynamic proxy object (specifically, an instance of <see cref="UniversalMemberAccessor"/>)
        /// for the static members of the type of the <typeparamref name="T"/> type parameter.
        /// </summary>
        /// <typeparam name="T">
        /// The type whose static members will be exposed by the resulting dynamic proxy object.
        /// </typeparam>
        /// <returns>A dynamic proxy object enabling access to all static members of the given type.</returns>
        /// <remarks>This is a very dangerous method - use with caution.</remarks>
        public static dynamic GetStatic<T>()
        {
            return GetStatic(typeof(T));
        }

        /// <summary>
        /// Gets a dynamic proxy object (specifically, an instance of <see cref="UniversalMemberAccessor"/>)
        /// for the static members of the type described by the <paramref name="assemblyQualifiedName"/>
        /// parameter.
        /// </summary>
        /// <param name="assemblyQualifiedName">
        /// The assembly qualified name of a type whose static members will be exposed by the
        /// resulting dynamic proxy object.
        /// </param>
        /// <returns>A dynamic proxy object enabling access to all static members of the given type.</returns>
        /// <remarks>This is a very dangerous method - use with caution.</remarks>
        public static dynamic GetStatic(string assemblyQualifiedName)
        {
            return GetStatic(Type.GetType(assemblyQualifiedName, true)!);
        }

        /// <inheritdoc />
        public override string? ToString()
        {
            return _instance is not null ? _instance.ToString() : _type.ToString();
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (_instance is not null)
            {
                var other = obj as UniversalMemberAccessor;
                if (other is not null)
                {
                    if (other._instance is not null)
                    {
                        return _instance.Equals(other._instance);
                    }
                    return false;
                }
                return _instance.Equals(obj);
            }
            else
            {
                var other = obj as UniversalMemberAccessor;
                if (other is not null)
                {
                    if (other._instance is null)
                    {
                        return _type.Equals((object)other._type);
                    }
                    return false;
                }
                return _type.Equals(obj);
            }
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _instance is not null ? _instance.GetHashCode() : _type.GetHashCode();
        }

        /// <summary>
        /// Provides the implementation for operations that get member values.
        /// </summary>
        /// <param name="binder">Provides information about the object that called the dynamic operation.</param>
        /// <param name="result">The result of the get operation.</param>
        /// <returns>
        /// true if the operation is successful; otherwise, false.
        /// </returns>
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            if (binder is null)
            {
                throw new ArgumentNullException(nameof(binder));
            }

            Func<object, object> getMember;

            if (!TryGetGetMemberFunc(binder.Name, out getMember))
            {
                switch (binder.Name)
                {
                    case "Instance":
                    case "Object":
                    case "Value":
                        if (_instance is not null)
                        {
                            result = _instance;
                            return true;
                        }
                        break;
                    case "Base":
                    case "BaseType":
                    case "BaseClass":
                        if (_base is not null)
                        {
                            result = _base.Value;
                            return true;
                        }
                        break;
                }

                return base.TryGetMember(binder, out result);
            }

            result = getMember(_instance!);
            return true;
        }

        private bool TryGetGetMemberFunc(string name, out Func<object, object> getMember)
        {
            getMember =
                _getMemberFuncs.GetOrAdd(
                    Tuple.Create(_type, name),
                    t => CreateGetMemberFunc(t.Item2));

            return getMember is not null;
        }

        /// <summary>
        /// Provides the implementation for operations that set member values.
        /// </summary>
        /// <param name="binder">Provides information about the object that called the dynamic operation.</param>
        /// <param name="value">The value to set to the member.</param>
        /// <returns>
        /// true if the operation is successful; otherwise, false.
        /// </returns>
        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            if (binder is null)
            {
                throw new ArgumentNullException(nameof(binder));
            }

            Action<object, object> setMember;
            var valueType = GetValueType(value);

            if (!TryGetSetMemberAction(binder.Name, valueType, out setMember))
            {
                return base.TrySetMember(binder, value);
            }

            setMember(_instance!, value!);
            return true;
        }

        private static Type? GetValueType(object? value)
        {
            return
                value is null
                    ? null
                    : value is UniversalMemberAccessor
                        ? ((UniversalMemberAccessor)value)._instance!.GetType()
                        : value.GetType();
        }

        private bool TryGetSetMemberAction(string name, Type? valueType, out Action<object, object> setMember)
        {
            setMember = _setMemberActions.GetOrAdd(
                Tuple.Create(_type, name, valueType)!,
                t => CreateSetMemberAction(t.Item2, t.Item3));

            return setMember is not null;
        }

        /// <summary>
        /// Provides the implementation for operations that invoke a member. Classes derived from the <see cref="System.Dynamic.DynamicObject"/> class can override this method to specify dynamic behavior for operations such as calling a method.
        /// </summary>
        /// <param name="binder">Provides information about the dynamic operation.</param>
        /// <param name="args">The arguments that are passed to the object member during the invoke operation.</param>
        /// <param name="result">The result of the member invocation.</param>
        /// <returns>
        /// true if the operation is successful; otherwise, false. If this method returns false, the run-time binder of the language determines the behavior. (In most cases, a language-specific run-time exception is thrown.)
        /// </returns>
        public override bool TryInvokeMember(
            InvokeMemberBinder binder,
            object?[]? args,
            out object? result)
        {
            if (binder is null)
            {
                throw new ArgumentNullException(nameof(binder));
            }

            var typeArguments = _getCSharpTypeArguments(binder);

            var invokeMethodFunc = _invokeMethodFuncs.GetOrAdd(
                new InvokeMethodDefinition(_type, binder.Name, typeArguments, args!),
                CreateInvokeMethodFunc);

            result = invokeMethodFunc(_instance!, args!);
            return true;
        }

        internal Func<object[], object> GetCreateInstanceFunc(object[] args)
        {
            return _createInstanceFuncs.GetOrAdd(
                new CreateInstanceDefinition(_type, args),
                CreateCreateInstanceFunc);
        }

        private class TaskTypeComparer : IComparer<Type>
        {
            public int Compare(Type? x, Type? y)
            {
                if (x!.IsAssignableFrom(y))
                    return 1;
                if (y!.IsAssignableFrom(x))
                    return -1;
                return 0;
            }
        }

        private static IList<Candidate> GetBetterMethods(Type[] argTypes, IList<Candidate> legalCandidates)
        {
            if (legalCandidates.All(c => typeof(Task).IsAssignableFrom(c.Method.DeclaringType) && c.Method.Name == "GetAwaiter" && c.Method.GetParameters().Length == 0))
            {
                // return the candidate whose declaringtype is the most-derived
                return legalCandidates.OrderBy(x => x.Method.DeclaringType, new TaskTypeComparer()!).Take(1).ToList();
            }

            var isBestCandidate = Enumerable.Repeat(true, legalCandidates.Count).ToList();

            for (int i = 0; i < legalCandidates.Count; i++)
            {
                var candidate = legalCandidates[i];

                for (int j = 0; j < legalCandidates.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var score = candidate.GetBetterScore(legalCandidates[j], argTypes);

                    if (score < 1)
                    {
                        isBestCandidate[i] = false;
                    }
                }
            }

            var betterMethods =
                isBestCandidate
                    .Select((isBest, index) => new { isBest, index })
                    .Where(x => x.isBest)
                    .Select(x => legalCandidates[x.index])
                    .ToList();

            return betterMethods;
        }

        /// <summary>
        /// Provides the implementation for operations that get a value by index.
        /// </summary>
        /// <param name="binder">Provides information about the operation.</param>
        /// <param name="indexes">The indexes that are used in the operation.</param>
        /// <param name="result">The result of the index operation.</param>
        /// <returns>
        /// true if the operation is successful; otherwise, false.
        /// </returns>
        public override bool TryGetIndex(
            GetIndexBinder binder,
            object[] indexes,
            out object? result)
        {
            if (indexes is null)
            {
                throw new ArgumentNullException(nameof(indexes));
            }

            if (indexes.Length == 1
                && indexes[0] is string)
            {
                Func<object, object> getMember;

                if (TryGetGetMemberFunc((string)indexes[0], out getMember))
                {
                    result = getMember(_instance!);
                    return true;
                }
            }

            if (_indexers is not null)
            {
                if (_indexers.Count == 1)
                {
                    result = _indexers[0].GetValue(_instance, indexes);
                    return true;
                }

                foreach (var indexer in _indexers)
                {
                    var getMethod = indexer.GetGetMethod();
                    var parameters = getMethod!.GetParameters();
                    if (parameters.Length == indexes.Length)
                    {
                        var foundMatch = true;
                        for (int i = 0; i < indexes.Length; i++)
                        {
                            if (indexes[i].GetType() != parameters[i].ParameterType)
                            {
                                foundMatch = false;
                                break;
                            }
                        }

                        if (foundMatch)
                        {
                            result = indexer.GetValue(_instance, indexes);
                            return true;
                        }
                    }
                }
            }

            if (_type.IsArray)
            {
                result = ((Array)_instance!).GetValue(indexes.Cast<int>().ToArray());
                return true;
            }

            return base.TryGetIndex(binder, indexes, out result);
        }

        /// <summary>
        /// Provides the implementation for operations that set a value by index.
        /// </summary>
        /// <param name="binder">Provides information about the operation.</param>
        /// <param name="indexes">The indexes that are used in the operation.</param>
        /// <param name="value">The value to set to the object that has the specified index.</param>
        /// <returns>
        /// true if the operation is successful; otherwise, false.
        /// </returns>
        public override bool TrySetIndex(
            SetIndexBinder binder,
            object[] indexes,
            object? value)
        {
            if (indexes is null)
            {
                throw new ArgumentNullException(nameof(indexes));
            }

            if (indexes.Length == 1
                && indexes[0] is string)
            {
                Action<object, object> setMember;
                var valueType = GetValueType(value);

                if (TryGetSetMemberAction((string)indexes[0], valueType, out setMember))
                {
                    setMember(_instance!, value!);
                    return true;
                }
            }

            if (_indexers is not null)
            {
                if (_indexers.Count == 1)
                {
                    _indexers[0].SetValue(_instance, value, indexes);
                    return true;
                }

                foreach (var indexer in _indexers)
                {
                    var setMethod = indexer.GetSetMethod();
                    var parameters = setMethod!.GetParameters();
                    if (parameters.Length == indexes.Length + 1)
                    {
                        var foundMatch = true;
                        for (int i = 0; i < indexes.Length; i++)
                        {
                            if (indexes[i].GetType() != parameters[i].ParameterType)
                            {
                                foundMatch = false;
                                break;
                            }
                        }

                        if (foundMatch)
                        {
                            indexer.SetValue(_instance, value, indexes);
                            return true;
                        }
                    }
                }
            }

            if (_type.IsArray)
            {
                ((Array)_instance!).SetValue(value, indexes.Cast<int>().ToArray());
                return true;
            }

            return base.TrySetIndex(binder, indexes, value);
        }

        /// <summary>
        /// Provides implementation for type conversion operations.
        /// </summary>
        /// <param name="binder">Provides information about the conversion operation.</param>
        /// <param name="result">The result of the type conversion operation.</param>
        /// <returns>
        /// true if the operation is successful; otherwise, false.
        /// </returns>
        public override bool TryConvert(
            ConvertBinder binder,
            out object result)
        {
            if (binder is null)
            {
                throw new ArgumentNullException(nameof(binder));
            }

            if (!binder.ReturnType.IsAssignableFrom(_type))
            {
                throw new RuntimeBinderException($"Cannot implicitly convert type '{_type}' to '{binder.ReturnType}'");
            }

            result = _instance!;
            return true;
        }

        /// <summary>
        /// Returns the enumeration of all dynamic member names. 
        /// </summary>
        /// <returns>
        /// A sequence that contains dynamic member names.
        /// </returns>
        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _memberNames;
        }

        private Func<object, object> CreateGetMemberFunc(string name)
        {
            var nestedType = _type.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (nestedType is not null)
            {
                var staticAccessor = GetStatic(nestedType);
                return obj => staticAccessor;
            }

            var parameter = Expression.Parameter(typeof(object), "instance");
            var convertParameter = Expression.Convert(parameter, _type);

            var propertyOrField = GetPropertyOrFieldGetterExpression(_type, name, convertParameter);

            if (propertyOrField is null)
            {
                return null!;
            }

            var type = propertyOrField.Type;

            if (type.IsValueType)
            {
                propertyOrField = Expression.Convert(propertyOrField, typeof(object));
            }

            var lambda =
                Expression.Lambda<Func<object, object>>(
                    propertyOrField,
                    new[] { parameter });

            var func = lambda.Compile();

            if (ShouldReturnRawValue(type))
            {
                return obj => func(UnwrapInstance(obj));
            }

            return obj => Get(func(UnwrapInstance(obj)));
        }

        private Expression GetPropertyOrFieldGetterExpression(Type type, string name, Expression convertParameter)
        {
            var propertyInfo = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propertyInfo is not null)
            {
                return Expression.Property(IsStatic(propertyInfo) ? null : convertParameter, propertyInfo);
            }
            else
            {
                var fieldInfo = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (fieldInfo is not null)
                {
                    return Expression.Field(fieldInfo.IsStatic ? null : convertParameter, fieldInfo);
                }
                else
                {
                    if (type.BaseType != typeof(object))
                    {
                        return GetPropertyOrFieldGetterExpression(type.BaseType!, name, convertParameter);
                    }
                    return null!;
                }
            }
        }

        private static void GetPropertyOrField(Type type, string name, out PropertyInfo property, out FieldInfo field)
        {
            var propertyInfo = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propertyInfo is not null)
            {
                property = propertyInfo;
                field = null!;
            }
            else
            {
                var fieldInfo = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (fieldInfo is not null)
                {
                    property = null!;
                    field = fieldInfo;
                }
                else
                {
                    if (type.BaseType != typeof(object))
                    {
                        GetPropertyOrField(type.BaseType!, name, out property, out field);
                    }
                    else
                    {
                        property = null!;
                        field = null!;
                    }
                }
            }
        }

        private Action<object, object> CreateSetMemberAction(string name, Type valueType)
        {
            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var valueParameter = Expression.Parameter(typeof(object), "value");

            var convertInstanceParameter = Expression.Convert(instanceParameter, _type);

            Action<object, object> action = null!;

            Expression propertyOrField;
            PropertyInfo propertyInfo;
            FieldInfo fieldInfo;

            GetPropertyOrField(_type, name, out propertyInfo, out fieldInfo);

            if (propertyInfo is not null)
            {
                if (valueType is not null)
                {
                    if (!CanBeAssigned(propertyInfo.PropertyType, valueType, Type.EmptyTypes))
                    {
                        var message = $"Cannot implicitly convert type '{valueType}' to '{propertyInfo.PropertyType}'";
                        return (instance, value) => { throw new RuntimeBinderException(message); };
                    }
                }
                else if (!CanBeAssignedNull(propertyInfo.PropertyType))
                {
                    var message = $"Cannot convert null to '{propertyInfo.PropertyType}' because it is a non-nullable value type";
                    return (instance, value) => { throw new RuntimeBinderException(message); };
                }

                propertyOrField = Expression.Property(IsStatic(propertyInfo) ? null : convertInstanceParameter, propertyInfo);
            }
            else
            {
                if (fieldInfo is not null)
                {
                    if (valueType is not null)
                    {
                        if (!CanBeAssigned(fieldInfo.FieldType, valueType, Type.EmptyTypes))
                        {
                            var message = $"Cannot implicitly convert type '{valueType}' to '{fieldInfo.FieldType}'";
                            return (instance, value) => { throw new RuntimeBinderException(message); };
                        }
                    }
                    else if (!CanBeAssignedNull(fieldInfo.FieldType))
                    {
                        var message = $"Cannot convert null to '{fieldInfo.FieldType}' because it is a non-nullable value type";
                        return (instance, value) => { throw new RuntimeBinderException(message); };
                    }

                    if (fieldInfo.IsInitOnly)
                    {
                        if ((fieldInfo.IsStatic && fieldInfo.FieldType.IsValueType && !_canSetReadonlyStaticValueType)
                            || (fieldInfo.IsStatic && !fieldInfo.FieldType.IsValueType && !_canSetReadonlyStaticReferenceType)
                            || (!fieldInfo.IsStatic && fieldInfo.FieldType.IsValueType && !_canSetReadonlyInstanceValueType)
                            || (!fieldInfo.IsStatic && !fieldInfo.FieldType.IsValueType && !_canSetReadonlyInstanceReferenceType))
                        {
                            var staticOrInstance = fieldInfo.IsStatic ? "static" : "instance";
                            var valueOrReference = fieldInfo.FieldType.IsValueType ? "value" : "reference";
                            var message = $"The current runtime does not allow the (illegal) changing of readonly {staticOrInstance} {valueOrReference}-type fields.";

                            return (instance, value) => { throw new NotSupportedException(message); };
                        }

                        var dynamicMethod = new DynamicMethod("", typeof(void),
                            new[] { typeof(object), typeof(object) }, fieldInfo.DeclaringType!);

                        var il = dynamicMethod.GetILGenerator();

                        if (!fieldInfo.IsStatic)
                        {
                            // Load the first arg (should not do for static field)
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Castclass, fieldInfo.DeclaringType!);
                        }

                        // Load the second arg
                        il.Emit(OpCodes.Ldarg_1);

                        if (fieldInfo.FieldType.IsValueType)
                        {
                            // Unbox value types.
                            if (valueType == fieldInfo.FieldType || valueType is null)
                            {
                                // Unbox as the field type when value type matches or is null.
                                il.Emit(OpCodes.Unbox_Any, fieldInfo.FieldType);
                            }
                            else
                            {
                                // If value type is different than field type, unbox as the
                                // value type. Otherwise we get an invalid cast exception.
                                il.Emit(OpCodes.Unbox_Any, valueType);
                            }
                        }
                        else
                        {
                            // Cast reference types.
                            il.Emit(OpCodes.Castclass, fieldInfo.FieldType);
                        }

                        // Different op code for static vs. instance.
                        il.Emit(fieldInfo.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, fieldInfo);
                        il.Emit(OpCodes.Ret);

                        action = (Action<object, object>)dynamicMethod.CreateDelegate(typeof(Action<object, object>));
                        propertyOrField = null!;
                    }
                    else
                    {
                        propertyOrField = Expression.Field(fieldInfo.IsStatic ? null : convertInstanceParameter, fieldInfo);
                    }
                }
                else
                {
                    return null!;
                }
            }

            if (action is null)
            {
                Expression newValue;

                if (propertyOrField.Type.IsValueType)
                {
                    if (valueType == propertyOrField.Type || valueType is null)
                    {
                        newValue = Expression.Unbox(valueParameter, propertyOrField.Type);
                    }
                    else
                    {
                        newValue = Expression.Convert(
                            Expression.Unbox(valueParameter, valueType),
                            propertyOrField.Type);
                    }
                }
                else
                {
                    newValue = Expression.Convert(valueParameter, propertyOrField.Type);
                }

                var lambda =
                    Expression.Lambda<Action<object, object>>(
                        Expression.Assign(propertyOrField, newValue),
                        new[] { instanceParameter, valueParameter });

                action = lambda.Compile();
            }

            return (obj, value) => action(UnwrapInstance(obj), UnwrapInstance(value));
        }

        private static Func<object[], object> CreateCreateInstanceFunc(CreateInstanceDefinition definition)
        {
            if (definition.Type.IsValueType && definition.ArgTypes.Length == 0)
            {
                var type = definition.Type;

                if (ShouldReturnRawValue(definition.Type))
                {
                    return args => FormatterServices.GetUninitializedObject(type);
                }

                return args => Get(FormatterServices.GetUninitializedObject(type));
            }

            var constructors =
                definition.Type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var candidates = constructors.Select(c => new Candidate(c))
                .Where(c => c.HasRequiredNumberOfParameters(definition.ArgTypes)).ToList();

            if (candidates.Count == 0)
            {
                var message = $"'{definition.Type}' does not contain a constructor that takes {definition.ArgTypes.Length} arguments";

                return args => { throw new RuntimeBinderException(message); };
            }

            var legalCandidates = candidates.Where(c => c.IsLegal(definition.ArgTypes, Type.EmptyTypes)).ToList();

            if (legalCandidates.Count == 0)
            {
                var method = candidates[0].Method.ToString();

#if NET48
                var message = $"The best overloaded constructor match for '{definition.Type}.{method.Substring(method.IndexOf(".ctor", StringComparison.Ordinal)).Replace(".ctor", definition.Type.Name)}' has some invalid arguments";
#else
                var message = $"The best overloaded constructor match for '{definition.Type}.{method?[method.IndexOf(".ctor", StringComparison.Ordinal)..].Replace(".ctor", definition.Type.Name!, StringComparison.Ordinal)}' has some invalid arguments";
#endif

                return args => { throw new RuntimeBinderException(message); };
            }

            Candidate candidate;

            if (legalCandidates.Count == 1)
            {
                candidate = legalCandidates[0];
            }
            else
            {
                var betterMethods = GetBetterMethods(definition.ArgTypes, legalCandidates);

                if (betterMethods.Count == 1)
                {
                    candidate = betterMethods[0];
                }
                else
                {
                    var method0 = candidates[0].Method.ToString();
                    var method1 = candidates[1].Method.ToString();

#if NET48
                    var message = $"The call is ambiguous between the following methods or properties: '{definition.Type}.{method0.Substring(method0.IndexOf(".ctor", StringComparison.Ordinal)).Replace(".ctor", definition.Type.Name)}' and '{definition.Type}.{method1.Substring(method1.IndexOf(".ctor", StringComparison.Ordinal)).Replace(".ctor", definition.Type.Name)}'";
#else
                    var message = $"The call is ambiguous between the following methods or properties: '{definition.Type}.{method0?[method0.IndexOf(".ctor", StringComparison.Ordinal)..].Replace(".ctor", definition.Type.Name!, StringComparison.Ordinal)}' and '{definition.Type}.{method1?[method1.IndexOf(".ctor", StringComparison.Ordinal)..].Replace(".ctor", definition.Type.Name!, StringComparison.Ordinal)}'";
#endif

                    return args => { throw new RuntimeBinderException(message); };
                }
            }

            var constructor = (ConstructorInfo)candidate.Method;

            var argsParameter = Expression.Parameter(typeof(object[]), "args");

            var constructorParameters = constructor.GetParameters();
            var newArguments = GetArguments(constructorParameters, argsParameter, definition.ArgTypes, Type.EmptyTypes);

            Expression body = Expression.New(constructor, newArguments);

            if (constructor.DeclaringType!.IsValueType)
            {
                body = Expression.Convert(body, typeof(object));
            }

            var lambda = Expression.Lambda<Func<object[], object>>(body, new[] { argsParameter });

            var func = lambda.Compile();

            if (ShouldReturnRawValue(constructor.DeclaringType))
            {
                return args => func(UnwrapArgs(args));
            }

            return args => Get(func(UnwrapArgs(args)));
        }

        private static IEnumerable<Tuple<Type, Type>> GetGenericParameters(Type type, Type closedGenericType)
        {
            if (type.IsGenericParameter)
                yield return Tuple.Create(type, closedGenericType);

            if (type.IsGenericType && closedGenericType?.IsGenericType != false)
            {
                var genericArgs = type.GetGenericArguments();
                var closedGenericArgs = closedGenericType?.GetGenericArguments() ?? new Type[genericArgs.Length];

                if (genericArgs.Length == closedGenericArgs.Length)
                    for (int i = 0; i < genericArgs.Length; i++)
                        foreach (var t in GetGenericParameters(genericArgs[i], closedGenericArgs[i]))
                            yield return t;
            }
        }

        private static Func<object, object[], object> CreateInvokeMethodFunc(
            InvokeMethodDefinition definition)
        {
            var legalCandidates = GetLegalCandidates(definition.Type, definition, out var getErrorMessage);

            if (legalCandidates.Count == 0)
            {
                switch (definition.Name)
                {
                    case "New":
                    case "Create":
                    case "CreateInstance":
                    case "NewInstance":
                        var createInstanceFunc = _createInstanceFuncs.GetOrAdd(
                            new CreateInstanceDefinition(definition.Type, definition.ArgTypes!),
                            CreateCreateInstanceFunc);

                        return (instance, args) => createInstanceFunc(args);
                }

                var errorMessage = getErrorMessage();
                return (instance, args) => { throw new RuntimeBinderException(errorMessage); };
            }

            Candidate candidate;

            if (legalCandidates.Count == 1)
            {
                candidate = legalCandidates[0];
            }
            else
            {
                var betterMethods = GetBetterMethods(definition.ArgTypes!, legalCandidates);

                if (betterMethods.Count == 1)
                {
                    candidate = betterMethods[0];
                }
                else
                {
                    var method0 = legalCandidates[0].Method.ToString();
                    var method1 = legalCandidates[1].Method.ToString();

#if NET48
                    var message = $"The call is ambiguous between the following methods or properties: '{method0.Substring(method0.IndexOf(' ') + 1)}' and '{method1.Substring(method1.IndexOf(' ') + 1)}'";
#else
                    var message = $"The call is ambiguous between the following methods or properties: '{method0?[(method0.IndexOf(' ', StringComparison.Ordinal) + 1)..]}' and '{method1?[(method1.IndexOf(' ', StringComparison.Ordinal) + 1)..]}'";
#endif

                    return (instance, args) => { throw new RuntimeBinderException(message); };
                }
            }

            var methodInfo = (MethodInfo)candidate.Method;

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var argsParameter = Expression.Parameter(typeof(object[]), "args");

            var methodInfoParameters = methodInfo.GetParameters();
            var callArguments = GetArguments(methodInfoParameters, argsParameter, definition.ArgTypes!, definition.TypeArguments);

            var localVariables = new List<ParameterExpression>();
            var assignToVariables = new List<Expression>();
            var assignToArgs = new List<Expression>();

            for (int i = 0; i < methodInfoParameters.Length; i++)
            {
                if (methodInfoParameters[i].ParameterType.IsByRef)
                {
                    // need a local variable
                    var localVariable = Expression.Variable(callArguments[i].Type, "local" + i);

                    var convertItem = callArguments[i];
                    var item = Expression.ArrayAccess(argsParameter, Expression.Constant(i));

                    localVariables.Add(localVariable);
                    callArguments[i] = localVariable;

                    if (!methodInfoParameters[i].IsOut)
                    {
                        // need to assign to local variable
                        assignToVariables.Add(Expression.Assign(localVariable, convertItem));
                    }

                    assignToArgs.Add(Expression.Assign(item, Expression.Convert(localVariable, typeof(object))));
                }
            }

            if (methodInfo.IsGenericMethodDefinition)
            {
                Type[] typeArguments;

                if (definition.TypeArguments.Count > 0)
                {
                    typeArguments = definition.TypeArguments.ToArray();
                }
                else
                {
                    var genericArguments = methodInfo.GetGenericArguments();
                    typeArguments = new Type[genericArguments.Length];

                    var parameters = methodInfo.GetParameters();

                    for (int i = 0; i < typeArguments.Length; i++)
                    {
                        var typeArgumentName = genericArguments[i].Name;

                        for (int j = 0; j < parameters.Length; j++)
                        {
                            var parameterType = parameters[j].ParameterType;
                            var definitionType = definition.ArgTypes[j];

                            // ref and out parameters need to be unwrapped.
                            if (parameterType.IsByRef)
                            {
                                parameterType = parameterType.GetElementType();
                            }

                            var genericParameter = GetGenericParameters(parameterType!, definitionType!)
                                .Where(t => t.Item1.Name == typeArgumentName)
                                .Select(t => t.Item2)
                                .FirstOrDefault();

                            if (genericParameter is not null)
                            {
                                typeArguments[i] = genericParameter;
                                break;
                            }
                        }
                    }
                }

                methodInfo = methodInfo.MakeGenericMethod(typeArguments);
            }

            Expression call;

            if (methodInfo.IsStatic)
            {
                call = Expression.Call(
                    methodInfo,
                    callArguments);
            }
            else
            {
                call = Expression.Call(
                    Expression.Convert(instanceParameter, definition.Type),
                    methodInfo,
                    callArguments);
            }

            Expression body;

            if (localVariables.Count == 0)
            {
                body = call;

                if (methodInfo.ReturnType == typeof(void))
                {
                    body = Expression.Block(body, Expression.Constant(null));
                }
                else if (methodInfo.ReturnType.IsValueType)
                {
                    body = Expression.Convert(body, typeof(object));
                }
            }
            else
            {
                var blockExpressions = assignToVariables.ToList();

                ParameterExpression returnValue = null!;

                if (methodInfo.ReturnType != typeof(void))
                {
                    returnValue = Expression.Variable(methodInfo.ReturnType, "returnValue");
                    localVariables.Add(returnValue);
                    call = Expression.Assign(returnValue, call);
                }

                blockExpressions.Add(call);
                blockExpressions.AddRange(assignToArgs);

                if (returnValue is not null)
                {
                    blockExpressions.Add(
                        methodInfo.ReturnType.IsValueType
                            ? (Expression)Expression.Convert(returnValue, typeof(object))
                            : returnValue);
                }

                body = Expression.Block(localVariables, blockExpressions);
            }

            var lambda =
                Expression.Lambda<Func<object, object[], object>>(
                    body,
                    new[] { instanceParameter, argsParameter });

            var func = lambda.Compile();

            if (methodInfo.ReturnType == typeof(void) || ShouldReturnRawValue(methodInfo.ReturnType))
            {
                return (instance, args) => func(UnwrapInstance(instance), UnwrapArgs(args));
            }

            return (instance, args) => Get(func(UnwrapInstance(instance), UnwrapArgs(args)));
        }

        private static List<Candidate> GetLegalCandidates(Type type, InvokeMethodDefinition definition, out Func<string> getErrorMessage)
        {
            var methodInfos =
                type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == definition.Name);

            var candidates = methodInfos.Select(c => new Candidate(c))
                .Where(c => c.HasRequiredNumberOfParameters(definition.ArgTypes!)).ToList();

            var legalCandidates = candidates.Where(c => c.IsLegal(definition.ArgTypes!, definition.TypeArguments)).ToList();

            if (legalCandidates.Count > 0)
            {
                getErrorMessage = () => null!;
                return legalCandidates;
            }

            if (type.BaseType == typeof(object))
            {
                if (candidates.Count == 0)
                {
                    getErrorMessage = () => $"No overload for method '{definition.Name}' takes {definition.ArgTypes.Length} arguments";
                }
                else
                {
                    var method = candidates[0].Method.ToString();

#if NET48
                    getErrorMessage = () => $"The best overloaded method match for '{definition.Type}.{method?.Substring(method.IndexOf(' ') + 1)}' has some invalid arguments";
#else
                    getErrorMessage = () => $"The best overloaded method match for '{definition.Type}.{method?.Substring(method.IndexOf(' ', StringComparison.Ordinal) + 1)}' has some invalid arguments";
#endif
                }

                return legalCandidates;
            }

            return GetLegalCandidates(type.BaseType!, definition, out getErrorMessage);
        }

        private static Expression[] GetArguments(ParameterInfo[] parameters,
            ParameterExpression argsParameter, Type[] argTypes, IList<Type> typeArguments)
        {
            var arguments = new Expression[parameters.Length];

            var skippedIndexes = new List<int>();

            for (var i = 0; i < arguments.Length; i++)
            {
                if (i < argTypes.Length)
                {
                    var parameterType = parameters[i].ParameterType;

                    if (parameterType.IsByRef)
                    {
                        parameterType = parameterType.GetElementType();
                    }

                    if (GetGenericParameters(parameterType!, null!).Any())
                    {
                        if (typeArguments.Count > 0)
                        {
                            parameterType = CloseGenericType(parameterType!, typeArguments);
                        }
                        else
                        {
                            if (argTypes[i] is null)
                            {
                                skippedIndexes.Add(i);
                                continue;
                            }

                            parameterType = argTypes[i];
                        }
                    }

                    SetArgumentsItem(argsParameter, argTypes, i, parameterType!, arguments);
                }
                else
                {
                    arguments[i] = Expression.Constant(parameters[i].DefaultValue, parameters[i].ParameterType);
                }
            }

            if (skippedIndexes.Count > 0)
            {
                // Collect all known generic types from the parameters
                var genericParameterTypes = new Dictionary<string, Type>();
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (argTypes[i] is null)
                        continue;

                    var genericParameters = GetGenericParameters(parameters[i].ParameterType, argTypes[i]);
                    foreach (var genericParameter in genericParameters)
                        if (!genericParameterTypes.ContainsKey(genericParameter.Item1.Name))
                            genericParameterTypes.Add(genericParameter.Item1.Name, genericParameter.Item2);
                }

                foreach (var i in skippedIndexes)
                {
                    var parameterType = parameters[i].ParameterType;

                    if (parameterType.IsByRef)
                        parameterType = parameterType.GetElementType();

                    if (parameterType!.IsGenericParameter || parameterType.IsGenericType)
                        argTypes[i] = CloseGenericType(parameterType, genericParameterTypes);

                    if (argTypes[i] is null)
                        throw new ArgumentNullException($"We don't know the closed generic type for '{parameterType.Name}'");

                    SetArgumentsItem(argsParameter, argTypes, i, argTypes[i], arguments);
                }
            }

            return arguments;
        }

        private static Type CloseGenericType(Type parameterType, Dictionary<string, Type> genericParameterTypes)
        {
            if (parameterType.IsGenericParameter)
                if (genericParameterTypes.ContainsKey(parameterType.Name))
                    return genericParameterTypes[parameterType.Name];
                else
                    throw new ArgumentNullException($"We don't know the type for a generic argument '{parameterType.Name}'");

            var genericArguments = parameterType.GetGenericArguments();
            var genericTypeArguments = new Type[genericArguments.Length];

            for (int i = 0; i < genericArguments.Length; i++)
                genericTypeArguments[i] = CloseGenericType(genericArguments[i], genericParameterTypes);

            return parameterType.GetGenericTypeDefinition().MakeGenericType(genericTypeArguments);
        }

        private static Type CloseGenericType(Type parameterType, IList<Type> methodTypeArguments)
        {
            if (parameterType.IsGenericParameter)
                return methodTypeArguments[parameterType.GenericParameterPosition];

            var genericArguments = parameterType.GetGenericArguments();
            var genericTypeArguments = new Type[genericArguments.Length];

            for (int i = 0; i < genericArguments.Length; i++)
                genericTypeArguments[i] = CloseGenericType(genericArguments[i], methodTypeArguments);

            return parameterType.GetGenericTypeDefinition().MakeGenericType(genericTypeArguments);
        }

        private static void SetArgumentsItem(
            ParameterExpression argsParameter, Type[] argTypes, int i, Type parameterType,
            Expression[] arguments)
        {
            var item = Expression.ArrayAccess(argsParameter, Expression.Constant(i));

            if (parameterType.IsValueType)
            {
                if (argTypes[i] is null)
                {
                    Debug.Assert(parameterType.IsGenericType
                        && parameterType.GetGenericTypeDefinition() == typeof(Nullable<>));

                    arguments[i] = Expression.Constant(null, parameterType);
                }
                else
                {
                    Debug.Assert(argTypes[i].IsValueType);

                    arguments[i] = Expression.Unbox(item, argTypes[i]);

                    if (argTypes[i] != parameterType)
                    {
                        arguments[i] = Expression.Convert(arguments[i], parameterType);
                    }
                }
            }
            else
            {
                if (GetGenericParameters(parameterType, argTypes[i]).Any())
                    arguments[i] = Expression.Convert(item, argTypes[i]);
                else
                    arguments[i] = Expression.Convert(item, parameterType);
            }
        }

        private static bool ShouldReturnRawValue(Type type)
        {
            return
                type == typeof(string)
                || typeof(Delegate).IsAssignableFrom(type)
                || IsValue(type)
                || (type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(Nullable<>)
                    && IsValue(type.GetGenericArguments()[0]));
        }

        private static bool IsValue(Type type)
        {
            return
                type.IsPrimitive
                || type.IsEnum
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || type == typeof(DateTimeOffset);
        }

        // Potentially modifies the items of the array and returns it.
        private static object[] UnwrapArgs(object[] args)
        {
            // Unwrap any UniversalMemberAccessor objects before sending to func.
            for (int i = 0; i < args.Length; i++)
            {
                var universalMemberAccessor = args[i] as UniversalMemberAccessor;
                if (universalMemberAccessor is not null)
                {
                    args[i] = universalMemberAccessor._instance!;
                }
            }

            return args;
        }

        private static object UnwrapInstance(object instance)
        {
            var universalMemberAccessor = instance as UniversalMemberAccessor;

            if (universalMemberAccessor is not null)
            {
                instance = universalMemberAccessor._instance!;
            }

            return instance;
        }

        private static bool IsStatic(PropertyInfo propertyInfo)
        {
            var getMethod = propertyInfo.GetGetMethod(true);
            if (getMethod is not null && getMethod.IsStatic)
            {
                return true;
            }

            var setMethod = propertyInfo.GetSetMethod(true);
            return setMethod is not null && setMethod.IsStatic;
        }

        private class Candidate
        {
            private readonly MethodBase _method;
            private readonly Type[] _parameters;
            private readonly Type[] _defaultParameterTypes;
            private readonly int _defaultParameterCount;
            private readonly Type[] _genericArguments;

            public Candidate(MethodBase method)
            {
                _method = method;

                var parameters = Method.GetParameters();

                _parameters = parameters.Select(p => p.ParameterType).ToArray();

                _defaultParameterTypes = new Type[_parameters.Length];

                for (int i = 0; i < _defaultParameterTypes.Length; i++)
                {
                    if ((parameters[i].Attributes & HasDefaultValue) == HasDefaultValue)
                    {
                        _defaultParameterTypes[i] =
                            parameters[i].DefaultValue?.GetType()!;
                        _defaultParameterCount++;
                    }
                }

                _genericArguments = method.IsGenericMethod
                    ? method.GetGenericArguments()
                    : Type.EmptyTypes;
            }

            public MethodBase Method
            {
                get { return _method; }
            }

            public bool HasRequiredNumberOfParameters(Type[] argTypes)
            {
                return argTypes.Length >= (_parameters.Length - _defaultParameterCount)
                       && argTypes.Length <= _parameters.Length;
            }

            public int GetBetterScore(Candidate other, Type[] argTypes)
            {
                int score = 0;

                for (int i = 0; i < argTypes.Length; i++)
                {
                    if (argTypes[i] is null
                        || _parameters[i] == other._parameters[i])
                    {
                        continue;
                    }

                    AccumulateScore(_parameters[i], other._parameters[i], argTypes[i], ref score);
                }

                return score;
            }

            private static void AccumulateScore(Type thisParameter, Type otherParameter, Type argType, ref int score)
            {
                // sbyte to short, int, long, float, double, or decimal
                // byte to short, ushort, int, uint, long, ulong, float, double, or decimal
                // short to int, long, float, double, or decimal
                // ushort to int, uint, long, ulong, float, double, or decimal
                // int to long, float, double, or decimal
                // uint to long, ulong, float, double, or decimal
                // long to float, double, or decimal
                // ulong to float, double, or decimal
                // char to ushort, int, uint, long, ulong, float, double, or decimal
                // float to double
                // Nullable type conversion
                // Reference type to object
                // Interface to base interface
                // Array to array when arrays have the same number of dimensions, there is an implicit conversion from the source element type to the destination element type and the source element type and the destination element type are reference types
                // Array type to System.Array
                // Array type to IList<> and its base interfaces
                // Delegate type to System.Delegate
                // Boxing conversion
                // Enum type to System.Enum

                if (thisParameter == otherParameter)
                {
                    return;
                }

                // Identity
                if (thisParameter == argType && otherParameter != argType)
                {
                    score++;
                    return;
                }

                if (otherParameter == argType && thisParameter != argType)
                {
                    score -= short.MaxValue;
                    return;
                }

                // Object is always last
                if (otherParameter == typeof(object))
                {
                    score++;
                    return;
                }

                if (thisParameter == typeof(object))
                {
                    score -= short.MaxValue;
                    return;
                }

                if (IsNumeric(argType))
                {
                    var thisAncestorDistance = GetAncestorDistance(argType, thisParameter);
                    var otherAncestorDistance = GetAncestorDistance(argType, otherParameter);

                    if (thisAncestorDistance < otherAncestorDistance)
                    {
                        score++;
                    }
                    else if (otherAncestorDistance < thisAncestorDistance)
                    {
                        score -= short.MaxValue;
                    }

                    return;
                }

                // Derived class to base class
                // Class to implemented interface
                var thisHasAncestor = HasAncestor(argType, thisParameter);
                var otherHasAncestor = HasAncestor(argType, otherParameter);

                if (thisHasAncestor && otherHasAncestor)
                {
                    var thisAncestorDistance = GetAncestorDistance(argType, thisParameter);
                    var otherAncestorDistance = GetAncestorDistance(argType, otherParameter);

                    if (thisAncestorDistance < otherAncestorDistance)
                    {
                        score++;
                    }
                    else if (otherAncestorDistance < thisAncestorDistance)
                    {
                        score -= short.MaxValue;
                    }
                }
                else
                {
                    if (thisHasAncestor)
                    {
                        score++;
                    }
                    else if (otherHasAncestor)
                    {
                        score -= short.MaxValue;
                    }
                }
            }

            private static bool IsNumeric(Type type)
            {
                return type == typeof(sbyte)
                       || type == typeof(byte)
                       || type == typeof(short)
                       || type == typeof(ushort)
                       || type == typeof(int)
                       || type == typeof(uint)
                       || type == typeof(long)
                       || type == typeof(ulong)
                       || type == typeof(float)
                       || type == typeof(double)
                       || type == typeof(decimal);
            }

            private static readonly Type[] _signedComparisons =
            {
                typeof(sbyte),
                typeof(short),
                typeof(int),
                typeof(long),
                typeof(float),
                typeof(double),
                typeof(decimal)
            };

            private static readonly Type[] _unsignedComparisons =
            {
                typeof(byte),
                typeof(short),
                typeof(ushort),
                typeof(int),
                typeof(uint),
                typeof(long),
                typeof(ulong),
                typeof(float),
                typeof(double),
                typeof(decimal)
            };

            private static int GetAncestorDistance(Type concreteType, Type ancestorType)
            {
                if ((IsNumeric(concreteType) || concreteType == typeof(char))
                    && IsNumeric(ancestorType))
                {
                    Type[] comparisons;

                    if (concreteType == typeof(sbyte)
                        || concreteType == typeof(short)
                        || concreteType == typeof(int)
                        || concreteType == typeof(long))
                    {
                        comparisons = _signedComparisons;
                    }
                    else
                    {
                        comparisons = _unsignedComparisons;
                    }

                    if (concreteType == typeof(char))
                    {
                        concreteType = typeof(short);
                    }

                    var concreteIndex = -1;
                    var ancestorIndex = -1;

                    for (var i = 0; i < comparisons.Length; i++)
                    {
                        if (comparisons[i] == concreteType)
                        {
                            concreteIndex = i;
                        }

                        if (comparisons[i] == ancestorType)
                        {
                            ancestorIndex = i;
                        }
                    }

                    Debug.Assert(concreteIndex != -1);
                    Debug.Assert(ancestorIndex != -1);
                    Debug.Assert(concreteIndex <= ancestorIndex);

                    return ancestorIndex - concreteIndex;

                    // Signed Value
                    //
                    // short
                    // int
                    // long
                    // float
                    // double
                    // decimal*

                    // Unsigned Value
                    //
                    // short
                    // ushort
                    // int
                    // uint
                    // long
                    // ulong
                    // float
                    // double
                    // decimal*

                    // *float and double do not convert to decimal, nor does decimal convert to
                    //  them. If 1) the given value is an integer type, 2) there is no method
                    //  with a legal integer type parameter, 3) there is a method with a decimal
                    //  parameter, and 4) there is another method with either a double or a float
                    //  parameter (or two methods with both types); then there is an ambiguous
                    //  match (CS0121).
                }

                if (ancestorType.IsInterface)
                {
                    // if type's interfaces contain ancestorType, check to see if type.BaseType's
                    // interfaces contain it, and so on. Each time, add one to the distance.

                    if (!concreteType.GetInterfaces().Contains(ancestorType))
                    {
                        return ushort.MaxValue;
                    }

                    var distance = 0;

                    while (true)
                    {
                        if (concreteType.BaseType!.GetInterfaces().Contains(ancestorType))
                        {
                            distance++;
                        }
                        else
                        {
                            break;
                        }

                        concreteType = concreteType.BaseType;
                    }

                    var typeInterfaces = concreteType.GetInterfaces();

                    return distance + typeInterfaces.Count(
                            typeInterface =>
                                typeInterface == ancestorType
                                || typeInterface.GetInterfaces().Any(typeInterfaceInterface =>
                                    typeInterfaceInterface == ancestorType));
                }

                if (ancestorType.IsClass)
                {
                    var count = 0;

                    while (true)
                    {
                        if (concreteType == ancestorType)
                        {
                            return count;
                        }

                        count++;

                        if (concreteType == typeof(object) || concreteType is null)
                        {
                            return ushort.MaxValue;
                        }

                        concreteType = concreteType.BaseType!;
                    }
                }

                return ushort.MaxValue;
            }

            private static bool HasAncestor(Type type, Type ancestorType)
            {
                if (ancestorType == typeof(object))
                {
                    return false;
                }

                if (ancestorType.IsInterface)
                {
                    return type.GetInterfaces().Any(i => i == ancestorType);
                }

                if (ancestorType.IsClass)
                {
                    while (true)
                    {
                        type = type.BaseType!;

                        if (type == ancestorType)
                        {
                            return true;
                        }

                        if (type == typeof(object) || type is null)
                        {
                            return false;
                        }
                    }
                }

                return false;
            }

            public bool IsLegal(Type[] argTypes, IList<Type> typeArguments)
            {
                if (typeArguments.Count > 0)
                {
                    if (typeArguments.Count != _genericArguments.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < _genericArguments.Length; i++)
                    {
                        if (!CanBeAssigned(_genericArguments[i], typeArguments[i], Type.EmptyTypes))
                        {
                            return false;
                        }
                    }
                }

                if (argTypes.Length != _parameters.Length)
                {
                    if (argTypes.Length > _parameters.Length
                        || argTypes.Length < _parameters.Length - _defaultParameterCount)
                    {
                        return false;
                    }

                    argTypes = AddDefaultParameterTypes(argTypes);
                }

                for (int i = 0; i < argTypes.Length; i++)
                {
                    var argType = argTypes[i];

                    if (argType is not null)
                    {
                        if (!CanBeAssigned(_parameters[i], argType, typeArguments))
                        {
                            return false;
                        }
                    }
                    else if (!CanBeAssignedNull(_parameters[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private Type[] AddDefaultParameterTypes(Type[] argTypes)
            {
                var newArgTypes = new Type[_parameters.Length];

                for (int i = 0; i < _defaultParameterTypes.Length; i++)
                {
                    if (i >= argTypes.Length)
                    {
                        newArgTypes[i] = _defaultParameterTypes[i];
                    }
                    else
                    {
                        newArgTypes[i] = argTypes[i];
                    }
                }

                return newArgTypes;
            }
        }

        private static bool CanBeAssignedNull(Type type)
        {
            return _canBeAssignedNullCache.GetOrAdd(type, t =>
                !t.IsValueType || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)));
        }

        private static bool CanBeAssigned(Type targetType, Type valueType, IList<Type> typeArguments)
        {
            // ref and out parameters need to be unwrapped.
            if (targetType.IsByRef)
            {
                targetType = targetType.GetElementType()!;
            }

            var typeArgument =
                targetType.IsGenericParameter && typeArguments.Count > 0
                    ? typeArguments[targetType.GenericParameterPosition]
                    : null;

            return _canBeAssignedCache.GetOrAdd(
                Tuple.Create(targetType, valueType, typeArgument!),
                tuple => GetCanBeAssignedValue(tuple.Item1, tuple.Item2, tuple.Item3));
        }

        private static bool GetCanBeAssignedValue(Type targetType, Type valueType, Type typeArgument)
        {
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                if (targetType.IsAssignableFrom(valueType))
                    return true;

                var targetArguments = targetType.GetGenericArguments();
                var valueArguments = valueType.GetGenericArguments();

                if (targetArguments.Length != valueArguments.Length)
                    return false;

                return targetArguments.Zip(valueArguments, (target, value) => Tuple.Create(target, value))
                    .All(x => GetCanBeAssignedValue(x.Item1, x.Item2, typeArgument));
            }
            else if (targetType.IsGenericParameter)
            {
                var constraints = targetType.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;

                if (constraints != GenericParameterAttributes.None)
                {
                    if ((constraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                        && !valueType.IsValueType && valueType.GetConstructor(Type.EmptyTypes) is null)
                    {
                        return false;
                    }

                    if ((constraints & GenericParameterAttributes.ReferenceTypeConstraint) != 0
                        && valueType.IsValueType)
                    {
                        return false;
                    }

                    if ((constraints & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0
                        && !valueType.IsValueType)
                    {
                        return false;
                    }
                }

                if (typeArgument is not null
                    && !GetCanNonGenericParameterBeAssignedValue(typeArgument, valueType))
                {
                    return false;
                }

                return targetType.GetGenericParameterConstraints()
                    .All(constraint => GetCanBeAssignedValue(constraint, valueType, typeArgument!));
            }

            return GetCanNonGenericParameterBeAssignedValue(targetType, valueType);
        }

        private static bool GetCanNonGenericParameterBeAssignedValue(Type targetType, Type valueType)
        {
            // Nullable target type needs to be unwrapped.
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                targetType = targetType.GetGenericArguments()[0];
            }

            if (targetType.IsAssignableFrom(valueType))
            {
                return true;
            }

            // sbyte to short, int, long, float, double, or decimal
            if (valueType == typeof(sbyte))
            {
                return targetType == typeof(short)
                       || targetType == typeof(int)
                       || targetType == typeof(long)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // byte to short, ushort, int, uint, long, ulong, float, double, or decimal
            if (valueType == typeof(byte))
            {
                return targetType == typeof(short)
                       || targetType == typeof(ushort)
                       || targetType == typeof(int)
                       || targetType == typeof(uint)
                       || targetType == typeof(long)
                       || targetType == typeof(ulong)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // short to int, long, float, double, or decimal
            if (valueType == typeof(short))
            {
                return targetType == typeof(int)
                       || targetType == typeof(long)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // ushort to int, uint, long, ulong, float, double, or decimal
            if (valueType == typeof(ushort))
            {
                return targetType == typeof(int)
                       || targetType == typeof(uint)
                       || targetType == typeof(long)
                       || targetType == typeof(ulong)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // int to long, float, double, or decimal
            if (valueType == typeof(int))
            {
                return targetType == typeof(long)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // uint to long, ulong, float, double, or decimal
            if (valueType == typeof(uint))
            {
                return targetType == typeof(long)
                       || targetType == typeof(ulong)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // long to float, double, or decimal
            if (valueType == typeof(long))
            {
                return targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // ulong to float, double, or decimal
            if (valueType == typeof(ulong))
            {
                return targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // char to ushort, int, uint, long, ulong, float, double, or decimal
            if (valueType == typeof(char))
            {
                return targetType == typeof(ushort)
                       || targetType == typeof(int)
                       || targetType == typeof(uint)
                       || targetType == typeof(long)
                       || targetType == typeof(ulong)
                       || targetType == typeof(float)
                       || targetType == typeof(double)
                       || targetType == typeof(decimal);
            }

            // float to double
            if (valueType == typeof(float))
            {
                return targetType == typeof(double);
            }

            return false;
        }

        private sealed class CreateInstanceDefinition
        {
            private readonly Type _type;
            private readonly Type[] _argTypes;

            public CreateInstanceDefinition(Type type, object[] args)
                : this(type, args.Select(arg => arg is not null ? (arg is UniversalMemberAccessor ? ((UniversalMemberAccessor)arg)._instance!.GetType() : arg.GetType()) : null).ToArray())
            {
            }

            public CreateInstanceDefinition(Type type, Type?[] argTypes)
            {
                _type = type;
                _argTypes = argTypes!;
            }

            public Type Type
            {
                get { return _type; }
            }

            public Type[] ArgTypes
            {
                get { return _argTypes; }
            }

            public override bool Equals(object? obj)
            {
                if (ReferenceEquals(this, obj))
                {
                    return true;
                }

                var other = obj as CreateInstanceDefinition;

                if (other is null
                    || !(_type == other._type)
                    || _argTypes.Length != other._argTypes.Length)
                {
                    return false;
                }

                return !_argTypes.Where((argType, i) => !(argType == other._argTypes[i])).Any();
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = _type.GetHashCode();
                    return _argTypes.Aggregate(hashCode, (current, argType) =>
                        (current * 397) ^ (argType is null ? 0 : argType.GetHashCode()));
                }
            }
        }

        private sealed class InvokeMethodDefinition
        {
            private readonly Type _type;
            private readonly string _name;
            private readonly IList<Type> _typeArguments;
            private readonly Type?[] _argTypes;

            public InvokeMethodDefinition(Type type, string name, IList<Type> typeArguments, object[] args)
            {
                _type = type;
                _name = name;
                _typeArguments = typeArguments;
                _argTypes = args.Select(arg => arg is not null ? (arg is UniversalMemberAccessor ? ((UniversalMemberAccessor)arg)._instance!.GetType() : arg.GetType()) : null).ToArray();
            }

            public Type Type
            {
                get { return _type; }
            }

            public string Name
            {
                get { return _name; }
            }

            public IList<Type> TypeArguments
            {
                get { return _typeArguments; }
            }

            public Type?[] ArgTypes
            {
                get { return _argTypes; }
            }

            public override bool Equals(object? obj)
            {
                if (ReferenceEquals(this, obj))
                {
                    return true;
                }

                var other = obj as InvokeMethodDefinition;

                if (other is null
                    || !(_type == other._type)
                    || !string.Equals(_name, other._name, StringComparison.Ordinal)
                    || _typeArguments.Count != other._typeArguments.Count
                    || _argTypes.Length != other._argTypes.Length)
                {
                    return false;
                }

                return !_typeArguments.Where((argType, i) => !(argType == other._typeArguments[i])).Any()
                    && !_argTypes.Where((argType, i) => !(argType == other._argTypes[i])).Any();
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = _type.GetHashCode();
#if NET48
                    hashCode = (hashCode * 397) ^ _name.GetHashCode();
#else
                    hashCode = (hashCode * 397) ^ _name.GetHashCode(StringComparison.Ordinal);
#endif
                    hashCode = _typeArguments.Aggregate(hashCode, (current, typeArgument) =>
                        (current * 397) ^ (typeArgument is null ? 0 : typeArgument.GetHashCode()));
                    return _argTypes.Aggregate(hashCode, (current, argType) =>
                        (current * 397) ^ (argType is null ? 0 : argType.GetHashCode()));
                }
            }
        }

        private class ReadonlyFields
        {
            private const int _staticValueType = 1;
            private const string _staticReferenceType = "A";
            private readonly int _instanceValueType = 2;
            private readonly string _instanceReferenceType = "B";

            public static int StaticValueType
            {
                get { return _staticValueType; }
            }

            public static string StaticReferenceType
            {
                get { return _staticReferenceType; }
            }

            public int InstanceValueType
            {
                get { return _instanceValueType; }
            }

            public string InstanceReferenceType
            {
                get { return _instanceReferenceType; }
            }
        }
    }
}
