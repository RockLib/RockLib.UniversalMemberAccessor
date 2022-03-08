using System;
using System.Collections.ObjectModel;
using System.Reflection.Emit;

namespace RockLib.Dynamic.UnitTests.TypeCreator
{
    public class ConstructorDefinition : MemberDefinition
    {
        public ConstructorDefinition(Action<TypeBuilder, Collection<FieldBuilder>> emitTo)
            : base(emitTo)
        {
        }
    }
}