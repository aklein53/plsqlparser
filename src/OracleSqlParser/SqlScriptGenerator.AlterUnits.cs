namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- ALTER of compiled units ----------

        private void AlterCompile(AlterCompileStatement s)
        {
            Add("ALTER ");
            KindKeyword(s.Kind);
            Sp(); Name(s.Name);
            if (s.Editionable != null) { Editionable(s.Editionable); return; }
            Add(" COMPILE");
            if (s.Debug) Add(" DEBUG");
            if (s.Part != null) Add(s.Part switch { CompilePart.Specification => " SPECIFICATION", CompilePart.Body => " BODY", _ => " PACKAGE" });
            if (s.ReuseSettings) Add(" REUSE SETTINGS");
            foreach (var p in s.Parameters)
            {
                Sp(); Name(p.Name); Add(" = "); Expr(p.Value);
            }
        }

        private void AlterTrigger(AlterTriggerStatement s)
        {
            Add("ALTER TRIGGER");
            Sp(); Name(s.Name);
            if (s.RenameTo != null) { Add(" RENAME TO "); Id(s.RenameTo); }
            else Add(s.Enable == true ? " ENABLE" : " DISABLE");
        }

        private void AlterView(AlterViewStatement s)
        {
            Add("ALTER VIEW");
            Sp(); Name(s.Name);
            foreach (var a in s.Actions) { Sp(); AlterAction(a); }
            if (s.ReadOnly != null) Add(s.ReadOnly == true ? " READ ONLY" : " READ WRITE");
        }
    }
}
