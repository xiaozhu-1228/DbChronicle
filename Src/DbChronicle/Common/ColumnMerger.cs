using System;

using DbChronicle.Models;

namespace DbChronicle.Common
{
    public static class ColumnMerger
    {
        public static ColumnDefinition Merge(ColumnDefinition baseDefinition, ColumnDefinition overlay)
        {
            var result = baseDefinition.Clone();
            result.Name = overlay.Name;

            if (overlay.Type.HasValue)
            {
                result.Type = overlay.Type;
            }
            if (overlay.Length.HasValue)
            {
                result.Length = overlay.Length;
            }
            if (overlay.Precision.HasValue)
            {
                result.Precision = overlay.Precision;
            }
            if (overlay.Scale.HasValue)
            {
                result.Scale = overlay.Scale;
            }
            if (overlay.Nullable.HasValue)
            {
                result.Nullable = overlay.Nullable;
            }
            if (overlay.DefaultValue != null && overlay.DefaultValue.IsDeclared)
            {
                result.DefaultValue = overlay.DefaultValue;
            }
            if (overlay.Comment != null)
            {
                result.Comment = overlay.Comment;
            }
            if (overlay.PrimaryKey.HasValue)
            {
                result.PrimaryKey = overlay.PrimaryKey;
            }
            if (overlay.AutoIncrement.HasValue)
            {
                result.AutoIncrement = overlay.AutoIncrement;
            }
            if (overlay.Unique.HasValue)
            {
                result.Unique = overlay.Unique;
            }
            if (overlay.Unsigned.HasValue)
            {
                result.Unsigned = overlay.Unsigned;
            }
            if (overlay.OnUpdate != null)
            {
                result.OnUpdate = overlay.OnUpdate;
            }
            if (overlay.Charset != null)
            {
                result.Charset = overlay.Charset;
            }
            if (overlay.Collate != null)
            {
                result.Collate = overlay.Collate;
            }
            if (overlay.ForeignKey != null)
            {
                result.ForeignKey = overlay.ForeignKey;
            }

            return result;
}
}
}