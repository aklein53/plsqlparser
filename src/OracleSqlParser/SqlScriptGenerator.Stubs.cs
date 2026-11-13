namespace OracleSqlParser;

// Temporary stubs, replaced as sections are implemented.
public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        private void Script(SqlScript sc) { bool f = true; foreach (var s in sc.Statements) { if (!f) Add("\n"); f = false; Statement(s); Add(";"); } }
        private void AdminLike(Statement st) => throw Unsupported(st);
        private void Ddl(Statement st) => throw Unsupported(st);
        private void Plsql(Statement st) => throw Unsupported(st);
        private void Declaration(Declaration d) => throw Unsupported(d);
        private void Syntax(SyntaxItem i) => throw Unsupported(i);
        private void SyntaxList(IEnumerable<SyntaxItem> i) { foreach (var x in i) throw Unsupported(x); }
        private string SyntaxText(IEnumerable<SyntaxItem> i) { foreach (var x in i) throw Unsupported(x); return ""; }
        private void Attribute(PhysicalAttribute a) => throw Unsupported(a);
        private void Handler(ExceptionHandler a) => throw Unsupported(a);
        private void ColumnDef(ColumnDefinition a) => throw Unsupported(a);
        private void Constraint(TableConstraint a) => throw Unsupported(a);
        private void Param(Parameter a) => throw Unsupported(a);
        private void ProgramOpts(ProgramOptions a) => throw Unsupported(a);
        private void ExternalCallClause(ExternalCall a) => throw Unsupported(a);
        private void AlterAction(AlterTableAction a) => throw Unsupported(a);
        private void IndexCol(IndexColumn a) => throw Unsupported(a);
        private void TriggerEv(TriggerEvent a) => throw Unsupported(a);
        private void SeqOption(SequenceOption a) => throw Unsupported(a);
        private void ConstraintStateText(ConstraintState a) => throw Unsupported(a);
    }
}
