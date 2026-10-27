namespace DbChronicle.Models
{
    public sealed class DefaultValueSpec
    {
        public bool IsDeclared { get; private set; }
        public bool IsNone { get; private set; }
        public bool IsNullLiteral { get; private set; }
        public string RawValue { get; private set; }

        public static readonly DefaultValueSpec Undeclared = new DefaultValueSpec { RawValue = "" };

        public static DefaultValueSpec None()
        {
            return new DefaultValueSpec { IsDeclared = true, IsNone = true, RawValue = "" };
        }

        public static DefaultValueSpec ExplicitNull()
        {
            return new DefaultValueSpec { IsDeclared = true, IsNullLiteral = true, RawValue = "null" };
        }

        public static DefaultValueSpec Value(string raw)
        {
            return new DefaultValueSpec { IsDeclared = true, RawValue = raw ?? "" };
        }
    }
}