using System;

namespace DbChronicle.Scanner.Tests
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ColumnAttribute : Attribute
    {
        public ColumnAttribute()
        {
        }

        public ColumnAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }

        public string TypeName { get; set; }
    }
}
