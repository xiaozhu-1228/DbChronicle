using System.Collections.Generic;
using System.Linq;

namespace DbChronicle.Models
{
    public sealed class RollbackStep
    {
        public ChangeUnit Unit { get; set; }

        public List<SqlStatement> Statements { get; } = new List<SqlStatement>();

        public List<string> Blockers { get; } = new List<string>();

        public bool IsBlocked
        {
            get { return Blockers.Count > 0; }
        }
    }
}