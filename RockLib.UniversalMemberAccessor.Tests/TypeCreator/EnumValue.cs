namespace RockLib.Dynamic.UnitTests.TypeCreator
{
    public class EnumValue
    {
        private readonly string _name;
        private readonly int? _value;

        public EnumValue(string name, int? value = null)
        {
            _name = name;
            _value = value;
        }

#pragma warning disable CA2225
        public static implicit operator EnumValue(string name)
        {
            return new EnumValue(name);
        }
#pragma warning restore CA2225

        public string Name { get { return _name; } }
        public int? Value { get { return _value; } }
    }
}