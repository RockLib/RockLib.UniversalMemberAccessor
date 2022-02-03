using System;

namespace RockLib.Dynamic.UnitTests.TypeCreator
{
    public class Parameter
    {
        private static readonly object _notADefaultValue = new object();

        private readonly Type? _type;
        private readonly string? _fieldToSet;
        private readonly object _defaultValue;

        public Parameter(Type type, string? fieldToSet = null)
            : this(_notADefaultValue, type, fieldToSet)
        {
        }

        public Parameter(object defaultValue, Type type, string? fieldToSet = null)
        {
            if (defaultValue != _notADefaultValue)
            {
                if (type is not null)
                {
                    if (type.IsValueType)
                    {
                        if (defaultValue is null)
                        {
                            throw new ArgumentException("defaultValue cannot be null when 'type' is a value type.", nameof(defaultValue));
                        }

                        if (type != defaultValue.GetType())
                        {
                            throw new ArgumentException("defaultValue must be the same type as 'type' when 'type' is a value type.", nameof(defaultValue));
                        }
                    }
                    else
                    {
                        if (defaultValue is not null)
                        {
                            throw new ArgumentException("defaultValue must be null when 'type' is a reference type.", nameof(defaultValue));
                        }
                    }
                }
            }

            _defaultValue = defaultValue!;
            _type = type;
            _fieldToSet = fieldToSet;
        }

        public Type? Type { get { return _type; } }
        public string? FieldToSet { get { return _fieldToSet; } }
        public object DefaultValue { get { return _defaultValue; } }
        public bool HasDefaultValue { get { return _defaultValue != _notADefaultValue; } }

#pragma warning disable CA2225 // Not implementing fix for a test
        public static implicit operator Parameter(Type type)
        {
            return new Parameter(type);
        }
#pragma warning restore CA2225
    }
}