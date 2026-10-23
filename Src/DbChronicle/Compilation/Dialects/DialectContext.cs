using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Compilation.Dialects;

public sealed class DialectContext
{
	private readonly List<CompileDiagnostic> _diagnostics = new List<CompileDiagnostic>();

	public string ChangeId { get; set; }

	public bool IncrementalModify { get; set; } = true;


	public IReadOnlyList<CompileDiagnostic> Diagnostics => _diagnostics;

	public void Begin(string changeId)
	{
		ChangeId = changeId;
		_diagnostics.Clear();
	}

	public void Warn(string code, string table, string column, string message)
	{
		_diagnostics.Add(new CompileDiagnostic
		{
			Code = code,
			Severity = DiagnosticSeverity.Warning,
			ChangeId = ChangeId,
			Table = table,
			Column = column,
			Message = message
		});
	}
}
