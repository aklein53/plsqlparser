namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ALTER INDEX ----

    /// <summary>
    /// Typed ALTER for the object kinds that have one, starting after the object kind. Returns null, with the cursor
    /// unchanged, when a clause is not modeled, so the caller parses the statement as a generic AlterObjectStatement.
    /// </summary>
    private Statement? TryParseTypedAlter(Token start, ObjectKind kind)
    {
        int save = _p, versionMark = _versionErrors.Count;
        Statement? typed = kind switch
        {
            ObjectKind.Index => ParseAlterIndex(start),
            ObjectKind.Sequence => ParseAlterSequence(start),
            ObjectKind.MaterializedView => ParseAlterMaterializedViewIfModeled(start),
            _ when IsAlterUnitKind(kind) => ParseAlterUnitIfModeled(start, kind),
            _ => null,
        };
        if (typed == null)
        {
            _p = save;
            _versionErrors.RemoveRange(versionMark, _versionErrors.Count - versionMark);
        }
        return typed;
    }

    private bool AtIdentifierName() => Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier;

    private Statement? ParseAlterUnitIfModeled(Token start, ObjectKind kind) => AtIdentifierName() ? ParseAlterUnit(start, kind, ParseDottedName()) : null;

    private Statement? ParseAlterMaterializedViewIfModeled(Token start)
    {
        if (!AtIdentifierName()) return null;
        var name = ParseDottedName();
        return AtAlterMaterializedViewClauseStart() || IsKw("LOGGING") || IsKw("NOLOGGING") || IsKw("CACHE") || IsKw("NOCACHE") || IsKw("PARALLEL") || IsKw("NOPARALLEL")
            ? ParseAlterMaterializedView(start, name) : null;
    }

    private Statement? ParseAlterIndex(Token start)
    {
        var st = new AlterIndexStatement { Name = ParseDottedName() };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (!AtModeledAlterIndexActionStart()) return null;
            st.Actions.Add(ParseAlterIndexAction());
        }
        if (st.Actions.Any(a => a.Attributes.Any(x => x is OtherAttribute))) return null;
        return Fin(st, start);
    }

    /// <summary>Clause starts the typed ALTER INDEX model; anything else (RENAME PARTITION, MODIFY PARTITION, ...) is generic.</summary>
    private bool AtModeledAlterIndexActionStart() =>
        IsKw("REBUILD") || (IsKw("RENAME") && IsKwAt(1, "TO")) || IsKw("UNUSABLE") || IsKw("VISIBLE") || IsKw("INVISIBLE") ||
        ((IsKw("MONITORING") || IsKw("NOMONITORING")) && IsKwAt(1, "USAGE")) || IsKw("COALESCE") || IsKw("ENABLE") || IsKw("DISABLE") ||
        (IsKw("SHRINK") && IsKwAt(1, "SPACE")) || (IsKw("DEALLOCATE") && IsKwAt(1, "UNUSED")) ||
        (Cur.Type == TokenType.Identifier && Cur.Value is "TABLESPACE" or "PCTFREE" or "PCTUSED" or "INITRANS" or "MAXTRANS" or
            "LOGGING" or "NOLOGGING" or "PARALLEL" or "NOPARALLEL" or "COMPRESS" or "NOCOMPRESS" or "CACHE" or "NOCACHE");

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

public sealed partial class SqlParser
{
    // ---- ALTER SEQUENCE ----

    private Statement ParseAlterSequence(Token start)
    {
        var st = new AlterSequenceStatement { Name = ParseDottedName() };
        st.Options = ParseSequenceOptions(() => false);
        return Fin(st, start);
    }
}
