namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- ALTER MATERIALIZED VIEW ----------

        private void AlterMaterializedView(AlterMaterializedViewStatement s)
        {
            Add("ALTER MATERIALIZED VIEW");
            Sp(); Name(s.Name);
            if (s.Refresh != null) { Sp(); Refresh(s.Refresh); }
            if (s.QueryRewrite != null) Add(s.QueryRewrite == true ? " ENABLE QUERY REWRITE" : " DISABLE QUERY REWRITE");
            if (s.Compile) Add(" COMPILE");
            if (s.ConsiderFresh) Add(" CONSIDER FRESH");
            if (s.ShrinkSpace)
            {
                Add(" SHRINK SPACE");
                if (s.ShrinkCompact) Add(" COMPACT");
                if (s.ShrinkCascade) Add(" CASCADE");
            }
            foreach (var a in s.Attributes) { Sp(); Attribute(a); }
        }
    }
}
