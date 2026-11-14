using System.Text;

namespace OracleSqlParser;

/// <summary>
/// Prints an AST back to Oracle SQL / PL/SQL: the equivalent of ScriptDom's SqlScriptGenerator.
/// Every node the parser produces can be printed; the output parses back to an identical tree
/// (see the round-trip tests). Comments are not part of the AST and are not printed.
/// Parentheses are added from operator precedence, so trees edited programmatically print correctly.
/// A node the generator does not know throws <see cref="NotSupportedException"/> naming its type (never silently skipped).
/// An instance is immutable after construction and thread-safe; each call uses its own state.
/// </summary>
public sealed partial class SqlScriptGenerator
{
    private readonly SqlScriptGeneratorOptions _options;

    /// <summary>Creates a generator with default options (uppercase keywords, identifiers as typed, compact output).</summary>
    public SqlScriptGenerator() : this(new SqlScriptGeneratorOptions()) { }

    /// <summary>Creates a generator; the options are copied, later changes to <paramref name="options"/> have no effect.</summary>
    public SqlScriptGenerator(SqlScriptGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Clone();
    }

    /// <summary>Generates SQL for any node: a script, a statement, a query, an expression, a data type, an identifier ...</summary>
    public string Generate(SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var p = new Printer(_options);
        p.Node(node);
        return p.ToString();
    }

    /// <summary>Generates SQL for <paramref name="node"/> into <paramref name="writer"/>.</summary>
    public void Generate(SqlNode node, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Generate(node));
    }

    private sealed partial class Printer
    {
        private readonly StringBuilder _sb = new();
        private readonly SqlScriptGeneratorOptions _o;
        private int _indent;

        public Printer(SqlScriptGeneratorOptions options) => _o = options;

        public override string ToString() => _sb.ToString();

        private static NotSupportedException Unsupported(SqlNode n) => new($"Printing {n.GetType().Name} is not supported");

        // ---------- low-level output ----------

        /// <summary>Appends keyword / punctuation text (cased by <see cref="SqlScriptGeneratorOptions.KeywordCasing"/>).</summary>
        private Printer Add(string s)
        {
            _sb.Append(_o.KeywordCasing == KeywordCasing.Lowercase ? s.ToLowerInvariant() : s);
            return this;
        }

        /// <summary>Appends user text (identifier, literal, hint, ...) verbatim.</summary>
        private Printer Raw(string s) { _sb.Append(s); return this; }

        private void Sp() => _sb.Append(' ');

        private void NewLine()
        {
            while (_sb.Length > 0 && _sb[^1] == ' ') _sb.Length--;
            _sb.Append('\n').Append(' ', Math.Max(0, _indent * _o.IndentSize));
        }

        private void Indent(Action body) { _indent++; body(); _indent--; }

        /// <summary>Starts a clause: on a new line when <paramref name="newLine"/>, else after a space.</summary>
        private void ClauseStart(bool newLine)
        {
            if (newLine) NewLine(); else Sp();
        }

        /// <summary>Clause body after its keyword: on its own indented line when AlignClauseBodies, else after a space.</summary>
        private void ClauseBody(Action body)
        {
            if (_o.AlignClauseBodies) { _indent++; NewLine(); body(); _indent--; }
            else { Sp(); body(); }
        }

        private void Sep<T>(IEnumerable<T> items, Action<T> each, string sep = ", ")
        {
            bool first = true;
            foreach (var i in items) { if (!first) Add(sep); first = false; each(i); }
        }

        /// <summary>Items separated by ',' each on its own line at the current indentation.</summary>
        private void SepLines<T>(IEnumerable<T> items, Action<T> each)
        {
            bool first = true;
            foreach (var i in items) { if (!first) Add(","); first = false; NewLine(); each(i); }
        }

        private void Paren(Action body) { Add("("); body(); Add(")"); }

        // ---------- identifiers ----------

        private static string CaseText(string text, IdentifierCasing c) => c switch
        {
            IdentifierCasing.Uppercase => text.ToUpperInvariant(),
            IdentifierCasing.Lowercase => text.ToLowerInvariant(),
            _ => text,
        };

        private string Word(string value, bool quoted, string? text)
        {
            if (quoted) return "\"" + value.Replace("\"", "\"\"") + "\"";
            return _o.IdentifierCasing == IdentifierCasing.Preserve ? (text ?? value) : CaseText(value, _o.IdentifierCasing);
        }

        private void Id(Identifier i) => Raw(Word(i.Value, i.Quoted, i.Text));
        private void Name(IEnumerable<Identifier> parts) => Sep(parts, Id, ".");
        private void IdList(IEnumerable<Identifier> ids) => Sep(ids, Id);
        private void ParenIdList(IEnumerable<Identifier> ids) { Add("("); IdList(ids); Add(")"); }
        private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

        // ---------- dispatch ----------

        public void Node(SqlNode n)
        {
            switch (n)
            {
                case SqlScript sc: Script(sc); break;
                case Statement st: Statement(st); break;
                case QueryExpression q: Query(q); break;
                case TableReference t: Table(t); break;
                case Expression e: Expr(e); break;
                case SelectItem si: Item(si); break;
                case OrderByItem o: OrderItem(o); break;
                case Identifier id: Id(id); break;
                case DataType dt: Type(dt); break;
                case Declaration d: Declaration(d); break;
                case FunctionClause fc: Clause(fc); break;
                case SyntaxItem si2: Syntax(si2); break;
                case PhysicalAttribute pa: Attribute(pa); break;
                case HintClause h: Hint(h); break;
                case ExceptionHandler eh: Handler(eh); break;
                case ColumnDefinition cd: ColumnDef(cd); break;
                case TableConstraint tc: Constraint(tc); break;
                case Parameter pr: Param(pr); break;
                case ProgramOptions po: ProgramOpts(po); break;
                case ExternalCall ec: ExternalCallClause(ec); break;
                case CommonTableExpression cte: Cte(cte); break;
                case WindowSpec w: Window(w); break;
                case SetClause sc2: SetClauseText(sc2); break;
                case PatternNode pn: Pattern(pn); break;
                case Argument a: Arg(a); break;
                case AlterTableAction ata: AlterAction(ata); break;
                case IndexColumn ic: IndexCol(ic); break;
                case TriggerEvent te: TriggerEv(te); break;
                case DatafileSpec df: DatafileSpecText(df); break;
                case TablespaceAction ta: TablespaceActionText(ta); break;
                case ProfileLimit pl: ProfileLimitText(pl); break;
                case UserClause uc: UserClauseText(uc); break;
                case SequenceOption so: SeqOption(so); break;
                case ConstraintState cs: ConstraintStateText(cs); break;
                case ErrorLogging el: ErrorLoggingText(el); break;
                default: throw Unsupported(n);
            }
        }
    }
}
