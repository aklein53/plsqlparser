namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ALTER MATERIALIZED VIEW ----

    /// <summary>Clause words that begin a modeled ALTER MATERIALIZED VIEW clause (also stop the physical-attribute fallback).</summary>
    private bool AtAlterMaterializedViewClauseStart() =>
        IsKw("REFRESH") || IsKw("NEVER") || IsKw("COMPILE") || IsKw("CONSIDER") || IsKw("SHRINK") ||
        ((IsKw("ENABLE") || IsKw("DISABLE")) && IsKwAt(1, "QUERY"));

    private Statement ParseAlterMaterializedView(Token start, List<Identifier> name)
    {
        var st = new AlterMaterializedViewStatement { Name = name };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (IsKw("REFRESH") || (IsKw("NEVER") && IsKwAt(1, "REFRESH"))) st.Refresh = ParseRefresh();
            else if (AcceptKw("COMPILE")) st.Compile = true;
            else if (AcceptKw("CONSIDER")) { ExpectKw("FRESH"); st.ConsiderFresh = true; }
            else if (AcceptKw("SHRINK"))
            {
                ExpectKw("SPACE");
                st.ShrinkSpace = true;
                st.ShrinkCompact = AcceptKw("COMPACT");
                st.ShrinkCascade = AcceptKw("CASCADE");
            }
            else if (IsKw("ENABLE") || IsKw("DISABLE"))
            {
                bool enable = Next().Value == "ENABLE";
                ExpectKw("QUERY"); ExpectKw("REWRITE");
                st.QueryRewrite = enable;
            }
            else st.Attributes.Add(ParsePhysicalAttribute(AtAlterMaterializedViewClauseStart));
        }
        return Fin(st, start);
    }
}
