namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ALTER MATERIALIZED VIEW ----

    /// <summary>Clause words that begin a modeled ALTER MATERIALIZED VIEW clause (also stop the physical-attribute fallback).</summary>
    private bool AtAlterMaterializedViewClauseStart() =>
        IsKw("REFRESH") || IsKw("NEVER") || IsKw("COMPILE") || IsKw("CONSIDER") || IsKw("SHRINK") ||
        ((IsKw("ENABLE") || IsKw("DISABLE")) && IsKwAt(1, "QUERY"));

    /// <summary>Typed ALTER MATERIALIZED VIEW; null when a clause is not modeled (the caller rewinds to the generic node).</summary>
    private Statement? ParseAlterMaterializedView(Token start, List<Identifier> name)
    {
        var st = new AlterMaterializedViewStatement { Name = name };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (IsKw("REFRESH") || (IsKw("NEVER") && IsKwAt(1, "REFRESH"))) st.Refresh = ParseRefresh();
            else if (AcceptKw("COMPILE")) st.Compile = true;
            else if (IsKw("CONSIDER") && IsKwAt(1, "FRESH")) { Next(); Next(); st.ConsiderFresh = true; }
            else if (IsKw("SHRINK") && IsKwAt(1, "SPACE"))
            {
                Next(); Next();
                st.ShrinkSpace = true;
                st.ShrinkCompact = AcceptKw("COMPACT");
                st.ShrinkCascade = AcceptKw("CASCADE");
            }
            else if ((IsKw("ENABLE") || IsKw("DISABLE")) && IsKwAt(1, "QUERY") && IsKwAt(2, "REWRITE"))
            {
                bool enable = Next().Value == "ENABLE";
                Next(); Next();
                st.QueryRewrite = enable;
            }
            else if (AtAlterMaterializedViewClauseStart()) return null;
            else
            {
                var pa = ParsePhysicalAttribute(AtAlterMaterializedViewClauseStart);
                if (pa is OtherAttribute) return null;
                st.Attributes.Add(pa);
            }
        }
        return Fin(st, start);
    }
}
