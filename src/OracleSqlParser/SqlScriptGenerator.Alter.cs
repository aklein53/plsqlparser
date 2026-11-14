namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- ALTER SEQUENCE ----------

        private void AlterSequence(AlterSequenceStatement s)
        {
            Add("ALTER SEQUENCE");
            Sp(); Name(s.Name);
            foreach (var o in s.Options) { Sp(); SeqOption(o); }
        }

        // ---------- ALTER INDEX ----------

        private void AlterIndex(AlterIndexStatement s)
        {
            Add("ALTER INDEX");
            Sp(); Name(s.Name);
            foreach (var a in s.Actions) { Sp(); AlterIndexActionText(a); }
        }

        private void AlterIndexActionText(AlterIndexAction a)
        {
            switch (a.Kind)
            {
                case AlterIndexActionKind.Rebuild:
                    Add("REBUILD");
                    if (a.Partition != null) { Add(a.Subpartition ? " SUBPARTITION " : " PARTITION "); Id(a.Partition); }
                    if (a.Reverse != null) Add(a.Reverse == true ? " REVERSE" : " NOREVERSE");
                    if (a.Online) Add(" ONLINE");
                    foreach (var p in a.Attributes) { Sp(); Attribute(p); }
                    break;
                case AlterIndexActionKind.RenameTo: Add("RENAME TO "); Id(a.NewName!); break;
                case AlterIndexActionKind.Unusable: Add("UNUSABLE"); if (a.Online) Add(" ONLINE"); break;
                case AlterIndexActionKind.Visibility: Add(a.Visible == true ? "VISIBLE" : "INVISIBLE"); break;
                case AlterIndexActionKind.MonitoringUsage: Add(a.Monitoring == true ? "MONITORING USAGE" : "NOMONITORING USAGE"); break;
                case AlterIndexActionKind.Coalesce:
                    Add("COALESCE");
                    if (a.Cleanup) Add(" CLEANUP");
                    if (a.Only) Add(" ONLY");
                    break;
                case AlterIndexActionKind.EnableDisable: Add(a.Enabled == true ? "ENABLE" : "DISABLE"); break;
                case AlterIndexActionKind.ShrinkSpace:
                    Add("SHRINK SPACE");
                    if (a.Compact) Add(" COMPACT");
                    if (a.Cascade) Add(" CASCADE");
                    break;
                case AlterIndexActionKind.DeallocateUnused:
                    Add("DEALLOCATE UNUSED");
                    if (a.Keep != null) { Add(" KEEP "); Expr(a.Keep); }
                    break;
                default:
                    Sep(a.Attributes, Attribute, " ");
                    break;
            }
        }
    }
}
