namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ALTER TABLE partition maintenance ----

    /// <summary>Clause starts that end a partition-maintenance attribute list (UPDATE and ONLINE are its own clauses).</summary>
    private bool AtPartitionMaintenanceTail() => IsOp(",") || IsOp("(") || IsKw("UPDATE") || IsKw("ONLINE") || AtAlterActionStart();

    private bool AtPartitionMaintenanceStart()
    {
        if (Cur.Type != TokenType.Identifier) return false;
        bool partitionOrSub = IsKwAt(1, "PARTITION") || IsKwAt(1, "SUBPARTITION");
        return (IsKw("ADD") && IsKwAt(1, "PARTITION"))
            || ((IsKw("DROP") || IsKw("TRUNCATE") || IsKw("SPLIT") || IsKw("EXCHANGE") || IsKw("MOVE") || IsKw("RENAME")) && partitionOrSub)
            || (IsKw("MERGE") && IsKwAt(1, "PARTITIONS"))
            || (IsKw("MODIFY") && IsKwAt(1, "PARTITION") && (IsKwAt(3, "ADD") || IsKwAt(3, "DROP")) && IsKwAt(4, "VALUES"))
            || (IsKw("SET") && IsKwAt(1, "INTERVAL"));
    }

    /// <summary>
    /// Parses one partition-maintenance action; the caller has checked <see cref="AtPartitionMaintenanceStart"/>.
    /// Other forms (for example MODIFY PARTITION with other clauses) are not reached here and stay in the token tree.
    /// </summary>
    private AlterTableAction ParsePartitionMaintenance()
    {
        var s = Cur;
        var a = new AlterTableAction();
        var verb = Next().Value;
        switch (verb)
        {
            case "ADD":
                a.Kind = AlterTableActionKind.AddPartition;
                a.Partitions.Add(ParsePartitionDefinition(AtPartitionMaintenanceTail));
                break;
            case "DROP":
            case "TRUNCATE":
                a.Kind = verb == "DROP" ? AlterTableActionKind.DropPartition : AlterTableActionKind.TruncatePartition;
                a.PartitionRefs = ParsePartitionTargets();
                if (verb == "TRUNCATE") ParseTruncateStorage(a);
                ParseUpdateIndexesClause(a);
                break;
            case "SPLIT":
                a.Kind = AlterTableActionKind.SplitPartition;
                a.PartitionRefs = ParsePartitionTargets();
                // the multi-way split (INTO only) has no AT/VALUES boundary
                bool boundary = false;
                if (AcceptKw("VALUES")) { a.SplitByValues = true; boundary = true; }
                else if (AcceptKw("AT")) boundary = true;
                if (boundary)
                {
                    ExpectOp("(");
                    do a.Values.Add(ParseExpr()); while (AcceptOp(","));
                    ExpectOp(")");
                }
                if (AcceptKw("INTO"))
                {
                    ExpectOp("(");
                    do a.Partitions.Add(ParsePartitionDefinition(AtPartitionMaintenanceTail)); while (AcceptOp(","));
                    ExpectOp(")");
                }
                ParseUpdateIndexesClause(a);
                break;
            case "MERGE":
                a.Kind = AlterTableActionKind.MergePartitions;
                a.PartitionRefs = ParsePartitionTargets(plural: true);
                if (AcceptKw("INTO")) a.Partitions.Add(ParsePartitionDefinition(AtPartitionMaintenanceTail));
                ParseUpdateIndexesClause(a);
                break;
            case "EXCHANGE":
                a.Kind = AlterTableActionKind.ExchangePartition;
                a.PartitionRefs = ParsePartitionTargets();
                ExpectKw("WITH"); ExpectKw("TABLE");
                a.Names = ParseDottedName();
                if (AcceptKw("INCLUDING")) { ExpectKw("INDEXES"); a.IncludingIndexes = true; }
                else if (AcceptKw("EXCLUDING")) { ExpectKw("INDEXES"); a.IncludingIndexes = false; }
                if (AcceptKw("WITHOUT")) { ExpectKw("VALIDATION"); a.WithValidation = false; }
                else if (IsKw("WITH") && IsKwAt(1, "VALIDATION")) { Next(); Next(); a.WithValidation = true; }
                ParseUpdateIndexesClause(a);
                break;
            case "MOVE":
            {
                a.Kind = AlterTableActionKind.MovePartition;
                a.PartitionRefs = ParsePartitionTargets();
                int prog = -1;
                while (!AtStatementEnd() && !AtAlterActionStart() && Progress(ref prog))
                {
                    if (AcceptKw("ONLINE")) a.Online = true;
                    else if (IsKw("UPDATE")) ParseUpdateIndexesClause(a);
                    else a.Attributes.Add(ParsePhysicalAttribute(AtPartitionMaintenanceTail));
                }
                break;
            }
            case "RENAME":
                a.Kind = AlterTableActionKind.RenamePartition;
                a.PartitionRefs = ParsePartitionTargets();
                ExpectKw("TO");
                a.NewName = ParseIdentifier();
                break;
            case "MODIFY":
                a.Kind = AlterTableActionKind.ModifyPartitionValues;
                a.PartitionRefs = ParsePartitionTargets();
                if (AcceptKw("ADD")) a.AddValues = true;
                else { ExpectKw("DROP"); a.AddValues = false; }
                ExpectKw("VALUES");
                ExpectOp("(");
                do a.Values.Add(ParseExpr()); while (AcceptOp(","));
                ExpectOp(")");
                break;
            case "SET":
                a.Kind = AlterTableActionKind.SetInterval;
                ExpectKw("INTERVAL");
                ExpectOp("(");
                if (!IsOp(")")) a.Interval = ParseExpr();
                ExpectOp(")");
                break;
        }
        return Fin(a, s);
    }

    /// <summary>
    /// PARTITION|SUBPARTITION name|FOR (value, ...) [, ...] (or PARTITIONS name, ... for MERGE). The keyword is consumed here.
    /// </summary>
    private List<PartitionReference> ParsePartitionTargets(bool plural = false)
    {
        bool sub = !plural && AcceptKw("SUBPARTITION");
        if (plural) ExpectKw("PARTITIONS");
        else if (!sub) ExpectKw("PARTITION");
        var list = new List<PartitionReference>();
        do
        {
            var s = Cur;
            var r = new PartitionReference { Subpartition = sub };
            if (AcceptKw("FOR"))
            {
                ExpectOp("(");
                do r.ForValues.Add(ParseExpr()); while (AcceptOp(","));
                ExpectOp(")");
            }
            else r.Name = ParseIdentifier();
            list.Add(Fin(r, s));
        } while (AcceptOp(","));
        return list;
    }

    /// <summary>Optional DROP STORAGE | REUSE STORAGE of TRUNCATE PARTITION.</summary>
    private void ParseTruncateStorage(AlterTableAction a)
    {
        if (AcceptKw("REUSE")) { ExpectKw("STORAGE"); a.Storage = TruncateStorage.ReuseStorage; }
        else if (IsKw("DROP") && IsKwAt(1, "STORAGE")) { Next(); Next(); a.Storage = TruncateStorage.DropStorage; }
    }

    /// <summary>Optional UPDATE [GLOBAL] INDEXES clause.</summary>
    private void ParseUpdateIndexesClause(AlterTableAction a)
    {
        if (!AcceptKw("UPDATE")) return;
        a.GlobalIndexes = AcceptKw("GLOBAL");
        ExpectKw("INDEXES");
        a.UpdateIndexes = true;
    }
}
