using System.Collections.Generic;

namespace DbChronicle.Domain.Operations
{
    public enum LogicalType
    {
        Boolean,
        Int8,
        Int16,
        Int32,
        Int64,
        Float32,
        Float64,
        Decimal,
        FixedString,
        String,
        Text,
        Json,
        Xml,
        DateTime,
        DateTimeOffset,
        Date,
        Time,
        Binary,
        Blob,
        Uuid
    }

    public enum ColumnAction
    {
        Add,
        Modify,
        Drop,
        Rename,
        Comment,
        Reorder
    }

    public enum ObjectAction
    {
        Add,
        Drop,
        Rename,
        Replace,
        Modify
    }

    public enum OperationStage
    {
        Before,
        After
    }

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

    public sealed class ColumnDefinition
    {
        public string Name { get; set; } = "";
        public LogicalType? Type { get; set; }
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool? Nullable { get; set; }
        public DefaultValueSpec DefaultValue { get; set; } = DefaultValueSpec.Undeclared;
        public string Comment { get; set; }
        public bool? PrimaryKey { get; set; }
        public bool? AutoIncrement { get; set; }
        public bool? Unique { get; set; }
        public bool? Unsigned { get; set; }
        public string OnUpdate { get; set; }
        public string Charset { get; set; }
        public string Collate { get; set; }
        public string ForeignKey { get; set; }

        public ColumnDefinition Clone()
        {
            return new ColumnDefinition
            {
                Name = Name,
                Type = Type,
                Length = Length,
                Precision = Precision,
                Scale = Scale,
                Nullable = Nullable,
                DefaultValue = DefaultValue,
                Comment = Comment,
                PrimaryKey = PrimaryKey,
                AutoIncrement = AutoIncrement,
                Unique = Unique,
                Unsigned = Unsigned,
                OnUpdate = OnUpdate,
                Charset = Charset,
                Collate = Collate,
                ForeignKey = ForeignKey
            };
        }
    }

    public abstract class SchemaOperation
    {
        public string Table { get; set; } = "";
        public OperationStage Stage { get; set; } = OperationStage.After;

        /// <summary>table="*" 时的显式表清单（逗号分隔），用于固定范围。</summary>
        public string Tables { get; set; }

        /// <summary>是否由通配符展开而来（用于排序：通配符语句优先执行）。</summary>
        public bool FromWildcard { get; set; }

        /// <summary>浅拷贝，供通配符（table="*"）展开时按表复制。</summary>
        public SchemaOperation Clone()
        {
            return (SchemaOperation)MemberwiseClone();
        }
    }

    public sealed class CreateTableOperation : SchemaOperation
    {
        public string Comment { get; set; }
        public List<ColumnDefinition> Columns { get; } = new List<ColumnDefinition>();
        public List<string> PrimaryKeyColumns { get; } = new List<string>();
        public string PrimaryKeyName { get; set; }
    }

    public sealed class DropTableOperation : SchemaOperation
    {
    }

    public sealed class RenameTableOperation : SchemaOperation
    {
        public string NewName { get; set; } = "";
    }

    public sealed class CommentTableOperation : SchemaOperation
    {
        public string Comment { get; set; }
    }

    public sealed class ColumnOperation : SchemaOperation
    {
        public ColumnAction Action { get; set; }
        public ColumnDefinition Definition { get; set; } = new ColumnDefinition();
        public string RenamedFrom { get; set; }
        public string After { get; set; }
        public string Comment { get; set; }

        public string Name
        {
            get { return Definition.Name; }
            set { Definition.Name = value; }
        }
    }

    public sealed class IndexOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public string NewName { get; set; }
        public bool Unique { get; set; }
        public string Type { get; set; }
        public List<string> Columns { get; } = new List<string>();
        public string Expression { get; set; }
        public string Where { get; set; }
    }

    public sealed class ForeignKeyOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public List<string> Columns { get; } = new List<string>();
        public string RefTable { get; set; }
        public List<string> RefColumns { get; } = new List<string>();
        public string OnDelete { get; set; }
        public string OnUpdate { get; set; }
    }

    public sealed class PrimaryKeyOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; }
        public List<string> Columns { get; } = new List<string>();
    }

    public sealed class UniqueConstraintOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public List<string> Columns { get; } = new List<string>();
    }

    public sealed class CheckConstraintOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public string Expression { get; set; }
    }

    public sealed class DefaultConstraintOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public string Column { get; set; }
        public string Value { get; set; }
    }

    public sealed class RawSqlOperation : SchemaOperation
    {
        public Dictionary<string, string> SqlByDialect { get; } = new Dictionary<string, string>();
    }

    public sealed class DataMigrationOperation : SchemaOperation
    {
        public Dictionary<string, string> SqlByDialect { get; } = new Dictionary<string, string>();
    }

    public sealed class SeedOperation : SchemaOperation
    {
        public string Conflict { get; set; }
        public List<string> Columns { get; } = new List<string>();
        public List<List<string>> Rows { get; } = new List<List<string>>();
    }

    public sealed class RebuildTableOperation : SchemaOperation
    {
        public List<ColumnDefinition> Columns { get; } = new List<ColumnDefinition>();
        public List<string> PrimaryKeyColumns { get; } = new List<string>();
        public List<KeyValuePair<string, string>> ColumnMappings { get; } = new List<KeyValuePair<string, string>>();
    }

    public sealed class UpdateOperation : SchemaOperation
    {
        public string Set { get; set; }

        public string Where { get; set; }
    }

    public sealed class DeleteOperation : SchemaOperation
    {
        public string Where { get; set; }
    }

    public sealed class CleanOperation : SchemaOperation
    {
        public string Rules { get; set; }

        public string Where { get; set; }
    }

    public sealed class BackfillOperation : SchemaOperation
    {
        public string Column { get; set; }

        public string Value { get; set; }

        public string Where { get; set; }
    }
}
