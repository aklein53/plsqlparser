namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- ALTER TABLE partition maintenance ----------

        /// <summary>Prints a partition-maintenance action; returns false for kinds that are not partition maintenance.</summary>
        private bool AlterPartitionAction(AlterTableAction a)
        {
            switch (a.Kind)
            {
                case AlterTableActionKind.AddPartition:
                    Add("ADD ");
                    PartitionDef(a.Partitions[0], false);
                    break;
                case AlterTableActionKind.DropPartition:
                    Add("DROP "); PartitionTargets(a, false); PartitionTail(a);
                    break;
                case AlterTableActionKind.TruncatePartition:
                    Add("TRUNCATE "); PartitionTargets(a, false); PartitionTail(a);
                    break;
                case AlterTableActionKind.SplitPartition:
                    Add("SPLIT "); PartitionTargets(a, false);
                    Add(a.SplitByValues ? " VALUES (" : " AT (");
                    Sep(a.Values, e => Expr(e));
                    Add(")");
                    if (a.Partitions.Count > 0) { Add(" INTO ("); Sep(a.Partitions, p => PartitionDef(p, false)); Add(")"); }
                    PartitionTail(a);
                    break;
                case AlterTableActionKind.MergePartitions:
                    Add("MERGE "); PartitionTargets(a, true);
                    if (a.Partitions.Count > 0) { Add(" INTO "); PartitionDef(a.Partitions[0], false); }
                    PartitionTail(a);
                    break;
                case AlterTableActionKind.ExchangePartition:
                    Add("EXCHANGE "); PartitionTargets(a, false);
                    Add(" WITH TABLE "); Name(a.Names);
                    if (a.IncludingIndexes != null) Add(a.IncludingIndexes == true ? " INCLUDING INDEXES" : " EXCLUDING INDEXES");
                    if (a.WithValidation != null) Add(a.WithValidation == true ? " WITH VALIDATION" : " WITHOUT VALIDATION");
                    PartitionTail(a);
                    break;
                case AlterTableActionKind.MovePartition:
                    Add("MOVE "); PartitionTargets(a, false);
                    foreach (var p in a.Attributes) { Sp(); Attribute(p); }
                    if (a.Online) Add(" ONLINE");
                    PartitionTail(a);
                    break;
                case AlterTableActionKind.RenamePartition:
                    Add("RENAME "); PartitionTargets(a, false); Add(" TO "); Id(a.NewName!);
                    break;
                case AlterTableActionKind.ModifyPartitionValues:
                    Add("MODIFY "); PartitionTargets(a, false);
                    Add(a.AddValues == true ? " ADD VALUES (" : " DROP VALUES (");
                    Sep(a.Values, e => Expr(e));
                    Add(")");
                    break;
                case AlterTableActionKind.SetInterval:
                    Add("SET INTERVAL (");
                    if (a.Interval != null) Expr(a.Interval);
                    Add(")");
                    break;
                default:
                    return false;
            }
            return true;
        }

        /// <summary>PARTITION|SUBPARTITION (or PARTITIONS for MERGE) followed by names or FOR (values), comma separated.</summary>
        private void PartitionTargets(AlterTableAction a, bool plural)
        {
            bool sub = a.PartitionRefs.Count > 0 && a.PartitionRefs[0].Subpartition;
            Add(plural ? "PARTITIONS " : sub ? "SUBPARTITION " : "PARTITION ");
            Sep(a.PartitionRefs, r =>
            {
                if (r.ForValues.Count > 0) { Add("FOR ("); Sep(r.ForValues, e => Expr(e)); Add(")"); }
                else Id(r.Name!);
            });
        }

        private void PartitionTail(AlterTableAction a)
        {
            if (!a.UpdateIndexes) return;
            Add(a.GlobalIndexes ? " UPDATE GLOBAL INDEXES" : " UPDATE INDEXES");
        }
    }
}
