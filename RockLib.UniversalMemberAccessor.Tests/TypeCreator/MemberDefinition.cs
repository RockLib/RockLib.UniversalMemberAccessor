using System;
using System.Collections.ObjectModel;
using System.Reflection.Emit;

namespace RockLib.Dynamic.UnitTests.TypeCreator
{
    public class MemberDefinition
    {
        private readonly Action<TypeBuilder, Collection<FieldBuilder>> _emitTo;

        public MemberDefinition(Action<TypeBuilder, Collection<FieldBuilder>> emitTo)
        {
            _emitTo = emitTo;
        }

        public void EmitTo(TypeBuilder typeBuilder, Collection<FieldBuilder> declaredFields)
        {
            _emitTo(typeBuilder, declaredFields);
        }
    }
}