using System;
using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Audio.WebM
{
    public class ElementDescriptor
    {
        public ElementDescriptor(ulong identifier, string name, ElementType type, string? defaultvalue, bool listEntry)
            : this(VInt.FromEncoded(identifier), name, type, defaultvalue, listEntry)
        {
        }

        public ElementDescriptor(long identifier, string name, ElementType type, string? defaultValue, bool listEntry)
            : this(VInt.FromEncoded((ulong)identifier), name, type, defaultValue, listEntry)
        {
        }

        private ElementDescriptor(VInt identifier, string name, ElementType type,string? defaultValue, bool listEntry)
        {
            if (!identifier.IsValidIdentifier && type != ElementType.None)
                throw new ArgumentException("Value is not valid identifier", nameof(identifier));

            Identifier = identifier;
            Name = name;
            Type = type;
            DefaultValue = defaultValue;
            ListEntry = listEntry;
        }
        
        public bool ListEntry { get; }

        public VInt Identifier { get; }

        public string? Name { get; }

        public ElementType Type { get; }
        
        public string? DefaultValue { get; }

        public override int GetHashCode()
        {
            int result = 17;
            result = 37*result + Identifier.GetHashCode();
            result = 37*result + (Name == null ? 0 : Name.GetHashCode());
            result = 37*result + (Type == ElementType.None ? 0 : Type.GetHashCode());
            return result;
        }

        public override bool Equals(object? obj)
        {
            if (this == obj) return true;
            
            if (obj is ElementDescriptor o2)
                return Identifier.Equals(o2.Identifier)
                       && Equals(Name, o2.Name)
                       && Type == o2.Type;
            return false;
        }

        [SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
        public override string ToString()
        {
            return $"{Name}:{Type} - id:{Identifier}";
        }

        public ElementDescriptor Named(string name)
        {
            return new ElementDescriptor(Identifier, name, Type, DefaultValue, ListEntry);
        }
    }
}