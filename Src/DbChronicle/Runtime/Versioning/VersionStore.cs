using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DbChronicle.Configuration;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Runtime.Versioning
{
    /// <summary>
    /// 版本状态存储：本地文件（{paths.versionLog}/DbchronicleState.xml），不写数据库。
    /// 语句级续跑进度也记录在同一文件的 &lt;statement&gt; 节点里。
    /// </summary>
    public sealed class VersionStore
    {
        public const string StateFileName = "DbchronicleState.xml";

        private const string RootName = "state";
        private const string AppliedName = "applied";
        private const string StatementsName = "statements";
        private const string StatementName = "statement";

        private readonly string _directory;
        private readonly object _sync = new object();

        public VersionStore(DbChronicleConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _directory = config.ResolveVersionLogPath();
        }

        public string StatePath
        {
            get { return Path.Combine(_directory, StateFileName); }
        }

        /// <summary>确保状态目录存在（保留旧方法名，语义从"建表"变为"建目录"）。</summary>
        public void EnsureTable()
        {
            Directory.CreateDirectory(_directory);
        }

        /// <summary>语句进度与主状态同文件，无需单独初始化。</summary>
        public void EnsureStatementTable()
        {
        }

        public List<AppliedChange> LoadAll()
        {
            var document = LoadDocument();
            var result = new List<AppliedChange>();
            foreach (var element in document.Root.Elements(AppliedName))
            {
                result.Add(new AppliedChange
                {
                    ChangeId = Attr(element, "changeId"),
                    Release = Attr(element, "release"),
                    Checksum = Attr(element, "checksum"),
                    Dialect = Attr(element, "dialect"),
                    AppliedBy = Attr(element, "appliedBy"),
                    Description = Attr(element, "description"),
                    AppliedAt = Attr(element, "appliedAt"),
                    HasFailure = !string.IsNullOrEmpty(Attr(element, "failureAt")),
                    FailureStatement = ParseInt(Attr(element, "failureStmt"))
                });
            }
            return result;
        }

        public Dictionary<int, string> LoadStatements(string changeId)
        {
            var result = new Dictionary<int, string>();
            var document = LoadDocument();
            var applied = Find(document, changeId);
            if (applied == null)
            {
                return result;
            }

            var statements = applied.Element(StatementsName);
            if (statements == null)
            {
                return result;
            }

            foreach (var element in statements.Elements(StatementName))
            {
                var index = ParseInt(Attr(element, "index"));
                if (index.HasValue)
                {
                    result[index.Value] = Attr(element, "hash");
                }
            }
            return result;
        }

        public void RecordStatement(string changeId, int statementIndex, string hash)
        {
            Mutate(document =>
            {
                var applied = Find(document, changeId);
                if (applied == null)
                {
                    return;
                }

                var statements = applied.Element(StatementsName);
                if (statements == null)
                {
                    statements = new XElement(StatementsName);
                    applied.Add(statements);
                }

                var existing = statements.Elements(StatementName)
                    .FirstOrDefault(x => ParseInt(Attr(x, "index")) == statementIndex);
                if (existing != null)
                {
                    existing.Remove();
                }

                statements.Add(new XElement(StatementName,
                    new XAttribute("index", statementIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("hash", hash ?? ""),
                    new XAttribute("appliedAt", Now())));
            });
        }

        public void ClearStatements(string changeId)
        {
            Mutate(document =>
            {
                var applied = Find(document, changeId);
                var statements = applied == null ? null : applied.Element(StatementsName);
                if (statements != null)
                {
                    statements.Remove();
                }
            });
        }

        public void Begin(string changeId, string release, string checksum, string dialect, string appliedBy, string description)
        {
            Mutate(document =>
            {
                var existing = Find(document, changeId);
                if (existing != null)
                {
                    existing.Remove();
                }

                document.Root.Add(new XElement(AppliedName,
                    new XAttribute("changeId", changeId ?? ""),
                    new XAttribute("release", release ?? ""),
                    new XAttribute("appliedAt", Now()),
                    new XAttribute("checksum", checksum ?? ""),
                    new XAttribute("dialect", dialect ?? ""),
                    new XAttribute("appliedBy", appliedBy ?? ""),
                    new XAttribute("description", description ?? ""),
                    new XAttribute("failureAt", ""),
                    new XAttribute("failureStmt", "")));
            });
        }

        public void MarkApplied(string changeId)
        {
            Mutate(document =>
            {
                var applied = Find(document, changeId);
                if (applied == null)
                {
                    return;
                }

                applied.SetAttributeValue("failureAt", "");
                applied.SetAttributeValue("failureStmt", "");
            });
        }

        public void MarkFailure(string changeId, int statementIndex)
        {
            Mutate(document =>
            {
                var applied = Find(document, changeId);
                if (applied == null)
                {
                    return;
                }

                applied.SetAttributeValue("failureAt", Now());
                applied.SetAttributeValue("failureStmt", statementIndex.ToString(CultureInfo.InvariantCulture));
            });
        }

        public void MarkAppliedManually(string changeId, string release, string checksum, string dialect, string appliedBy, string description)
        {
            Delete(changeId);
            Begin(changeId, release, checksum, dialect, appliedBy, description);
            MarkApplied(changeId);
        }

        public void Delete(string changeId)
        {
            Mutate(document =>
            {
                var applied = Find(document, changeId);
                if (applied != null)
                {
                    applied.Remove();
                }
            });
        }

        private XDocument LoadDocument()
        {
            if (!File.Exists(StatePath))
            {
                return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(RootName));
            }

            using (var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var document = XDocument.Load(stream);
                if (document.Root == null)
                {
                    return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(RootName));
                }
                return document;
            }
        }

        private void Mutate(Action<XDocument> action)
        {
            lock (_sync)
            {
                var document = LoadDocument();
                action(document);
                SaveDocument(document);
            }
        }

        private void SaveDocument(XDocument document)
        {
            Directory.CreateDirectory(_directory);

            var temp = StatePath + ".tmp";
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false)
            };

            using (var writer = XmlWriter.Create(temp, settings))
            {
                document.Save(writer);
            }

            if (File.Exists(StatePath))
            {
                File.Replace(temp, StatePath, null);
            }
            else
            {
                File.Move(temp, StatePath);
            }
        }

        private static XElement Find(XDocument document, string changeId)
        {
            if (document.Root == null)
            {
                return null;
            }

            return document.Root.Elements(AppliedName)
                .FirstOrDefault(x => string.Equals(Attr(x, "changeId"), changeId, StringComparison.OrdinalIgnoreCase));
        }

        private static string Attr(XElement element, string name)
        {
            var attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? "" : attribute.Value;
        }

        private static int? ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (int?)null;
        }

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
