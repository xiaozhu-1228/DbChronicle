using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml.Linq;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Configuration;
using DbChronicle.Diagnostics;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

namespace DbChronicle.Parsing
{
    public sealed class ChangeUnitParser
    {
        private static readonly Regex FileNamePattern =
            new Regex(@"^(\d{8}_\d{6})_([0-9a-f]{6})_([a-z0-9_]+)\.xml$", RegexOptions.IgnoreCase);

        private readonly DbChronicleConfig _config;

        public ChangeUnitParser(DbChronicleConfig config)
        {
            _config = config;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        public ChangeUnit Parse(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var unit = new ChangeUnit
            {
                SourcePath = fullPath,
                Checksum = ComputeChecksum(fullPath)
            };

            XDocument document;
            try
            {
                document = XDocument.Load(fullPath);
            }
            catch (Exception ex)
            {
                AddError(unit, "E001", null, null, "XML 解析失败：" + ex.Message);
                return unit;
            }

            var root = document.Root;
            if (root == null || root.Name.LocalName != "changeUnit")
            {
                AddError(unit, "E002", null, null, "根节点必须是 <changeUnit>");
                return unit;
            }

            unit.Id = Attr(root, "id");
            unit.Author = Attr(root, "author");
            unit.Release = Attr(root, "release");
            unit.Description = (string)root.Element("description") ?? "";

            foreach (var dependency in root.Elements("depends"))
            {
                var on = Attr(dependency, "on");
                if (!string.IsNullOrEmpty(on))
                {
                    unit.Depends.Add(on);
                }
            }

            ValidateFileName(unit, fullPath);
            ValidateRelease(unit);
            ValidateEncoding(unit, fullPath);

            foreach (var table in root.Elements("table"))
            {
                ParseTable(unit, table);
            }

            foreach (var rawSql in root.Elements("rawSql"))
            {
                var operation = new RawSqlOperation { Table = Attr(rawSql, "name") };
                ReadRawSql(unit, rawSql, operation);
                operation.Stage = ParseStage(unit, rawSql, operation.Table, null);
                unit.Operations.Add(operation);
            }

            foreach (var element in root.Elements("view"))
            {
                ParseView(unit, element);
            }

            foreach (var element in root.Elements("sequence"))
            {
                ParseSequence(unit, element);
            }

            // 触发器 / 存储过程 / 函数：语法差异是结构性的，按方言写 SQL 原样透传
            foreach (var name in new[] { "trigger", "procedure", "function" })
            {
                foreach (var element in root.Elements(name))
                {
                    var operation = new RawSqlOperation { Table = Attr(element, "name") };
                    ReadDialectSql(element, operation.SqlByDialect);
                    if (operation.SqlByDialect.Count == 0)
                    {
                        AddError(unit, DiagnosticCodes.RawSqlEmpty, operation.Table, null,
                            "<" + name + "> 的 <sql> 子节点内容为空：请直接写 SQL 文本（长脚本建议用 CDATA）");
                        continue;
                    }

                    operation.Stage = ParseStage(unit, element, Attr(element, "name"), null);
                    unit.Operations.Add(operation);
                }
            }

            ValidateDuplicates(unit);
            ValidateBaselineFrozen(unit);
            return unit;
        }

        public static string ComputeChecksum(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(stream);
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 视图：有 &lt;select&gt; 时走结构化生成（各方言自动适配 CREATE / CREATE OR REPLACE / CREATE OR ALTER）；
        /// 只有 &lt;sql dialect="..."&gt; 时保持原样透传（兼容旧写法）。
        /// </summary>
        private void ParseView(ChangeUnit unit, XElement element)
        {
            var name = Attr(element, "name");
            if (string.IsNullOrEmpty(name))
            {
                AddError(unit, "E048", null, null, "<view> 缺少 name 属性");
                return;
            }

            var action = ParseObjectAction(Attr(element, "action"));
            var selectElement = element.Element("select");
            var select = selectElement != null ? selectElement.Value.Trim() : Attr(element, "select");
            if (string.IsNullOrEmpty(select))
            {
                if (action == ObjectAction.Drop)
                {
                    unit.Operations.Add(new ViewOperation { Table = name, Name = name, Action = ObjectAction.Drop });
                    return;
                }

                var raw = new RawSqlOperation { Table = name };
                ReadDialectSql(element, raw.SqlByDialect);
                if (raw.SqlByDialect.Count == 0)
                {
                    AddError(unit, "E048", name, null,
                        "<view> 必须提供 <select> 子节点（结构化生成），或按方言的 <sql> 子节点（原样透传）");
                    return;
                }
                unit.Operations.Add(raw);
                return;
            }

            unit.Operations.Add(new ViewOperation
            {
                Table = name,
                Name = name,
                Action = action,
                Select = select,
                Columns = Attr(element, "columns"),
                Comment = Attr(element, "comment")
            });
        }

        /// <summary>
        /// 序列：结构化属性生成（PG / SQL Server 原生支持，其余方言编译报错）；
        /// 带 &lt;sql&gt; 时按方言原样透传。
        /// </summary>
        private void ParseSequence(ChangeUnit unit, XElement element)
        {
            var name = Attr(element, "name");
            if (string.IsNullOrEmpty(name))
            {
                AddError(unit, "E049", null, null, "<sequence> 缺少 name 属性");
                return;
            }

            if (element.Element("sql") != null)
            {
                var raw = new RawSqlOperation { Table = name };
                ReadDialectSql(element, raw.SqlByDialect);
                if (raw.SqlByDialect.Count == 0)
                {
                    AddError(unit, DiagnosticCodes.RawSqlEmpty, name, null,
                        "<sequence> 的 <sql> 子节点内容为空：请直接写 SQL 文本（长脚本建议用 CDATA）");
                    return;
                }

                unit.Operations.Add(raw);
                return;
            }

            unit.Operations.Add(new SequenceOperation
            {
                Table = name,
                Name = name,
                Action = ParseObjectAction(Attr(element, "action")),
                StartWith = LongAttr(element, "start"),
                IncrementBy = LongAttr(element, "increment"),
                MinValue = LongAttr(element, "minValue"),
                MaxValue = LongAttr(element, "maxValue"),
                Cycle = BoolAttr(element, "cycle"),
                Cache = LongAttr(element, "cache")
            });
        }

        private void ParseTable(ChangeUnit unit, XElement tableElement)
        {
            var tableName = Attr(tableElement, "name");
            if (string.IsNullOrEmpty(tableName))
            {
                AddError(unit, "E010", null, null, "<table> 缺少 name 属性");
                return;
            }

            var action = (Attr(tableElement, "action") ?? "ALTER").ToUpperInvariant();
            var comment = Attr(tableElement, "comment");

            switch (action)
            {
                case "CREATE":
                    ParseCreateTable(unit, tableElement, tableName, comment);
                    break;

                case "DROP":
                    if (!RequireDestructive(unit, tableName, null))
                    {
                        return;
                    }
                    unit.Operations.Add(new DropTableOperation { Table = tableName });
                    break;

                case "RENAME":
                    var newName = Attr(tableElement, "newName");
                    if (string.IsNullOrEmpty(newName))
                    {
                        AddError(unit, "E011", tableName, null, "<table action=\"RENAME\"> 缺少 newName");
                        return;
                    }
                    unit.Operations.Add(new RenameTableOperation { Table = tableName, NewName = newName });
                    break;

                case "COMMENT":
                    unit.Operations.Add(new CommentTableOperation { Table = tableName, Comment = comment ?? "" });
                    break;

                case "ALTER":
                case "REBUILD":
                    if (action == "REBUILD")
                    {
                        if (!RequireDestructive(unit, tableName, null))
                        {
                            return;
                        }
                        unit.Diagnostics.Add(CompileDiagnostic.Warning("W018",
                            "表 " + tableName + " 的 REBUILD 仅做破坏性校验，按 ALTER 处理（SQLite 会自动 rebuild，其他方言请用 <rawSql>）"));
                    }
                    ParseAlterTable(unit, tableElement, tableName);
                    break;

                case "ENGINE":
                case "CHARSET":
                case "TABLESPACE":
                    var tableOption = new TableOptionOperation
                    {
                        Table = tableName,
                        Engine = Attr(tableElement, "engine"),
                        Charset = Attr(tableElement, "charset"),
                        Collate = Attr(tableElement, "collate"),
                        Tablespace = Attr(tableElement, "tablespace")
                    };
                    if (string.IsNullOrEmpty(tableOption.Engine) && string.IsNullOrEmpty(tableOption.Charset)
                        && string.IsNullOrEmpty(tableOption.Collate) && string.IsNullOrEmpty(tableOption.Tablespace))
                    {
                        AddError(unit, "E050", tableName, null,
                            "<table action=\"ENGINE|CHARSET|TABLESPACE\"> 至少需要 engine / charset / collate / tablespace 之一");
                        return;
                    }
                    unit.Operations.Add(tableOption);
                    break;

                default:
                    AddError(unit, "E012", tableName, null,
                        "未知的表级 action：" + action
                        + "（PARTITION / GRANT / SYNONYM / 索引提示等请用 <rawSql> 实现）");
                    break;
            }
        }

        private void ParseCreateTable(ChangeUnit unit, XElement tableElement, string tableName, string comment)
        {
            var operation = new CreateTableOperation { Table = tableName, Comment = comment };

            foreach (var columnElement in tableElement.Elements("column"))
            {
                var column = ParseColumn(unit, columnElement, tableName, true);
                if (column == null)
                {
                    continue;
                }
                if (column.PrimaryKey == true)
                {
                    operation.PrimaryKeyColumns.Add(column.Name);
                }
                operation.Columns.Add(column);
            }

            foreach (var primaryKey in tableElement.Elements("primaryKey"))
            {
                foreach (var name in SplitColumns(Attr(primaryKey, "columns")))
                {
                    if (!operation.PrimaryKeyColumns.Contains(name))
                    {
                        operation.PrimaryKeyColumns.Add(name);
                    }
                }
                operation.PrimaryKeyName = Attr(primaryKey, "name");
            }

            unit.Operations.Add(operation);

            foreach (var element in tableElement.Elements())
            {
                var localName = element.Name.LocalName;
                if (localName == "column" || localName == "primaryKey")
                {
                    continue;
                }
                if (localName == "foreignKey")
                {
                    ForeignKeyOperation foreignKey;
                    if (TryParseForeignKey(unit, element, tableName, out foreignKey))
                    {
                        foreignKey.PartOfCreateTable = true;
                        if (foreignKey.Action != ObjectAction.Drop)
                        {
                            operation.ForeignKeys.Add(foreignKey);
                        }
                        unit.Operations.Add(foreignKey);
                    }
                    continue;
                }
                ParseTableChild(unit, element, tableName);
            }
        }

        private void ParseAlterTable(ChangeUnit unit, XElement tableElement, string tableName)
        {
            var explicitTables = Attr(tableElement, "tables");
            var start = unit.Operations.Count;

            foreach (var element in tableElement.Elements())
            {
                ParseTableChild(unit, element, tableName);
            }

            if (!string.IsNullOrEmpty(explicitTables))
            {
                for (var i = start; i < unit.Operations.Count; i++)
                {
                    unit.Operations[i].Tables = explicitTables;
                }
            }
        }

        private void ParseTableChild(ChangeUnit unit, XElement element, string tableName)
        {
            switch (element.Name.LocalName)
            {
                case "column":
                    var columnOperation = ParseColumnOperation(unit, element, tableName);
                    if (columnOperation != null)
                    {
                        unit.Operations.Add(columnOperation);
                    }
                    break;

                case "index":
                    var index = new IndexOperation
                    {
                        Table = tableName,
                        Action = ParseObjectAction(Attr(element, "action")),
                        Name = Attr(element, "name"),
                        NewName = Attr(element, "newName"),
                        Unique = BoolAttr(element, "unique") ?? false,
                        Type = Attr(element, "type"),
                        Expression = Attr(element, "expression"),
                        Where = Attr(element, "where"),
                        Comment = Attr(element, "comment")
                    };
                    FillColumns(element, index.Columns, Attr(element, "columns"));
                    unit.Operations.Add(index);
                    break;

                case "constraintComment":
                    var constraintComment = new ConstraintCommentOperation
                    {
                        Table = tableName,
                        Name = Attr(element, "name"),
                        Kind = Attr(element, "kind"),
                        Comment = Attr(element, "comment")
                    };
                    if (string.IsNullOrEmpty(constraintComment.Name))
                    {
                        AddError(unit, "E043", tableName, null, "<constraintComment> 缺少 name 属性");
                        break;
                    }
                    unit.Operations.Add(constraintComment);
                    break;

                case "foreignKey":
                    ForeignKeyOperation tableForeignKey;
                    if (TryParseForeignKey(unit, element, tableName, out tableForeignKey))
                    {
                        unit.Operations.Add(tableForeignKey);
                    }
                    break;

                case "primaryKey":
                    var primaryKey = new PrimaryKeyOperation
                    {
                        Table = tableName,
                        Action = ParseObjectAction(Attr(element, "action")),
                        Name = Attr(element, "name")
                    };
                    FillColumns(element, primaryKey.Columns, Attr(element, "columns"));
                    unit.Operations.Add(primaryKey);
                    break;

                case "uniqueConstraint":
                    var unique = new UniqueConstraintOperation
                    {
                        Table = tableName,
                        Action = ParseObjectAction(Attr(element, "action")),
                        Name = Attr(element, "name")
                    };
                    FillColumns(element, unique.Columns, Attr(element, "columns"));
                    unit.Operations.Add(unique);
                    break;

                case "checkConstraint":
                    var check = new CheckConstraintOperation
                    {
                        Table = tableName,
                        Action = ParseObjectAction(Attr(element, "action")),
                        Name = Attr(element, "name"),
                        Expression = Attr(element, "expression")
                    };
                    if (check.Action != ObjectAction.Drop && string.IsNullOrEmpty(check.Expression))
                    {
                        AddError(unit, "E021", tableName, null, "<checkConstraint> 缺少 expression");
                        break;
                    }
                    unit.Operations.Add(check);
                    break;

                case "defaultConstraint":
                    unit.Operations.Add(new DefaultConstraintOperation
                    {
                        Table = tableName,
                        Action = ParseObjectAction(Attr(element, "action")),
                        Name = Attr(element, "name"),
                        Column = Attr(element, "column"),
                        Value = Attr(element, "value")
                    });
                    break;

                case "dataMigration":
                    var migration = new DataMigrationOperation
                    {
                        Table = tableName,
                        Stage = ParseStage(unit, element, tableName, null)
                    };
                    ReadDialectSql(element, migration.SqlByDialect);
                    unit.Operations.Add(migration);
                    break;

                case "seed":
                    unit.Operations.Add(ParseSeed(unit, element, tableName));
                    break;

                case "update":
                    var update = new UpdateOperation
                    {
                        Table = tableName,
                        Set = Attr(element, "set"),
                        Where = Attr(element, "where"),
                        Stage = ParseStage(unit, element, tableName, null)
                    };
                    if (string.IsNullOrEmpty(update.Set))
                    {
                        AddError(unit, "E022", tableName, null, "<update> 缺少 set 属性");
                        break;
                    }
                    unit.Operations.Add(update);
                    break;

                case "delete":
                    if (!RequireDestructive(unit, tableName, null))
                    {
                        break;
                    }
                    unit.Operations.Add(new DeleteOperation
                    {
                        Table = tableName,
                        Where = Attr(element, "where"),
                        Stage = ParseStage(unit, element, tableName, null)
                    });
                    break;

                case "clean":
                    var clean = new CleanOperation
                    {
                        Table = tableName,
                        Rules = Attr(element, "rules"),
                        Where = Attr(element, "where"),
                        Stage = ParseStage(unit, element, tableName, null)
                    };
                    if (string.IsNullOrEmpty(clean.Rules))
                    {
                        AddError(unit, "E023", tableName, null, "<clean> 缺少 rules 属性（形如 \"email = NULL; phone = NULL\"）");
                        break;
                    }
                    unit.Operations.Add(clean);
                    break;

                case "backfill":
                    var backfill = new BackfillOperation
                    {
                        Table = tableName,
                        Column = Attr(element, "column"),
                        Value = Attr(element, "value"),
                        Where = Attr(element, "where"),
                        Stage = ParseStage(unit, element, tableName, Attr(element, "column"))
                    };
                    if (string.IsNullOrEmpty(backfill.Column) || backfill.Value == null)
                    {
                        AddError(unit, "E024", tableName, backfill.Column, "<backfill> 必须提供 column 与 value");
                        break;
                    }
                    unit.Operations.Add(backfill);
                    break;

                default:
                    unit.Diagnostics.Add(CompileDiagnostic.Warning("W020", "忽略未知节点 <" + element.Name.LocalName + ">（表 " + tableName + "）"));
                    break;
            }
        }

        private bool TryParseForeignKey(ChangeUnit unit, XElement element, string tableName, out ForeignKeyOperation foreignKey)
        {
            foreignKey = new ForeignKeyOperation
            {
                Table = tableName,
                Action = ParseObjectAction(Attr(element, "action")),
                Name = Attr(element, "name"),
                RefTable = Attr(element, "refTable"),
                OnDelete = Attr(element, "onDelete"),
                OnUpdate = Attr(element, "onUpdate")
            };
            FillColumns(element, foreignKey.Columns, Attr(element, "columns"));
            foreach (var name in SplitColumns(Attr(element, "refColumns")))
            {
                foreignKey.RefColumns.Add(name);
            }
            if (foreignKey.Action != ObjectAction.Drop && string.IsNullOrEmpty(foreignKey.RefTable))
            {
                AddError(unit, "E020", tableName, null, "<foreignKey> 缺少 refTable");
                return false;
            }
            return true;
        }

        private SeedOperation ParseSeed(ChangeUnit unit, XElement element, string tableName)
        {
            var seed = new SeedOperation
            {
                Table = tableName,
                Conflict = Attr(element, "conflict"),
                Stage = ParseStage(unit, element, tableName, null)
            };

            foreach (var name in SplitColumns(Attr(element, "columns")))
            {
                seed.Columns.Add(name);
            }

            foreach (var row in element.Elements("row"))
            {
                seed.Rows.Add(SplitValues(Attr(row, "values")));
            }

            return seed;
        }

        private ColumnOperation ParseColumnOperation(ChangeUnit unit, XElement element, string tableName)
        {
            var actionText = (Attr(element, "action") ?? "ADD").ToUpperInvariant();
            ColumnAction action;
            switch (actionText)
            {
                case "ADD":
                    action = ColumnAction.Add;
                    break;
                case "MODIFY":
                    action = ColumnAction.Modify;
                    break;
                case "DROP":
                    action = ColumnAction.Drop;
                    break;
                case "RENAME":
                    action = ColumnAction.Rename;
                    break;
                case "COMMENT":
                    action = ColumnAction.Comment;
                    break;
                case "REORDER":
                    action = ColumnAction.Reorder;
                    break;
                default:
                    AddError(unit, "E013", tableName, Attr(element, "name"), "未知的列级 action：" + actionText);
                    return null;
            }

            if (action == ColumnAction.Drop && !RequireDestructive(unit, tableName, Attr(element, "name")))
            {
                return null;
            }

            var definition = ParseColumn(unit, element, tableName, false);
            if (definition == null)
            {
                return null;
            }

            var operation = new ColumnOperation
            {
                Table = tableName,
                Action = action,
                Definition = definition,
                RenamedFrom = Attr(element, "renamedFrom"),
                After = Attr(element, "after"),
                Comment = Attr(element, "comment"),
                Stage = ParseStage(unit, element, tableName, definition.Name)
            };

            if (action == ColumnAction.Rename && string.IsNullOrEmpty(operation.RenamedFrom))
            {
                AddError(unit, "E014", tableName, definition.Name, "RENAME 必须提供 renamedFrom");
                return null;
            }

            if (action == ColumnAction.Reorder && element.Attribute("before") != null)
            {
                AddError(unit, DiagnosticCodes.ColumnReorderBeforeUnsupported, tableName, definition.Name,
                    "REORDER 不支持 before 属性，请改用 after=\"列名\" 或 after=\"FIRST\"");
                return null;
            }

            if (action == ColumnAction.Modify)
            {
                if (_config.Options.IncrementalModify)
                {
                    if (!HasDeclaredColumnAttribute(element))
                    {
                        AddError(unit, "E028", tableName, definition.Name, "MODIFY 在增量模式下至少需要声明一项属性");
                        return null;
                    }
                }
                else if (!definition.Type.HasValue || !definition.Nullable.HasValue)
                {
                    AddError(unit, "E027", tableName, definition.Name,
                        "MODIFY 在 full 模式（incrementalModify=false）下必须声明 type 与 nullable");
                    return null;
                }
            }

            return operation;
        }

        private ColumnDefinition ParseColumn(ChangeUnit unit, XElement element, string tableName, bool forCreate)
        {
            var name = Attr(element, "name");
            if (string.IsNullOrEmpty(name))
            {
                AddError(unit, "E015", tableName, null, "<column> 缺少 name 属性");
                return null;
            }

            var definition = new ColumnDefinition { Name = name };
            var typeText = Attr(element, "type");

            if (!string.IsNullOrEmpty(typeText))
            {
                LogicalType type;
                if (!Enum.TryParse(typeText, false, out type) || !Enum.IsDefined(typeof(LogicalType), type))
                {
                    AddError(unit, "E016", tableName, name, "未知逻辑类型：" + typeText);
                    return null;
                }
                definition.Type = type;
            }
            else if (forCreate)
            {
                AddError(unit, "E017", tableName, name, "列缺少 type（逻辑类型）");
                return null;
            }

            definition.Length = IntAttr(element, "length");
            definition.Precision = IntAttr(element, "precision");
            definition.Scale = IntAttr(element, "scale");
            definition.Nullable = BoolAttr(element, "nullable");
            definition.PrimaryKey = BoolAttr(element, "primaryKey");
            definition.AutoIncrement = BoolAttr(element, "autoIncrement");
            definition.Unique = BoolAttr(element, "unique");
            definition.Unsigned = BoolAttr(element, "unsigned");
            definition.OnUpdate = Attr(element, "onUpdate");
            definition.Charset = Attr(element, "charset");
            definition.Collate = Attr(element, "collate");
            definition.ForeignKey = Attr(element, "foreignKey");

            if (!string.IsNullOrEmpty(definition.ForeignKey) && !IsForeignKeyReference(definition.ForeignKey))
            {
                AddError(unit, "E031", tableName, name, "foreignKey 格式必须为 表名.列名：" + definition.ForeignKey);
                return null;
            }

            if (element.Attribute("comment") != null)
            {
                definition.Comment = Attr(element, "comment");
            }

            var defaultText = Attr(element, "defaultValue");
            if (defaultText != null)
            {
                if (string.Equals(defaultText, "null", StringComparison.OrdinalIgnoreCase))
                {
                    definition.DefaultValue = DefaultValueSpec.ExplicitNull();
                }
                else if (string.Equals(defaultText, "none", StringComparison.OrdinalIgnoreCase))
                {
                    definition.DefaultValue = DefaultValueSpec.None();
                }
                else
                {
                    definition.DefaultValue = DefaultValueSpec.Value(defaultText);
                }
            }

            if (definition.Type.HasValue)
            {
                if ((definition.Type == LogicalType.FixedString || definition.Type == LogicalType.String || definition.Type == LogicalType.Binary)
                    && !definition.Length.HasValue)
                {
                    AddError(unit, "E018", tableName, name, "类型 " + definition.Type + " 必须提供 length");
                    return null;
                }

                if (definition.Type == LogicalType.Blob && definition.Length.HasValue)
                {
                    unit.Diagnostics.Add(new CompileDiagnostic
                    {
                        Code = DiagnosticCodes.BlobLengthIgnored,
                        Severity = DiagnosticSeverity.Warning,
                        ChangeId = unit.Id,
                        Table = tableName,
                        Column = name,
                        Message = "Blob 的 length 被忽略"
                    });
                    definition.Length = null;
                }

                if (definition.Unsigned == true && !IsIntegerType(definition.Type.Value))
                {
                    AddError(unit, "E019", tableName, name, "unsigned=true 仅对整数类型有效");
                    return null;
                }

                if (!string.IsNullOrEmpty(definition.OnUpdate)
                    && definition.Type != LogicalType.DateTime
                    && definition.Type != LogicalType.DateTimeOffset)
                {
                    AddError(unit, "E025", tableName, name, "onUpdate 仅对 DateTime / DateTimeOffset 有效");
                    return null;
                }
            }

            return definition;
        }

        private bool RequireDestructive(ChangeUnit unit, string tableName, string columnName)
        {
            if (_config.Options.AllowDestructive)
            {
                return true;
            }
            AddError(unit, "E030", tableName, columnName, "破坏性操作被拒绝（需要 options.allowDestructive: true）");
            return false;
        }

        private void ValidateFileName(ChangeUnit unit, string path)
        {
            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, "00000000_000000_baseline.xml", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var match = FileNamePattern.Match(fileName);
            if (!match.Success)
            {
                AddError(unit, "E003", null, null,
                    "文件名不符合规范 {yyyyMMdd_HHmmss}_{6位随机码}_{描述}.xml：" + fileName);
                return;
            }

            if (!string.IsNullOrEmpty(unit.Id) && !fileName.StartsWith(unit.Id, StringComparison.OrdinalIgnoreCase))
            {
                AddError(unit, "E004", null, null, "id 必须与文件名前缀一致（id=" + unit.Id + "）");
            }
        }

        private void ValidateRelease(ChangeUnit unit)
        {
            if (string.IsNullOrEmpty(unit.Release))
            {
                AddError(unit, "E005", null, null, "缺少 release 属性");
                return;
            }

            if (unit.IsBaseline)
            {
                return;
            }

            if (unit.Release.Length != 8 || !IsDigits(unit.Release))
            {
                AddError(unit, "E006", null, null, "release 格式必须为 yyyyMMdd：" + unit.Release);
                return;
            }

            DateTime releaseDate;
            if (!DateTime.TryParseExact(unit.Release, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out releaseDate))
            {
                AddError(unit, "E006", null, null, "release 不是合法日期：" + unit.Release);
                return;
            }

            if (releaseDate > Clock().Date.AddDays(1))
            {
                AddError(unit, "E007", null, null, "release 不得晚于当前日期 + 1 天：" + unit.Release);
            }

            if (!string.IsNullOrEmpty(unit.Id) && unit.Id.Length >= 8)
            {
                var idDate = unit.Id.Substring(0, 8);
                if (IsDigits(idDate) && idDate != unit.Release)
                {
                    unit.Diagnostics.Add(new CompileDiagnostic
                    {
                        Code = DiagnosticCodes.ReleaseDateMismatch,
                        Severity = DiagnosticSeverity.Warning,
                        ChangeId = unit.Id,
                        Message = "release(" + unit.Release + ") 与 change_id 日期(" + idDate + ") 不一致"
                    });
                }
            }
        }

        private void ValidateBaselineFrozen(ChangeUnit unit)
        {
            if (!unit.IsBaseline || !_config.Baseline.Frozen)
            {
                return;
            }

            foreach (var operation in unit.Operations)
            {
                if (IsBaselineDefinition(operation))
                {
                    continue;
                }

                AddError(unit, "E040", operation.Table, null,
                    "基线已冻结（baseline.frozen=true），只允许建表与新增类操作（ADD / CREATE）");
                return;
            }
        }

        private static bool IsBaselineDefinition(SchemaOperation operation)
        {
            if (operation is CreateTableOperation || operation is CommentTableOperation)
            {
                return true;
            }

            var column = operation as ColumnOperation;
            if (column != null)
            {
                return column.Action == ColumnAction.Add;
            }

            var index = operation as IndexOperation;
            if (index != null)
            {
                return index.Action == ObjectAction.Add;
            }

            var foreignKey = operation as ForeignKeyOperation;
            if (foreignKey != null)
            {
                return foreignKey.Action == ObjectAction.Add;
            }

            var primaryKey = operation as PrimaryKeyOperation;
            if (primaryKey != null)
            {
                return primaryKey.Action == ObjectAction.Add;
            }

            var unique = operation as UniqueConstraintOperation;
            if (unique != null)
            {
                return unique.Action == ObjectAction.Add;
            }

            var check = operation as CheckConstraintOperation;
            if (check != null)
            {
                return check.Action == ObjectAction.Add;
            }

            var defaultConstraint = operation as DefaultConstraintOperation;
            if (defaultConstraint != null)
            {
                return defaultConstraint.Action == ObjectAction.Add;
            }

            return false;
        }

        private void ValidateEncoding(ChangeUnit unit, string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                unit.Diagnostics.Add(new CompileDiagnostic
                {
                    Code = DiagnosticCodes.FileEncodingNotStandard,
                    Severity = DiagnosticSeverity.Warning,
                    ChangeId = unit.Id,
                    Message = "文件带 UTF-8 BOM，不符合约定（会导致 checksum 漂移）"
                });
            }

            var text = Encoding.UTF8.GetString(bytes);
            if (text.Contains("\r\n"))
            {
                unit.Diagnostics.Add(new CompileDiagnostic
                {
                    Code = DiagnosticCodes.FileEncodingNotStandard,
                    Severity = DiagnosticSeverity.Warning,
                    ChangeId = unit.Id,
                    Message = "文件使用 CRLF 换行，不符合约定（会导致 checksum 漂移）"
                });
            }
        }

        /// <summary>
        /// 读取 &lt;rawSql&gt; 的内容。**推荐写法是把 SQL 直接写在节点里**（支持 CDATA）：
        /// 不做任何分析、也不区分方言——方言由启动配置决定，纯 SQL 不需要按方言分化。
        /// 兼容旧写法 &lt;rawSql&gt;&lt;sql dialect="x"&gt;…&lt;/sql&gt;&lt;/rawSql&gt;（方言字典保留，但不再推荐）。
        /// </summary>
        private void ReadRawSql(ChangeUnit unit, XElement element, RawSqlOperation operation)
        {
            if (element.Elements("sql").Any())
            {
                ReadDialectSql(element, operation.SqlByDialect);
                if (operation.SqlByDialect.Count == 0)
                {
                    AddError(unit, DiagnosticCodes.RawSqlEmpty, operation.Table, null,
                        "<rawSql> 内容为空：请直接写 SQL 文本（长脚本建议用 CDATA）");
                }
                return;
            }

            var text = element.Value.Trim();
            if (string.IsNullOrEmpty(text))
            {
                AddError(unit, DiagnosticCodes.RawSqlEmpty, operation.Table, null,
                    "<rawSql> 内容为空：请直接写 SQL 文本（长脚本建议用 CDATA）");
                return;
            }

            operation.SqlByDialect["any"] = text;
        }

        private static void ReadDialectSql(XElement element, Dictionary<string, string> target)
        {
            var inline = Attr(element, "dialect");
            var inlineValue = element.Nodes().OfType<XText>().Any() ? element.Value.Trim() : null;
            if (!string.IsNullOrEmpty(inline) && !string.IsNullOrEmpty(inlineValue))
            {
                target[inline.ToLowerInvariant()] = inlineValue;
                return;
            }

            foreach (var sql in element.Elements("sql"))
            {
                var dialect = Attr(sql, "dialect");
                if (string.IsNullOrEmpty(dialect))
                {
                    dialect = "any";
                }

                var text = sql.Value.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                target[dialect.ToLowerInvariant()] = text;
            }
        }

        private static OperationStage ParseStage(ChangeUnit unit, XElement element, string table, string column)
        {
            var text = Attr(element, "stage");
            if (string.IsNullOrEmpty(text))
            {
                return OperationStage.After;
            }

            if (string.Equals(text, "before", StringComparison.OrdinalIgnoreCase))
            {
                return OperationStage.Before;
            }

            if (string.Equals(text, "after", StringComparison.OrdinalIgnoreCase))
            {
                return OperationStage.After;
            }

            AddError(unit, "E026", table, column, "stage 仅允许 before / after：" + text);
            return OperationStage.After;
        }

        private static bool HasDeclaredColumnAttribute(XElement element)
        {
            string[] names =
            {
                "type", "length", "precision", "scale", "nullable", "defaultValue", "comment",
                "primaryKey", "autoIncrement", "unique", "unsigned", "onUpdate", "charset", "collate", "foreignKey"
            };

            foreach (var name in names)
            {
                if (element.Attribute(name) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateDuplicates(ChangeUnit unit)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var operation in unit.Operations)
            {
                var column = operation as ColumnOperation;
                if (column != null)
                {
                    var key = (column.Table ?? "") + "." + column.Name;
                    if (!seen.Add(key))
                    {
                        AddError(unit, "E029", column.Table, column.Name, "同一变更单元内，同一表同一列重复出现：" + key);
                    }
                    continue;
                }

                var create = operation as CreateTableOperation;
                if (create != null)
                {
                    foreach (var definition in create.Columns)
                    {
                        var key = (create.Table ?? "") + "." + definition.Name;
                        if (!seen.Add(key))
                        {
                            AddError(unit, "E029", create.Table, definition.Name, "同一变更单元内，同一表同一列重复出现：" + key);
                        }
                    }
                }
            }
        }

        private static ObjectAction ParseObjectAction(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return ObjectAction.Add;
            }
            switch (text.ToUpperInvariant())
            {
                case "DROP":
                    return ObjectAction.Drop;
                case "RENAME":
                    return ObjectAction.Rename;
                case "REPLACE":
                    return ObjectAction.Replace;
                case "MODIFY":
                    return ObjectAction.Modify;
                case "COMMENT":
                    return ObjectAction.Comment;
                default:
                    return ObjectAction.Add;
            }
        }

        private static void FillColumns(XElement element, List<string> target, string columnList)
        {
            foreach (var name in SplitColumns(columnList))
            {
                target.Add(name);
            }
            foreach (var columnRef in element.Elements("columnRef"))
            {
                var name = Attr(columnRef, "name");
                if (!string.IsNullOrEmpty(name))
                {
                    target.Add(name);
                }
            }
        }

        private static List<string> SplitColumns(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }
            foreach (var part in text.Split(','))
            {
                var value = part.Trim();
                if (value.Length > 0)
                {
                    result.Add(value);
                }
            }
            return result;
        }

        private static List<string> SplitValues(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }
            var current = new StringBuilder();
            var inQuote = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '\'')
                {
                    if (inQuote && i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        current.Append("''");
                        i++;
                        continue;
                    }

                    inQuote = !inQuote;
                    current.Append(ch);
                    continue;
                }

                if (ch == ',' && !inQuote)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }
            result.Add(current.ToString().Trim());
            return result;
        }

        private static bool IsIntegerType(LogicalType type)
        {
            return type == LogicalType.Int8 || type == LogicalType.Int16
                || type == LogicalType.Int32 || type == LogicalType.Int64;
        }

        /// <summary>列级外键引用必须是「表名.列名」：恰好一个点号，且两端非空。</summary>
        private static bool IsForeignKeyReference(string value)
        {
            var parts = value.Split('.');
            if (parts.Length != 2)
            {
                return false;
            }
            return !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]);
        }

        private static bool IsDigits(string text)
        {
            foreach (var ch in text)
            {
                if (ch < '0' || ch > '9')
                {
                    return false;
                }
            }
            return text.Length > 0;
        }

        private static string Attr(XElement element, string name)
        {
            var attribute = element.Attribute(name);
            return attribute == null ? null : attribute.Value;
        }

        private static long? LongAttr(XElement element, string name)
        {
            var text = Attr(element, name);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            long value;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return null;
        }

        private static bool? BoolAttr(XElement element, string name)
        {
            var text = Attr(element, name);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int? IntAttr(XElement element, string name)
        {
            var text = Attr(element, name);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            int value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return null;
        }

        private static void AddError(ChangeUnit unit, string code, string table, string column, string message)
        {
            unit.Diagnostics.Add(new CompileDiagnostic
            {
                Code = code,
                Severity = DiagnosticSeverity.Error,
                ChangeId = unit.Id,
                Table = table,
                Column = column,
                Message = message
            });
        }
    }
}