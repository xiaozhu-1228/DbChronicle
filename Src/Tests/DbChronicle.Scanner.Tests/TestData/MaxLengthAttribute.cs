using System;

namespace DbChronicle.Scanner.Tests
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MaxLengthAttribute : Attribute
    {
        public MaxLengthAttribute(int length)
        {
            Length = length;
        }

        public int Length { get; private set; }
    }
}
