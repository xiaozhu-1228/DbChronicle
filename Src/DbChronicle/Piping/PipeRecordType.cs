namespace DbChronicle.Piping
{
    public enum PipeRecordType
    {
        Statement = 0,
        Version = 1,
        UnitBegin = 2,
        UnitEnd = 3,
        Diagnostic = 4,
        StreamEnd = 5
    }
}
