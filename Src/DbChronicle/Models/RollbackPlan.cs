using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DbChronicle.Models
{
    public sealed class RollbackPlan
    {
        public RollbackPlan(string targetRelease)
        {
            TargetRelease = targetRelease;
        }

        public string TargetRelease { get; private set; }

        public List<RollbackStep> Steps { get; } = new List<RollbackStep>();

        public bool HasBlockers
        {
            get { return Steps.Any(x => x.IsBlocked); }
        }

        public string ToSql()
        {
            var sb = new StringBuilder();
            foreach (var step in Steps)
            {
                sb.Append("-- Rollback: ").AppendLine(step.Unit.Id);
                foreach (var blocker in step.Blockers)
                {
                    sb.Append("-- BLOCKED: ").AppendLine(blocker);
                }
                foreach (var statement in step.Statements)
                {
                    sb.AppendLine(statement.Text);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}