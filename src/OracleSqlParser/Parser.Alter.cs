namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ALTER INDEX ----

    private Statement ParseAlterIndex(Token start)
    {
        var st = new AlterIndexStatement { Name = ParseDottedName() };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog)) st.Actions.Add(ParseAlterIndexAction());
        return Fin(st, start);
    }

    /// <summary>Words that begin an ALTER INDEX clause; they end the previous clause.</summary>
    private bool AtAlterIndexActionStart() => Cur.Type == TokenType.Identifier && Cur.Value is
        "REBUILD" or "RENAME" or "UNUSABLE" or "VISIBLE" or "INVISIBLE" or "MONITORING" or "NOMONITORING" or
        "COALESCE" or "SHRINK" or "DEALLOCATE" or "ENABLE" or "DISABLE";

    private AlterIndexAction ParseAlterIndexAction()
    {
        var s = Cur;
        var a = new AlterIndexAction();
        if (AcceptKw("REBUILD"))
        {
            a.Kind = AlterIndexActionKind.Rebuild;
            if (IsKw("PARTITION") || IsKw("SUBPARTITION"))
            {
                a.Subpartition = Next().Value == "SUBPARTITION";
                a.Partition = ParseIdentifier();
            }
            int prog = -1;
            while (!AtStatementEnd() && !AtAlterIndexActionStart() && Progress(ref prog))
            {
                if (AcceptKw("REVERSE")) a.Reverse = true;
                else if (AcceptKw("NOREVERSE")) a.Reverse = false;
                else if (AcceptKw("ONLINE")) a.Online = true;
                else a.Attributes.Add(ParsePhysicalAttribute(() => AtAlterIndexActionStart() || IsKw("ONLINE") || IsKw("REVERSE") || IsKw("NOREVERSE")));
            }
        }
        else if (AcceptKw("RENAME"))
        {
            ExpectKw("TO");
            a.Kind = AlterIndexActionKind.RenameTo;
            a.NewName = ParseIdentifier();
        }
        else if (AcceptKw("UNUSABLE"))
        {
            a.Kind = AlterIndexActionKind.Unusable;
            a.Online = AcceptKw("ONLINE");
        }
        else if (IsKw("VISIBLE") || IsKw("INVISIBLE"))
        {
            a.Kind = AlterIndexActionKind.Visibility;
            a.Visible = Next().Value == "VISIBLE";
        }
        else if (IsKw("MONITORING") || IsKw("NOMONITORING"))
        {
            a.Kind = AlterIndexActionKind.MonitoringUsage;
            a.Monitoring = Next().Value == "MONITORING";
            ExpectKw("USAGE");
        }
        else if (AcceptKw("COALESCE"))
        {
            a.Kind = AlterIndexActionKind.Coalesce;
            a.Cleanup = AcceptKw("CLEANUP");
            a.Only = AcceptKw("ONLY");
        }
        else if (IsKw("ENABLE") || IsKw("DISABLE"))
        {
            a.Kind = AlterIndexActionKind.EnableDisable;
            a.Enabled = Next().Value == "ENABLE";
        }
        else if (AcceptKw("SHRINK"))
        {
            ExpectKw("SPACE");
            a.Kind = AlterIndexActionKind.ShrinkSpace;
            a.Compact = AcceptKw("COMPACT");
            a.Cascade = AcceptKw("CASCADE");
        }
        else if (AcceptKw("DEALLOCATE"))
        {
            ExpectKw("UNUSED");
            a.Kind = AlterIndexActionKind.DeallocateUnused;
            if (AcceptKw("KEEP")) a.Keep = ParseAdditive();
        }
        else
        {
            a.Kind = AlterIndexActionKind.Attributes;
            a.Attributes = ParsePhysicalAttributes(AtAlterIndexActionStart);
        }
        return Fin(a, s);
    }
}
