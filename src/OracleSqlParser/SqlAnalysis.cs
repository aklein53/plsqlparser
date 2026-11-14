namespace OracleSqlParser;

/// <summary>How a statement uses a table or view named in it.</summary>
public enum TableAccess
{
    /// <summary>Read: FROM, JOIN, subquery and MERGE USING sources, and DML FROM lists.</summary>
    Read,
    /// <summary>Target of INSERT (also each INTO clause of INSERT ALL / INSERT FIRST).</summary>
    Insert,
    /// <summary>Target of UPDATE.</summary>
    Update,
    /// <summary>Target of DELETE.</summary>
    Delete,
    /// <summary>Target of MERGE.</summary>
    Merge,
    /// <summary>
    /// Named by a DDL or utility statement: CREATE TABLE/VIEW/INDEX/TRIGGER ON, ALTER TABLE, DROP, TRUNCATE,
    /// COMMENT ON TABLE/COLUMN, LOCK TABLE, FLASHBACK TABLE, ANALYZE, PURGE, and the referenced table of a
    /// REFERENCES constraint. <see cref="TableUse.Node"/> is then the statement (or the constraint).
    /// </summary>
    Ddl,
}

/// <summary>A table, view or CTE name found by <see cref="SqlAnalysis.GetTables"/>.</summary>
public sealed class TableUse
{
    /// <summary>The <see cref="NamedTable"/> for a FROM, JOIN or DML reference; the DDL statement (or <see cref="TableConstraint"/>) for <see cref="TableAccess.Ddl"/>.</summary>
    public required SqlNode Node { get; init; }
    /// <summary>How the statement uses the name.</summary>
    public required TableAccess Access { get; init; }
    /// <summary>
    /// True when the name is a WITH (CTE) name in scope rather than a database object. Such references are reported
    /// here so they are not lost, but they are not tables: filter them out to get only database objects.
    /// </summary>
    public bool IsCteReference { get; init; }
    /// <summary>For a CTE reference, the WITH definition it refers to; otherwise null.</summary>
    public CommonTableExpression? Cte { get; init; }
    /// <summary>The name parts as written, e.g. [SCH, TAB] for SCH.TAB. Identifier positions are available on each part.</summary>
    public required IReadOnlyList<Identifier> NameParts { get; init; }
    /// <summary>The schema, when the name has two parts; null for an unqualified name.</summary>
    public string? Schema { get; init; }
    /// <summary>The object name (the last name part), normalized as <see cref="Identifier.Value"/>.</summary>
    public required string Name { get; init; }
    /// <summary>The database link after '@', dotted when it has several parts; null when there is no link.</summary>
    public string? DbLink { get; init; }
    /// <summary>The table alias, normalized as <see cref="Identifier.Value"/>; null when there is none.</summary>
    public string? Alias { get; init; }
    /// <summary>Start offset of the reported name (<see cref="NameParts"/>; excludes the database link and alias).</summary>
    public required int Start { get; init; }
    /// <summary>End offset of the reported name.</summary>
    public required int End { get; init; }
}

/// <summary>How a column reference is classified by <see cref="SqlAnalysis.GetColumns"/>.</summary>
public enum ColumnUseKind
{
    /// <summary>An ordinary column reference (qualified or not).</summary>
    Column,
    /// <summary>NEXTVAL or CURRVAL on a sequence, e.g. seq.NEXTVAL or sch.seq.CURRVAL. <see cref="ColumnUse.Qualifier"/> is the sequence name.</summary>
    SequenceValue,
    /// <summary>An unquoted, unqualified Oracle pseudo-column or parameterless SQL function (ROWNUM, ROWID, ORA_ROWSCN, SYSDATE, SYSTIMESTAMP, USER, UID).</summary>
    PseudoColumn,
    /// <summary>An unqualified name that is a SELECT-list alias, used in the ORDER BY of the same query. Not resolved to a column.</summary>
    SelectAlias,
}

/// <summary>Whether <see cref="ColumnUse.Source"/> was determined.</summary>
public enum ColumnResolution
{
    /// <summary>The reference was bound to exactly one FROM item (<see cref="ColumnUse.Source"/>).</summary>
    Resolved,
    /// <summary>An unqualified name with several FROM items in the innermost scope that has any, so the owner cannot be known.</summary>
    Ambiguous,
    /// <summary>No FROM item in scope has the qualifier (e.g. a PL/SQL record variable, a graph element variable, or a name outside the analysed node).</summary>
    Unresolved,
    /// <summary>Not a column of a FROM item (sequence values, pseudo-columns, select aliases).</summary>
    NotApplicable,
}

/// <summary>A column reference found by <see cref="SqlAnalysis.GetColumns"/>.</summary>
public sealed class ColumnUse
{
    /// <summary>The <see cref="ColumnReference"/>; or the <see cref="Identifier"/> of an INSERT or MERGE insert column list.</summary>
    public required SqlNode Node { get; init; }
    /// <summary>The name parts as written, e.g. [E, ID] for e.id.</summary>
    public required IReadOnlyList<Identifier> Parts { get; init; }
    /// <summary>The dotted qualifier before the name (e.g. "E", or "SCH.TAB"); null when unqualified.</summary>
    public string? Qualifier { get; init; }
    /// <summary>The last name part, normalized as <see cref="Identifier.Value"/>.</summary>
    public required string Name { get; init; }
    /// <summary>What kind of reference this is.</summary>
    public required ColumnUseKind Kind { get; init; }
    /// <summary>True for columns the statement writes: INSERT and MERGE insert column lists, and UPDATE and MERGE SET targets.</summary>
    public bool IsWriteTarget { get; init; }
    /// <summary>
    /// The FROM item the column belongs to when <see cref="Resolution"/> is <see cref="ColumnResolution.Resolved"/>:
    /// a <see cref="NamedTable"/> (a table, view or CTE), a <see cref="SubqueryTable"/>, or another row source such as
    /// JSON_TABLE. Null otherwise.
    /// </summary>
    public TableReference? Source { get; init; }
    /// <summary>The name the source is referred to by: its alias, or the dotted table name when it has no alias.</summary>
    public string? SourceName { get; init; }
    /// <summary>How the owner was determined; see <see cref="ColumnResolution"/>.</summary>
    public required ColumnResolution Resolution { get; init; }
    /// <summary>Start offset of the reference (its name parts).</summary>
    public required int Start { get; init; }
    /// <summary>End offset of the reference.</summary>
    public required int End { get; init; }
}

/// <summary>A bind variable found by <see cref="SqlAnalysis.GetBinds"/>.</summary>
public sealed class BindUse
{
    /// <summary>The bind variable node.</summary>
    public required BindVariable Node { get; init; }
    /// <summary>The name as written after the colon (no colon). For a quoted bind, the name without quotes. A numeric bind (:1) gives "1".</summary>
    public required string Name { get; init; }
    /// <summary>True when the name was written quoted (:"name").</summary>
    public bool Quoted { get; init; }
    /// <summary>Start offset of the bind, including the colon.</summary>
    public required int Start { get; init; }
    /// <summary>End offset of the bind.</summary>
    public required int End { get; init; }
}

/// <summary>What a call is, as far as the syntax shows.</summary>
public enum FunctionKind
{
    /// <summary>name(args) inside an expression. The parser cannot tell a function from a collection element or a built-in; see <see cref="SqlAnalysis.GetFunctions"/>.</summary>
    Function,
    /// <summary>A call written as a statement (PL/SQL procedure call, EXEC, CALL). Parameterless calls written without parentheses are included.</summary>
    Procedure,
    /// <summary>target.name(args) on an expression, e.g. the method of an object type.</summary>
    Method,
    /// <summary>NEW type(args) constructor call.</summary>
    Constructor,
}

/// <summary>A function, procedure or method call found by <see cref="SqlAnalysis.GetFunctions"/>.</summary>
public sealed class FunctionUse
{
    /// <summary>The <see cref="FunctionCall"/>, <see cref="MethodCall"/>, or the <see cref="ColumnReference"/> of a parameterless procedure call.</summary>
    public required SqlNode Node { get; init; }
    /// <summary>What kind of call this is.</summary>
    public required FunctionKind Kind { get; init; }
    /// <summary>The name parts as written: [FN] for fn(), [PKG, FN] for pkg.fn(), [SCH, PKG, FN] for sch.pkg.fn(). A method call has its name only.</summary>
    public required IReadOnlyList<Identifier> NameParts { get; init; }
    /// <summary>The dotted qualifier before the name (e.g. "SCH.PKG"); null when there is none (and for methods, whose target is an expression on <see cref="Node"/>).</summary>
    public string? Qualifier { get; init; }
    /// <summary>The last name part, normalized as <see cref="Identifier.Value"/>.</summary>
    public required string Name { get; init; }
    /// <summary>True when the call has an OVER clause (an analytic or window function).</summary>
    public bool IsAnalytic { get; init; }
    /// <summary>Start offset of the name parts.</summary>
    public required int Start { get; init; }
    /// <summary>End offset of the name parts.</summary>
    public required int End { get; init; }
}

/// <summary>
/// Discovery helpers over a parsed tree: the tables and views a statement touches, its column references, its bind
/// variables and its function calls. Each helper walks the tree with the typed visitor (<see cref="SqlNodeVisitor"/>)
/// and returns structured results that carry the node and its source span. Results are in source order.
/// </summary>
/// <remarks>
/// The analysis is syntactic. It knows nothing about the database: column ownership is inferred only from the FROM
/// clauses in the tree, never from the data dictionary. Where the tree does not determine an answer, the result says
/// so (<see cref="ColumnResolution.Ambiguous"/>, <see cref="ColumnResolution.Unresolved"/>) instead of guessing.
/// <para>
/// Not analysed: dynamic SQL (EXECUTE IMMEDIATE and OPEN ... FOR strings are literals, not parsed), the contents of
/// SELECT INTO / FETCH INTO / RETURNING INTO targets (they are PL/SQL variables, so they are not reported as columns),
/// and column references in DDL outside a query (CHECK and DEFAULT expressions). Names defined outside the analysed node
/// (a WITH clause or a table alias of an enclosing statement) are not known when the node is analysed alone.
/// </para>
/// </remarks>
public static class SqlAnalysis
{
    /// <summary>
    /// Every table, view and CTE name in <paramref name="node"/>, with how it is used. FROM, JOIN, subquery and MERGE USING
    /// sources are <see cref="TableAccess.Read"/>; DML targets are Insert, Update, Delete or Merge; DDL and utility statements
    /// name their object with <see cref="TableAccess.Ddl"/>. A WITH name in scope is reported with
    /// <see cref="TableUse.IsCteReference"/> set. Scoping: a WITH name is visible in the query of the WITH (including earlier and
    /// its own CTEs, so a recursive reference is a CTE reference), and an inner WITH shadows an outer one of the same name.
    /// A name used before its own definition in the same WITH (a forward reference, which Oracle rejects) is treated as a table.
    /// </summary>
    /// <param name="node">The tree to analyse (a script, a statement, a query or an expression).</param>
    public static IReadOnlyList<TableUse> GetTables(SqlNode node) => Analyze(node).TableUses();

    /// <summary>
    /// The column references in <paramref name="node"/>, each with the FROM item it belongs to when that is determined.
    /// <para>
    /// A reference is resolved to the innermost query block that has a matching FROM item: a qualifier must equal an alias
    /// (or, without an alias, the table name, or schema.table), and an unqualified name is bound when there is exactly one
    /// FROM item in that block (otherwise <see cref="ColumnResolution.Ambiguous"/>). A block with no FROM items passes the
    /// name to the enclosing block, so correlated subqueries resolve to the outer query. Owner determination never uses
    /// column lists, so an unqualified name in a block with one FROM item is bound to it even if that table lacks the column.
    /// </para>
    /// <para>
    /// Excluded: function names (see <see cref="GetFunctions"/>), bind variables, names with an attribute (cursor%ROWTYPE,
    /// SQL%ROWCOUNT), and the INTO targets of SELECT, FETCH and RETURNING. Reported as other kinds: sequence values
    /// (<see cref="ColumnUseKind.SequenceValue"/>, reported anywhere, since they are never PL/SQL variables), Oracle
    /// pseudo-columns (<see cref="ColumnUseKind.PseudoColumn"/>), and unqualified names that match a SELECT-list alias in the
    /// ORDER BY of the same query (<see cref="ColumnUseKind.SelectAlias"/>; they are not resolved to a column even when a
    /// column of that name exists, because Oracle's choice is not visible in the tree).
    /// </para>
    /// <para>
    /// Column references outside a query or DML statement (PL/SQL variables in assignments and conditions, CHECK and DEFAULT
    /// clauses) are not reported. If <paramref name="node"/> is an expression, its references are reported even outside a
    /// statement. Columns of JSON_TABLE, XMLTABLE, GRAPH_TABLE and MATCH_RECOGNIZE are not reported by name (their PATH and
    /// COLUMNS names are not column references); references inside a GRAPH_TABLE, or in a CTE's SEARCH and CYCLE clauses,
    /// are reported as <see cref="ColumnResolution.Unresolved"/>.
    /// </para>
    /// <para>
    /// Unqualified names in PL/SQL can also be variables. The tree does not know which names are declared variables, so a
    /// name in a SELECT whose query has exactly one FROM item is reported as a column of that item.
    /// </para>
    /// </summary>
    /// <param name="node">The tree to analyse.</param>
    public static IReadOnlyList<ColumnUse> GetColumns(SqlNode node) => Analyze(node).ColumnUses();

    /// <summary>
    /// The bind variables (:name, :1, :"quoted") in source order.
    /// </summary>
    /// <param name="node">The tree to analyse.</param>
    /// <param name="distinct">
    /// When true, only the first occurrence of each bind is returned. Unquoted names are compared case-insensitively, as Oracle
    /// does, and quoted names exactly. Numeric binds are distinct by number.
    /// </param>
    /// <remarks>
    /// The parser reads <c>:new.col</c> (as used in trigger bodies) as a bind named "new.col". It is reported as written.
    /// </remarks>
    public static IReadOnlyList<BindUse> GetBinds(SqlNode node, bool distinct = false)
    {
        var all = Analyze(node).BindUses();
        if (!distinct) return all;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return all.Where(b => seen.Add(b.Quoted ? b.Name : b.Name.ToUpperInvariant())).ToList();
    }

    /// <summary>
    /// The function, procedure and method calls in <paramref name="node"/>, with their qualified name parts.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>The parser has no catalogue of built-in functions, so built-in and user-defined calls are not
    /// distinguished. Compare <see cref="FunctionUse.Name"/> with your own list if you need that.</description></item>
    /// <item><description>Aggregates cannot be recognised by name for the same reason. <see cref="FunctionUse.IsAnalytic"/>
    /// reports the OVER clause, which is syntax, not a name.</description></item>
    /// <item><description>name(args) is reported as a call even when the parser cannot tell a function from a collection
    /// element or a record field in PL/SQL.</description></item>
    /// <item><description>Dedicated syntax nodes are not reported: CAST, EXTRACT, TRIM, the JSON and XML constructors, and
    /// SQL%BULK_ROWCOUNT(i) (an attribute, not a call).</description></item>
    /// <item><description>A procedure call statement (ProcedureCallStatement, CALL, EXEC) is reported as
    /// <see cref="FunctionKind.Procedure"/>, including a parameterless one written without parentheses.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="node">The tree to analyse.</param>
    public static IReadOnlyList<FunctionUse> GetFunctions(SqlNode node) => Analyze(node).FunctionUses();

    private static Collector Analyze(SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        // A bare expression has no enclosing statement, so its column references are reported as SQL references.
        var collector = new Collector(node is Expression ? 1 : 0);
        node.Accept(collector);
        return collector;
    }

    /// <summary>Walks a tree once, collecting all four kinds of result and tracking the query scopes needed to resolve columns.</summary>
    private sealed class Collector(int sqlDepth) : SqlNodeVisitor
    {
        private readonly List<TableUse> _tables = [];
        private readonly List<ColumnUse> _columns = [];
        private readonly List<BindUse> _binds = [];
        private readonly List<FunctionUse> _functions = [];

        private Scope? _scope;
        private CteScope? _ctes;
        private int _sqlDepth = sqlDepth;
        private int _opaqueDepth;
        private int _variableDepth;
        private HashSet<string>? _selectAliases;
        private SqlNode? _procedureCall;

        public IReadOnlyList<TableUse> TableUses() => _tables.OrderBy(t => t.Start).ThenBy(t => t.End).ToList();
        public IReadOnlyList<ColumnUse> ColumnUses() => _columns.OrderBy(c => c.Start).ThenBy(c => c.End).ToList();
        public IReadOnlyList<BindUse> BindUses() => _binds.OrderBy(b => b.Start).ThenBy(b => b.End).ToList();
        public IReadOnlyList<FunctionUse> FunctionUses() => _functions.OrderBy(f => f.Start).ThenBy(f => f.End).ToList();

        // ---- queries and scopes ----

        public override void ExplicitVisit(SelectStatement node)
        {
            _sqlDepth++;
            var outerCtes = _ctes;
            var outerAliases = _selectAliases;
            try
            {
                // Each CTE sees the CTEs before it and itself (a self reference is recursive); the body sees all of them.
                var visible = outerCtes;
                foreach (var cte in node.With)
                {
                    visible = new CteScope(cte, visible);
                    _ctes = visible;
                    cte.Query.Accept(this);
                    if (cte.Search is not null || cte.Cycle is not null)
                    {
                        _opaqueDepth++;
                        try
                        {
                            cte.Search?.Accept(this);
                            cte.Cycle?.Accept(this);
                        }
                        finally { _opaqueDepth--; }
                    }
                }
                _ctes = visible;
                _selectAliases = null;
                node.Body.Accept(this);

                if (node.OrderBy.Count > 0)
                {
                    // ORDER BY sees the columns of the query block, and the SELECT-list aliases. For a set operation the
                    // names are the result columns, which this analysis does not model, so they are left unresolved.
                    var block = LeftmostBlock(node.Body);
                    var orderScope = block is null
                        ? new Scope(Array.Empty<TableReference>(), _scope, stopsLookup: true)
                        : new Scope(FlattenAll(block.From), _scope);
                    _selectAliases = block is null
                        ? null
                        : block.SelectList.OfType<ExpressionSelectItem>()
                            .Where(i => i.Alias is not null)
                            .Select(i => i.Alias!.Value)
                            .ToHashSet();
                    InScope(orderScope, () =>
                    {
                        foreach (var item in node.OrderBy) item.Accept(this);
                    });
                    _selectAliases = null;
                }

                node.Offset?.Accept(this);
                node.Fetch?.Accept(this);
                node.ForUpdate?.Accept(this);
                foreach (var declaration in node.WithDeclarations) declaration.Accept(this);
            }
            finally
            {
                _ctes = outerCtes;
                _selectAliases = outerAliases;
                _sqlDepth--;
            }
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            // The FROM items of this block form the scope for everything else in it.
            var scope = new Scope(FlattenAll(node.From), _scope);
            InScope(scope, () =>
            {
                foreach (var source in node.From) source.Accept(this);
                foreach (var item in node.SelectList) item.Accept(this);
                node.Where?.Accept(this);
                node.StartWith?.Accept(this);
                node.ConnectBy?.Accept(this);
                foreach (var expression in node.GroupBy) expression.Accept(this);
                node.Having?.Accept(this);
                node.Model?.Accept(this);
            });
            WalkVariables(node.Into);
        }

        public override void ExplicitVisit(GraphTable node)
        {
            _opaqueDepth++;
            try { base.ExplicitVisit(node); }
            finally { _opaqueDepth--; }
        }

        public override void ExplicitVisit(PivotTable node) => WithSourceScope(node.Source, () => base.ExplicitVisit(node));

        public override void ExplicitVisit(UnpivotTable node) => WithSourceScope(node.Source, () => base.ExplicitVisit(node));

        public override void ExplicitVisit(MatchRecognizeTable node) => WithSourceScope(node.Source, () => base.ExplicitVisit(node));

        // ---- table references ----

        public override void ExplicitVisit(NamedTable node)
        {
            RecordTable(node, TableAccess.Read);
            base.ExplicitVisit(node);
        }

        // ---- DML ----

        public override void ExplicitVisit(InsertStatement node)
        {
            _sqlDepth++;
            try
            {
                WriteTarget(node.Target, TableAccess.Insert);
                var target = new Scope([node.Target], null);
                foreach (var column in node.Columns) AddColumn(column, [column], write: true, target);
                foreach (var row in node.ValueRows)
                    foreach (var value in row) value.Accept(this);
                node.RecordValue?.Accept(this);
                node.Query?.Accept(this);
                WalkReturning(node, target);
                node.ErrorLogging?.Accept(this);
            }
            finally { _sqlDepth--; }
        }

        public override void ExplicitVisit(UpdateStatement node)
        {
            _sqlDepth++;
            try
            {
                WriteTarget(node.Target, TableAccess.Update);
                var target = new Scope([node.Target], null);
                var sources = new List<TableReference> { node.Target };
                sources.AddRange(FlattenAll(node.From));
                InScope(new Scope(sources, _scope), () =>
                {
                    foreach (var source in node.From) source.Accept(this);
                    foreach (var set in node.Set)
                    {
                        foreach (var column in set.Columns) AddColumn(column, column.Parts, write: true, target);
                        set.Value.Accept(this);
                    }
                    node.Where?.Accept(this);
                });
                WalkReturning(node, target);
                node.ErrorLogging?.Accept(this);
            }
            finally { _sqlDepth--; }
        }

        public override void ExplicitVisit(DeleteStatement node)
        {
            _sqlDepth++;
            try
            {
                WriteTarget(node.Target, TableAccess.Delete);
                var target = new Scope([node.Target], null);
                var sources = new List<TableReference> { node.Target };
                sources.AddRange(FlattenAll(node.From));
                InScope(new Scope(sources, _scope), () =>
                {
                    foreach (var source in node.From) source.Accept(this);
                    node.Where?.Accept(this);
                });
                WalkReturning(node, target);
                node.ErrorLogging?.Accept(this);
            }
            finally { _sqlDepth--; }
        }

        public override void ExplicitVisit(MergeStatement node)
        {
            _sqlDepth++;
            try
            {
                WriteTarget(node.Target, TableAccess.Merge);
                node.Source.Accept(this);
                var target = new Scope([node.Target], null);
                var sources = new List<TableReference> { node.Target };
                sources.AddRange(FlattenAll([node.Source]));
                InScope(new Scope(sources, _scope), () =>
                {
                    node.On.Accept(this);
                    foreach (var clause in node.Clauses)
                    {
                        clause.AndCondition?.Accept(this);
                        foreach (var set in clause.Set)
                        {
                            foreach (var column in set.Columns) AddColumn(column, column.Parts, write: true, target);
                            set.Value.Accept(this);
                        }
                        clause.UpdateWhere?.Accept(this);
                        clause.DeleteWhere?.Accept(this);
                        foreach (var column in clause.InsertColumns) AddColumn(column, [column], write: true, target);
                        foreach (var value in clause.InsertValues) value.Accept(this);
                        clause.InsertWhere?.Accept(this);
                    }
                });
                WalkReturning(node, target);
                node.ErrorLogging?.Accept(this);
            }
            finally { _sqlDepth--; }
        }

        public override void ExplicitVisit(MultiTableInsertStatement node)
        {
            _sqlDepth++;
            try
            {
                node.Query.Accept(this);
                // The VALUES of INSERT ALL read the columns of the subquery, so they are resolved against its FROM items.
                var block = LeftmostBlock(node.Query.Body);
                var source = new Scope(block is null ? new List<TableReference>() : FlattenAll(block.From), _scope);
                InScope(source, () =>
                {
                    foreach (var branch in node.Branches)
                    {
                        branch.When?.Accept(this);
                        foreach (var into in branch.Targets) WalkInsertInto(into);
                    }
                    if (node.Else is not null)
                        foreach (var into in node.Else.Targets) WalkInsertInto(into);
                });
            }
            finally { _sqlDepth--; }
        }

        private void WalkInsertInto(InsertIntoClause into)
        {
            WriteTarget(into.Table, TableAccess.Insert);
            var target = new Scope([into.Table], null);
            foreach (var column in into.Columns) AddColumn(column, [column], write: true, target);
            foreach (var value in into.Values) value.Accept(this);
        }

        private void WalkReturning(DmlStatement node, Scope target)
        {
            // RETURNING expressions read the row of the DML target.
            InScope(target, () =>
            {
                foreach (var item in node.Returning) item.Accept(this);
            });
            WalkVariables(node.ReturningInto);
        }

        /// <summary>
        /// Visits PL/SQL variable targets (SELECT INTO, RETURNING INTO) for their binds only: the names are variables, so
        /// they are not reported as columns.
        /// </summary>
        private void WalkVariables(IEnumerable<Expression> targets)
        {
            _variableDepth++;
            try
            {
                foreach (var target in targets) target.Accept(this);
            }
            finally { _variableDepth--; }
        }

        private void WriteTarget(TableReference target, TableAccess access)
        {
            if (target is NamedTable named) RecordTable(named, access);
            else target.Accept(this);
        }

        // ---- DDL and utility statements that name a table or view ----

        public override void ExplicitVisit(CreateTableStatement node)
        {
            AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateViewStatement node)
        {
            AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateIndexStatement node)
        {
            AddDdl(node, node.Table);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateTriggerStatement node)
        {
            if (node.TargetKind is TriggerTargetKind.Table or TriggerTargetKind.View) AddDdl(node, node.Target);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterTableStatement node)
        {
            AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(TableConstraint node)
        {
            AddDdl(node, node.RefTable);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DropStatement node)
        {
            if (node.Kind is ObjectKind.Table or ObjectKind.View or ObjectKind.MaterializedView) AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(TruncateStatement node)
        {
            if (node.Target == TruncateTarget.Table) AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(PurgeStatement node)
        {
            if (node.Target == PurgeTarget.Table) AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CommentStatement node)
        {
            if (node.Target == CommentTarget.Table) AddDdl(node, node.Name);
            // COMMENT ON COLUMN t.c: the table is every part but the last.
            else if (node.Target == CommentTarget.Column && node.Name.Count > 1) AddDdl(node, node.Name.GetRange(0, node.Name.Count - 1));
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AnalyzeStatement node)
        {
            if (node.Target == AnalyzeTarget.Table) AddDdl(node, node.Name);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FlashbackTableStatement node)
        {
            foreach (var table in node.Tables) AddDdl(node, table);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(LockTableStatement node)
        {
            foreach (var table in node.Tables)
            {
                if (table is NamedTable named) RecordTable(named, TableAccess.Ddl);
                else table.Accept(this);
            }
        }

        // ---- columns ----

        public override void ExplicitVisit(ColumnReference node)
        {
            if (node.Attribute is null) AddColumn(node, node.Parts, write: false, _scope);
            base.ExplicitVisit(node);
        }

        // ---- binds ----

        public override void ExplicitVisit(BindVariable node)
        {
            _binds.Add(new BindUse
            {
                Node = node,
                Name = node.Name,
                Quoted = node.Quoted,
                Start = node.Start,
                End = node.End,
            });
        }

        // ---- calls ----

        public override void ExplicitVisit(FunctionCall node)
        {
            if (node.Attribute is null)
            {
                var kind = node.New ? FunctionKind.Constructor
                    : ReferenceEquals(node, _procedureCall) ? FunctionKind.Procedure
                    : FunctionKind.Function;
                AddFunction(node, kind, node.Name, node.Over is not null);
            }
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(MethodCall node)
        {
            AddFunction(node, FunctionKind.Method, [node.Name], analytic: false);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ProcedureCallStatement node)
        {
            NoteRoutine(node.Call);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CallStatement node)
        {
            NoteRoutine(node.Routine);
            base.ExplicitVisit(node);
        }

        /// <summary>A call statement's routine: a FunctionCall is a procedure call; a bare name is a parameterless one.</summary>
        private void NoteRoutine(Expression routine)
        {
            switch (routine)
            {
                case FunctionCall call:
                    _procedureCall = call;
                    break;
                case ColumnReference name when name.Attribute is null:
                    AddFunction(name, FunctionKind.Procedure, name.Parts, analytic: false);
                    break;
            }
        }

        // ---- helpers ----

        private void RecordTable(NamedTable node, TableAccess access)
        {
            var cte = node.DbLink.Count == 0 && node.Name.Count == 1 ? FindCte(node.Name[0].Value) : null;
            AddTable(node, node.Name, access, cte, node.DbLink.Count == 0 ? null : Dotted(node.DbLink), node.Alias?.Value);
        }

        private void AddDdl(SqlNode node, IReadOnlyList<Identifier> name)
        {
            if (name.Count > 0) AddTable(node, name, TableAccess.Ddl, cte: null, dbLink: null, alias: null);
        }

        private void AddTable(SqlNode node, IReadOnlyList<Identifier> name, TableAccess access, CommonTableExpression? cte, string? dbLink, string? alias)
        {
            var (start, end) = SpanOf(name, node);
            _tables.Add(new TableUse
            {
                Node = node,
                Access = access,
                IsCteReference = cte is not null,
                Cte = cte,
                NameParts = name,
                Schema = name.Count == 2 ? name[0].Value : null,
                Name = LastName(name),
                DbLink = dbLink,
                Alias = alias,
                Start = start,
                End = end,
            });
        }

        private CommonTableExpression? FindCte(string name)
        {
            for (var c = _ctes; c is not null; c = c.Next)
                if (c.Cte.Name.Value == name) return c.Cte;
            return null;
        }

        private void AddColumn(SqlNode node, IReadOnlyList<Identifier> parts, bool write, Scope? scope)
        {
            if (parts.Count == 0) return;
            // INTO targets are variables, not columns.
            if (!write && _variableDepth > 0) return;
            var name = parts[^1];
            var qualifier = parts.Count > 1 ? Dotted(parts.Take(parts.Count - 1).ToList()) : null;
            var (start, end) = SpanOf(parts, node);

            // Sequence values are reported wherever they appear: a name like seq.NEXTVAL is never a PL/SQL variable.
            if (!write && parts.Count > 1 && !name.Quoted && name.Value is ("NEXTVAL" or "CURRVAL"))
            {
                _columns.Add(Column(node, parts, qualifier, ColumnUseKind.SequenceValue, write, null, ColumnResolution.NotApplicable, start, end));
                return;
            }
            // Outside any statement, a name is a PL/SQL variable or expression, not a column.
            if (!write && _sqlDepth == 0) return;

            if (!write && parts.Count == 1 && !name.Quoted)
            {
                if (_selectAliases is not null && _selectAliases.Contains(name.Value))
                {
                    _columns.Add(Column(node, parts, null, ColumnUseKind.SelectAlias, write, null, ColumnResolution.NotApplicable, start, end));
                    return;
                }
                if (PseudoColumns.Contains(name.Value))
                {
                    _columns.Add(Column(node, parts, null, ColumnUseKind.PseudoColumn, write, null, ColumnResolution.NotApplicable, start, end));
                    return;
                }
            }

            if (_opaqueDepth > 0)
            {
                _columns.Add(Column(node, parts, qualifier, ColumnUseKind.Column, write, null, ColumnResolution.Unresolved, start, end));
                return;
            }
            var (resolution, source) = Resolve(parts, scope);
            _columns.Add(Column(node, parts, qualifier, ColumnUseKind.Column, write, source, resolution, start, end));
        }

        private static ColumnUse Column(SqlNode node, IReadOnlyList<Identifier> parts, string? qualifier, ColumnUseKind kind, bool write,
            TableReference? source, ColumnResolution resolution, int start, int end) => new()
        {
            Node = node,
            Parts = parts,
            Qualifier = qualifier,
            Name = LastName(parts),
            Kind = kind,
            IsWriteTarget = write,
            Source = source,
            SourceName = source is null ? null : SourceNameOf(source),
            Resolution = resolution,
            Start = start,
            End = end,
        };

        private void AddFunction(SqlNode node, FunctionKind kind, IReadOnlyList<Identifier> name, bool analytic)
        {
            if (name.Count == 0) return;
            var (start, end) = SpanOf(name, node);
            _functions.Add(new FunctionUse
            {
                Node = node,
                Kind = kind,
                NameParts = name,
                Qualifier = name.Count > 1 ? Dotted(name.Take(name.Count - 1).ToList()) : null,
                Name = LastName(name),
                IsAnalytic = analytic,
                Start = start,
                End = end,
            });
        }

        private void InScope(Scope scope, Action walk)
        {
            var saved = _scope;
            _scope = scope;
            try { walk(); }
            finally { _scope = saved; }
        }

        private void WithSourceScope(TableReference source, Action walk) => InScope(new Scope(FlattenAll([source]), _scope), walk);

        // ---- static helpers ----

        /// <summary>Oracle pseudo-columns and parameterless functions that can be written without a qualifier and are reserved in SQL, so they are never column names.</summary>
        private static readonly HashSet<string> PseudoColumns = ["ROWNUM", "ROWID", "ORA_ROWSCN", "SYSDATE", "SYSTIMESTAMP", "USER", "UID"];

        /// <summary>Resolves a column name to the FROM item that owns it, searching from the innermost scope outwards.</summary>
        private static (ColumnResolution, TableReference?) Resolve(IReadOnlyList<Identifier> parts, Scope? scope)
        {
            var qualifier = parts.Count > 1 ? parts.Take(parts.Count - 1).Select(p => p.Value).ToList() : null;
            for (var s = scope; s is not null; s = s.Parent)
            {
                if (s.StopsLookup) return (ColumnResolution.Unresolved, null);
                if (qualifier is null)
                {
                    if (s.Sources.Count == 0) continue;
                    return s.Sources.Count == 1 ? (ColumnResolution.Resolved, s.Sources[0]) : (ColumnResolution.Ambiguous, null);
                }
                var matches = s.Sources.Where(source => Matches(source, qualifier)).ToList();
                if (matches.Count == 1) return (ColumnResolution.Resolved, matches[0]);
                if (matches.Count > 1) return (ColumnResolution.Ambiguous, null);
            }
            return (ColumnResolution.Unresolved, null);
        }

        /// <summary>
        /// Oracle's rule: an aliased FROM item is known only by its alias; an unaliased table is known by its name, or by
        /// schema.name when the qualifier has two parts.
        /// </summary>
        private static bool Matches(TableReference source, List<string> qualifier)
        {
            if (source is NamedTable named && named.Alias is null)
            {
                if (qualifier.Count > named.Name.Count) return false;
                for (int i = 0; i < qualifier.Count; i++)
                    if (named.Name[named.Name.Count - qualifier.Count + i].Value != qualifier[i]) return false;
                return true;
            }
            return qualifier.Count == 1 && source.Alias is not null && source.Alias.Value == qualifier[0];
        }

        private static string? SourceNameOf(TableReference source) =>
            source.Alias?.Value ?? (source is NamedTable named ? Dotted(named.Name) : null);

        /// <summary>The FROM items of a block, with JOIN and parenthesized joins flattened. Pivot, match-recognize and other wrappers are single items.</summary>
        private static List<TableReference> FlattenAll(IEnumerable<TableReference> from)
        {
            var result = new List<TableReference>();
            foreach (var item in from) Flatten(item, result);
            return result;
        }

        private static void Flatten(TableReference item, List<TableReference> into)
        {
            switch (item)
            {
                case JoinTable join:
                    Flatten(join.Left, into);
                    Flatten(join.Right, into);
                    break;
                case ParenthesizedTable paren:
                    Flatten(paren.Inner, into);
                    break;
                default:
                    into.Add(item);
                    break;
            }
        }

        private static QuerySpecification? LeftmostBlock(QueryExpression query) => query switch
        {
            QuerySpecification block => block,
            SetOperation set => LeftmostBlock(set.Left),
            ParenthesizedQuery paren => LeftmostBlock(paren.Query.Body),
            _ => null,
        };

        private static string Dotted(IEnumerable<Identifier> parts) => string.Join(".", parts.Select(p => p.Value));

        private static string LastName(IReadOnlyList<Identifier> parts) => parts.Count == 0 ? "" : parts[^1].Value;

        private static (int Start, int End) SpanOf(IReadOnlyList<Identifier> parts, SqlNode fallback) =>
            parts.Count == 0 ? (fallback.Start, fallback.End) : (parts[0].Start, parts[^1].End);

        /// <summary>A FROM-item scope: the items a name can be bound to. <paramref name="stopsLookup"/> ends the search with no owner.</summary>
        private sealed class Scope(IReadOnlyList<TableReference> sources, Scope? parent, bool stopsLookup = false)
        {
            public IReadOnlyList<TableReference> Sources { get; } = sources;
            public Scope? Parent { get; } = parent;
            public bool StopsLookup { get; } = stopsLookup;
        }

        /// <summary>The WITH names in scope, innermost first.</summary>
        private sealed class CteScope(CommonTableExpression cte, CteScope? next)
        {
            public CommonTableExpression Cte { get; } = cte;
            public CteScope? Next { get; } = next;
        }
    }
}
