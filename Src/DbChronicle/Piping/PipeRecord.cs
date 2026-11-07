using System.Collections.Generic;

namespace DbChronicle.Piping
{
    public sealed class PipeRecord
    {
        public PipeRecord(PipeRecordType type, string[] fields)
        {
            Type = type;
            Fields = fields ?? new string[0];
        }

        public PipeRecordType Type { get; private set; }

        public IReadOnlyList<string> Fields { get; private set; }

        public bool IsStatement
        {
            get { return Type == PipeRecordType.Statement; }
        }

        public string Sql
        {
            get { return Field(0); }
        }

        public string UnitId
        {
            get { return Field(0); }
        }

        public string Release
        {
            get { return Field(1); }
        }

        public string Severity
        {
            get { return Field(0); }
        }

        public string Code
        {
            get { return Field(1); }
        }

        public string Message
        {
            get { return Field(2); }
        }

        private string Field(int index)
        {
            if (index < 0 || index >= Fields.Count)
            {
                return "";
            }

            return Fields[index] ?? "";
        }
    }
}
