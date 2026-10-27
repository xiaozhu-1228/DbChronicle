using System;

namespace DbChronicle.Scanner.Tests
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CommentAttribute : Attribute
    {
        public CommentAttribute(string comment)
        {
            Comment = comment;
        }

        public string Comment { get; private set; }
    }
}
