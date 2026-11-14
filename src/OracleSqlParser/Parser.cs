namespace OracleSqlParser;

public sealed class ParseResult
{
    /// <summary>
    /// The exact text that was parsed. Node <see cref="SqlNode.Start"/>/<see cref="SqlNode.End"/> offsets and
    /// <see cref="Token.Start"/>/<see cref="Token.End"/> index into this string. Empty for a result not produced by <see cref="SqlParser.Parse(string)"/>.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    public SqlScript Script { get; init; } = new();
    public IReadOnlyList<ParseError> Errors { get; init; } = [];

    /// <summary>
    /// Every token the lexer produced, in source order, including optimizer hints that the parser does not use and
    /// the final EOF token. Each token's <see cref="Token.LeadingTrivia"/> holds the whitespace and comments before it.
    /// Node <c>FirstTokenIndex</c>/<c>LastTokenIndex</c> are indexes into this list.
    /// </summary>
    public IReadOnlyList<Token> Tokens { get; init; } = [];

    public bool Success => Errors.Count == 0;
}

/// <summary>Recursive-descent parser for Oracle SQL (queries and DML) producing an AST.</summary>
public sealed partial class SqlParser
{
    // ---- public API ----

    /// <summary>
    /// Parses a script of one or more statements separated by ';' (or '/'). Never throws; see Errors.
    /// Parsing is thread-safe (each call has its own state); the returned tree is mutable and not thread-safe.
    /// </summary>
    public static ParseResult Parse(string sql) => Parse(sql, null);

    /// <summary>Parses a script with explicit <see cref="ParseOptions"/>. Never throws; see Errors.</summary>
    public static ParseResult Parse(string sql, ParseOptions? options)
    {
        var errors = new List<ParseError>();
        var tokens = TokenizeLenient(sql, errors, options?.SubstitutionEnabled ?? true);
        var parser = new SqlParser(tokens, sql, options);
        parser._lexErrors = errors.ToList();
        var script = parser.ParseScript(errors);
        return new ParseResult { Source = sql, Script = script, Errors = errors, Tokens = tokens };
    }

    /// <summary>Parses a single expression; throws ParseException on error.</summary>
    public static Expression ParseExpression(string sql) => ParseExpression(sql, null);

    /// <summary>Parses a single expression with explicit options; throws ParseException on error.</summary>
    public static Expression ParseExpression(string sql, ParseOptions? options)
    {
        var p = new SqlParser(Lexer.Tokenize(sql, options?.SubstitutionEnabled ?? true), sql, options);
        var e = p.ParseExpr();
        if (p.Cur.Type != TokenType.EOF) throw p.Fail($"Unexpected '{p.Cur.Text}' after expression");
        return e;
    }

    /// <summary>Parses a single statement; throws ParseException on error.</summary>
    public static Statement ParseStatement(string sql) => ParseStatement(sql, null);

    /// <summary>Parses a single statement with explicit options; throws ParseException on error.</summary>
    public static Statement ParseStatement(string sql, ParseOptions? options)
    {
        var p = new SqlParser(Lexer.Tokenize(sql, options?.SubstitutionEnabled ?? true), sql, options);
        var s = p.ParseStatementCore();
        while (p.AcceptOp(";") || p.AcceptOp("/")) { }
        if (p.Cur.Type != TokenType.EOF) throw p.Fail($"Unexpected '{p.Cur.Text}' after statement");
        return s;
    }

    private static List<Token> TokenizeLenient(string sql, List<ParseError> errors, bool substitutionEnabled)
    {
        var lx = new Lexer(sql, substitutionEnabled);
        var list = new List<Token>();
        while (true)
        {
            var t = lx.Next() with { TokenIndex = list.Count };
            list.Add(t);
            if (t.Type == TokenType.EOF) break;
        }
        errors.AddRange(lx.Errors);
        return list;
    }

    // ---- token plumbing ----

    private readonly List<Token> _t;
    private readonly string _src;
    private int _p;

    private readonly int _maxDepth;
    private int _depth;
    private List<ParseError> _lexErrors = [];

    private SqlParser(List<Token> tokens, string src, ParseOptions? options = null)
    {
        _src = src;
        _maxDepth = options?.MaxNestingDepth ?? 200;
        // Keep optimizer hints only where they are meaningful (right after a statement keyword).
        _t = [];
        foreach (var tok in tokens)
        {
            if (tok.Type == TokenType.Hint &&
                !(_t.Count > 0 && _t[^1].Type == TokenType.Identifier &&
                  _t[^1].Value is "SELECT" or "INSERT" or "UPDATE" or "DELETE" or "MERGE"))
                continue;
            _t.Add(tok);
        }
    }

    /// <summary>Enters one nesting level; throws when the configured maximum depth is exceeded. Pair with <see cref="Leave"/>.</summary>
    private void Enter()
    {
        if (++_depth > _maxDepth) throw Fail($"nesting depth exceeds {_maxDepth}");
        if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Fail($"nesting depth exceeds {_depth - 1} (insufficient stack space)");
    }

    private void Leave() => _depth--;

    /// <summary>
    /// Shared loop guard: call at the top of every iteration of an open-ended loop. Throws if the previous
    /// iteration did not advance the token position (which would otherwise spin forever).
    /// </summary>
    private bool Progress(ref int mark)
    {
        if (_p == mark) throw Fail($"parser made no progress at {Describe(Cur)}");
        mark = _p;
        return true;
    }

    private Token Cur => _t[_p];
    private Token Prev => _t[Math.Max(_p - 1, 0)];
    private Token PeekTok(int n = 1) => _t[Math.Min(_p + n, _t.Count - 1)];
    private Token Next()
    {
        // Every recursion consumes tokens, so this is a universal last line of defence against stack exhaustion.
        if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Fail($"nesting depth exceeds {_depth} (insufficient stack space)");
        var t = Cur;
        if (_p < _t.Count - 1) _p++;
        return t;
    }

    private bool IsKw(string kw) => Cur.Type == TokenType.Identifier && Cur.Value == kw;
    private bool IsKwAt(int n, string kw) => PeekTok(n) is { Type: TokenType.Identifier } t && t.Value == kw;
    private bool IsOp(string op) => Cur.Type == TokenType.Operator && Cur.Text == op;
    private bool IsOpAt(int n, string op) => PeekTok(n) is { Type: TokenType.Operator } t && t.Text == op;
    private bool AcceptKw(string kw) { if (IsKw(kw)) { Next(); return true; } return false; }
    private bool AcceptOp(string op) { if (IsOp(op)) { Next(); return true; } return false; }

    private void ExpectKw(string kw) { if (!AcceptKw(kw)) throw Fail($"Expected {kw} but found {Describe(Cur)}"); }
    private void ExpectOp(string op) { if (!AcceptOp(op)) throw Fail($"Expected '{op}' but found {Describe(Cur)}"); }

    private static string Describe(Token t) => t.Type == TokenType.EOF ? "end of input" : $"'{t.Text}'";

    private ParseException Fail(string msg) => new(new ParseError(msg, Cur.Start, Cur.Line, Cur.Column));

    private T Fin<T>(T node, Token start) where T : SqlNode
    {
        node.Start = start.Start;
        node.End = Prev.End;
        node.Line = start.Line;
        node.Column = start.Column;
        node.FirstTokenIndex = start.TokenIndex;
        node.LastTokenIndex = Prev.TokenIndex;
        return node;
    }

    // ---- keyword sets ----

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "ACCESS","ADD","ALL","ALTER","AND","ANY","AS","ASC","AUDIT","BETWEEN","BY","CHAR","CHECK","CLUSTER","COLUMN",
        "COLUMN_VALUE","COMMENT","COMPRESS","CONNECT","CREATE","CURRENT","DATE","DECIMAL","DEFAULT","DELETE","DESC",
        "DISTINCT","DROP","ELSE","EXCLUSIVE","EXISTS","FILE","FLOAT","FOR","FROM","GRANT","GROUP","HAVING","IDENTIFIED",
        "IMMEDIATE","IN","INCREMENT","INDEX","INITIAL","INSERT","INTEGER","INTERSECT","INTO","IS","LEVEL","LIKE","LOCK",
        "LONG","MAXEXTENTS","MINUS","MLSLABEL","MODE","MODIFY","NESTED_TABLE_ID","NOAUDIT","NOCOMPRESS","NOT","NOWAIT",
        "NULL","NUMBER","OF","OFFLINE","ON","ONLINE","OPTION","OR","ORDER","PCTFREE","PRIOR","PUBLIC","RAW","RENAME",
        "RESOURCE","REVOKE","ROW","ROWID","ROWNUM","ROWS","SELECT","SESSION","SET","SHARE","SIZE","SMALLINT","START",
        "SUCCESSFUL","SYNONYM","SYSDATE","TABLE","THEN","TO","TRIGGER","UID","UNION","UNIQUE","UPDATE","USER","VALIDATE",
        "VALUES","VARCHAR","VARCHAR2","VIEW","WHENEVER","WHERE","WITH",
    };

    /// <summary>Reserved words that nevertheless act as value expressions.</summary>
    private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.Ordinal)
    {
        "LEVEL","ROWNUM","ROWID","SYSDATE","USER","UID","COLUMN_VALUE","NESTED_TABLE_ID","MODE",
    };

    /// <summary>Non-reserved words that begin a clause and so can't be implicit aliases.</summary>
    private static readonly HashSet<string> ClauseWords = new(StringComparer.Ordinal)
    {
        "NATURAL","INNER","LEFT","RIGHT","FULL","CROSS","OUTER","JOIN","USING","FETCH","OFFSET","WHEN","MATCHED",
        "RETURNING","RETURN","PIVOT","UNPIVOT","SAMPLE","PARTITION","APPLY","WINDOW","MODEL","LOG","OVER","WITHIN",
        "KEEP","VERSIONS","FLASHBACK","ESCAPE","LATERAL","COLUMNS","BULK","ONE","AFTER","PATTERN","INITIAL","SEEK","SUBSET","DEFINE","MEASURES","MATCH_RECOGNIZE","DIMENSION","RULES","CYCLE","SEARCH",
    };

    private bool CanBeImplicitAlias(Token t) =>
        t.Type == TokenType.QuotedIdentifier ||
        (t.Type == TokenType.Identifier && !Reserved.Contains(t.Value) && !ClauseWords.Contains(t.Value));

    // ---- script & statements ----

    private SqlScript ParseScript(List<ParseError> errors)
    {
        var script = new SqlScript();
        var first = Cur;
        int prog1 = -1;
        while (Progress(ref prog1))
        {
            int prog103 = -1;
            while ((AcceptOp(";") || AcceptOp("/")) && Progress(ref prog103)) { }
            if (Cur.Type == TokenType.EOF) break;
            int startIdx = _p;
            _depth = 0;
            Exception? failure = null;
            try
            {
                var st = ParseStatementCore();
                // A statement that contains a lexical error (already reported) is unreliable: drop it.
                int from = _t[startIdx].Start, to = Prev.End;
                if (!_lexErrors.Any(e => e.Offset >= from && e.Offset <= to)) script.Statements.Add(st);
                if (st is not SqlPlusCommand && !(st is ExecStatement && Cur.Line > Prev.Line) && Cur.Type != TokenType.EOF && !IsOp(";") && !IsOp("/"))
                    throw Fail($"Unexpected {Describe(Cur)}");
            }
            catch (ParseException ex) { failure = ex; }
            // Last-resort net for defects not yet found: Parse must never throw.
            catch (Exception ex) when (ex is not OutOfMemoryException) { failure = ex; }
            // Recover outside the catch block: a catch handler still runs on top of the throwing (possibly very deep) stack.
            if (failure is ParseException pe)
            {
                errors.Add(pe.Error);
                Resync(startIdx);
            }
            else if (failure != null)
            {
                errors.Add(new ParseError($"internal parser error: {failure.GetType().Name}", Cur.Start, Cur.Line, Cur.Column));
                Resync(startIdx);
            }
            if (_p == startIdx && Cur.Type != TokenType.EOF) Next();
        }
        return Fin(script, first);
    }

    /// <summary>
    /// Skips to a safe restart point after a syntax error. Inside PL/SQL blocks and stored program units
    /// (which contain ';' internally) the restart point is a '/' alone on its line, else the ';' that ends the
    /// outermost END; for everything else it is the next ';'.
    /// </summary>
    private void Resync(int startIdx)
    {
        if (IsProgramUnitStart(startIdx))
        {
            for (int i = _p; i < _t.Count - 1; i++)
                if (IsLoneSlash(i)) { _p = i; return; }
            for (int i = _p; i < _t.Count - 1; i++)
            {
                if (_t[i].Type != TokenType.Operator || _t[i].Text != ";" || i == 0) continue;
                bool afterEnd = IsEndToken(i - 1) || (i >= 2 && _t[i - 1].Type is TokenType.Identifier or TokenType.QuotedIdentifier && IsEndToken(i - 2));
                if (afterEnd) { _p = i; return; }
            }
            _p = _t.Count - 1;
            return;
        }
        while (Cur.Type != TokenType.EOF && !IsOp(";") && !IsLoneSlash(_p)) Next();
    }

    private bool IsEndToken(int i) => _t[i].Type == TokenType.Identifier && _t[i].Value == "END";

    private bool IsLoneSlash(int i) =>
        _t[i].Type == TokenType.Operator && _t[i].Text == "/" &&
        (i == 0 || _t[i - 1].Line < _t[i].Line) &&
        (i + 1 >= _t.Count || _t[i + 1].Type == TokenType.EOF || _t[i + 1].Line > _t[i].Line);

    private bool IsProgramUnitStart(int idx)
    {
        var t = _t[idx];
        if (t.Type == TokenType.Operator && t.Text == "<<") return true;
        if (t.Type != TokenType.Identifier) return false;
        if (t.Value is "DECLARE" or "BEGIN") return true;
        if (t.Value != "CREATE") return false;
        int i = idx + 1;
        while (i < _t.Count && _t[i].Type == TokenType.Identifier && _t[i].Value is "OR" or "REPLACE" or "EDITIONABLE" or "NONEDITIONABLE") i++;
        return i < _t.Count && _t[i].Type == TokenType.Identifier && _t[i].Value is "PROCEDURE" or "FUNCTION" or "PACKAGE" or "TRIGGER" or "TYPE";
    }

    private Statement ParseStatementCore()
    {
        if (IsKw("SELECT") || IsKw("WITH") || IsOp("(")) return ParseSelectStatement();
        if (IsKw("INSERT")) return ParseInsert();
        if (IsKw("UPDATE")) return ParseUpdate();
        if (IsKw("DELETE")) return ParseDelete();
        if (IsKw("MERGE")) return ParseMerge();
        return ParseOtherStatement();
    }

    // ---- queries ----

    private bool LooksLikeQuery()
    {
        int n = 0;
        while (IsOpAt(n, "(")) n++;
        return IsKwAt(n, "SELECT") || IsKwAt(n, "WITH");
    }

    private SelectStatement ParseSelectStatement()
    {
        Enter();
        var r = ParseSelectStatementCore();
        Leave();
        return r;
    }

    private SelectStatement ParseSelectStatementCore()
    {
        var start = Cur;
        var stmt = new SelectStatement();
        if (AcceptKw("WITH"))
        {
            int prog104 = -1;
            while ((IsKw("FUNCTION") || IsKw("PROCEDURE")) && Progress(ref prog104)) { stmt.WithDeclarations.Add(ParseSubprogram()); AcceptOp(";"); }
            if (!(IsKw("SELECT") || IsOp("(")))
            do
            {
                var cs = Cur;
                var cte = new CommonTableExpression { Name = ParseIdentifier() };
                if (AcceptOp("(")) { cte.Columns = ParseIdentifierList(); ExpectOp(")"); }
                ExpectKw("AS");
                ExpectOp("(");
                cte.Query = ParseSelectStatement();
                ExpectOp(")");
                if (IsKw("SEARCH") && (IsKwAt(1, "DEPTH") || IsKwAt(1, "BREADTH"))) cte.Search = ParseSearchClause();
                if (IsKw("CYCLE")) cte.Cycle = ParseCycleClause();
                stmt.With.Add(Fin(cte, cs));
            } while (AcceptOp(","));
        }

        stmt.Body = ParseQueryExpression();

        if (IsKw("ORDER"))
        {
            Next();
            stmt.OrderSiblings = AcceptKw("SIBLINGS");
            ExpectKw("BY");
            stmt.OrderBy = ParseOrderByList();
        }
        if (IsKw("OFFSET") && !IsOpAt(1, ",")) // OFFSET n ROWS
        {
            Next();
            stmt.Offset = ParseAdditive();
            if (!AcceptKw("ROWS")) AcceptKw("ROW");
        }
        if (IsKw("FETCH"))
        {
            var fs = Next();
            var f = new FetchClause { Next = IsKw("NEXT") };
            if (!AcceptKw("FIRST") && !AcceptKw("NEXT")) throw Fail("Expected FIRST or NEXT after FETCH");
            if (!IsKw("ROW") && !IsKw("ROWS"))
            {
                f.Count = ParseAdditive();
                f.Percent = AcceptKw("PERCENT");
            }
            if (!AcceptKw("ROWS")) ExpectKw("ROW");
            if (AcceptKw("ONLY")) { }
            else { ExpectKw("WITH"); ExpectKw("TIES"); f.WithTies = true; }
            stmt.Fetch = Fin(f, fs);
        }
        if (IsKw("FOR") && IsKwAt(1, "UPDATE"))
        {
            var us = Next(); Next();
            var fu = new ForUpdateClause();
            if (AcceptKw("OF"))
            {
                do fu.Of.Add(ParseColumnReferenceOnly()); while (AcceptOp(","));
            }
            if (AcceptKw("NOWAIT")) fu.NoWait = true;
            else if (AcceptKw("WAIT")) fu.Wait = ParseAdditive();
            else if (AcceptKw("SKIP")) { ExpectKw("LOCKED"); fu.SkipLocked = true; }
            stmt.ForUpdate = Fin(fu, us);
        }
        return Fin(stmt, start);
    }

    private QueryExpression ParseQueryExpression()
    {
        var start = Cur;
        var left = ParseQueryTerm();
        int prog2 = -1;
        while (Progress(ref prog2))
        {
            SetOperator op;
            if (IsKw("UNION")) op = SetOperator.Union;
            else if (IsKw("INTERSECT")) op = SetOperator.Intersect;
            else if (IsKw("MINUS")) op = SetOperator.Minus;
            else break;
            Next();
            bool all = AcceptKw("ALL");
            if (!all) AcceptKw("DISTINCT");
            var right = ParseQueryTerm();
            left = Fin(new SetOperation { Left = left, Operator = op, All = all, Right = right }, start);
        }
        return left;
    }

    private QueryExpression ParseQueryTerm()
    {
        var start = Cur;
        if (IsOp("("))
        {
            Next();
            var q = ParseSelectStatement();
            ExpectOp(")");
            return Fin(new ParenthesizedQuery { Query = q }, start);
        }
        return ParseQuerySpecification();
    }

    private QuerySpecification ParseQuerySpecification()
    {
        var start = Cur;
        ExpectKw("SELECT");
        var qs = new QuerySpecification();
        if (Cur.Type == TokenType.Hint) qs.Hint = ParseHint(Next());
        if (AcceptKw("ALL")) qs.Quantifier = QuantifierKind.All;
        else if (AcceptKw("DISTINCT")) qs.Quantifier = QuantifierKind.Distinct;
        else if (AcceptKw("UNIQUE")) qs.Quantifier = QuantifierKind.Unique;

        do qs.SelectList.Add(ParseSelectItem()); while (AcceptOp(","));

        if (IsKw("BULK") && IsKwAt(1, "COLLECT")) { Next(); Next(); qs.IntoBulkCollect = true; ExpectKw("INTO"); ParseIntoTargets(qs.Into); }
        else if (AcceptKw("INTO")) ParseIntoTargets(qs.Into);

        if (AcceptKw("FROM"))
        {
            do qs.From.Add(ParseJoinedTable()); while (AcceptOp(","));
        }

        // WHERE, hierarchical, GROUP BY, HAVING: Oracle permits hierarchical/GROUP BY in either order.
        int prog3 = -1;
        while (Progress(ref prog3))
        {
            if (IsKw("WHERE") && qs.Where == null) { Next(); qs.Where = ParseExpr(); }
            else if (IsKw("START") && IsKwAt(1, "WITH") && qs.StartWith == null)
            {
                Next(); Next(); qs.StartWith = ParseExpr();
            }
            else if (IsKw("CONNECT") && IsKwAt(1, "BY") && qs.ConnectBy == null)
            {
                Next(); Next();
                qs.NoCycle = AcceptKw("NOCYCLE");
                qs.ConnectBy = ParseExpr();
            }
            else if (IsKw("GROUP") && IsKwAt(1, "BY") && qs.GroupBy.Count == 0)
            {
                Next(); Next();
                do qs.GroupBy.Add(ParseGroupByItem()); while (AcceptOp(","));
            }
            else if (IsKw("HAVING") && qs.Having == null) { Next(); qs.Having = ParseExpr(); }
            else if (IsKw("MODEL") && qs.Model == null) qs.Model = ParseModelClause();
            else break;
        }
        return Fin(qs, start);
    }

    /// <summary>An element of GROUPING SETS: a parenthesized column list (always an ExpressionList, even with one item or none), or a ROLLUP/CUBE/expression.</summary>
    private Expression ParseGroupingSetElement()
    {
        var start = Cur;
        if (!IsOp("(")) return ParseGroupByItem();
        Next();
        var list = new ExpressionList();
        if (!IsOp(")")) do list.Items.Add(ParseExpr()); while (AcceptOp(","));
        ExpectOp(")");
        return Fin(list, start);
    }

    private void ParseIntoTargets(List<Expression> into)
    {
        do into.Add(ParsePostfix()); while (AcceptOp(","));
    }

    private Expression ParseGroupByItem()
    {
        var start = Cur;
        if (IsOp("(") && IsOpAt(1, ")")) { Next(); Next(); return Fin(new ExpressionList(), start); }
        if ((IsKw("ROLLUP") || IsKw("CUBE")) && IsOpAt(1, "(")) return ParseExpr(); // parsed as function call
        if (IsKw("GROUPING") && IsKwAt(1, "SETS") && IsOpAt(2, "("))
        {
            Next(); Next(); Next();
            var fc = new FunctionCall { Name = [new Identifier { Value = "GROUPING SETS", Start = start.Start, End = start.End, FirstTokenIndex = start.TokenIndex, LastTokenIndex = start.TokenIndex }] };
            if (!IsOp(")"))
                do { var a = Cur; fc.Arguments.Add(Fin(new Argument { Value = ParseGroupingSetElement() }, a)); } while (AcceptOp(","));
            ExpectOp(")");
            return Fin(fc, start);
        }
        return ParseExpr();
    }

    private SelectItem ParseSelectItem()
    {
        var start = Cur;
        if (IsOp("*")) { Next(); return Fin(new StarSelectItem(), start); }
        var e = ParseExpr();
        if (e is StarExpression star) return Fin(new StarSelectItem { Qualifier = star.Qualifier }, start);
        var item = new ExpressionSelectItem { Expression = e };
        item.Alias = ParseOptionalAlias();
        return Fin(item, start);
    }

    private Identifier? ParseOptionalAlias()
    {
        if (IsKw("AS") && !IsKwAt(1, "OF")) { Next(); return ParseIdentifier(); }
        if (CanBeImplicitAlias(Cur)) return ParseIdentifier();
        return null;
    }

    private List<OrderByItem> ParseOrderByList()
    {
        var list = new List<OrderByItem>();
        do
        {
            var s = Cur;
            var item = new OrderByItem { Expression = ParseExpr() };
            if (AcceptKw("DESC")) item.Descending = true; else AcceptKw("ASC");
            if (IsKw("NULLS"))
            {
                Next();
                if (AcceptKw("FIRST")) item.Nulls = NullsOrdering.First;
                else { ExpectKw("LAST"); item.Nulls = NullsOrdering.Last; }
            }
            list.Add(Fin(item, s));
        } while (AcceptOp(","));
        return list;
    }

    // ---- table references ----

    private TableReference ParseJoinedTable()
    {
        var start = Cur;
        var left = ParseTablePrimary();
        int prog4 = -1;
        while (Progress(ref prog4))
        {
            var js = Cur;
            bool natural = false;
            JoinKind kind;
            if (IsKw("NATURAL")) { Next(); natural = true; }

            if (IsKw("CROSS") && IsKwAt(1, "APPLY")) { Next(); Next(); kind = JoinKind.CrossApply; }
            else if (IsKw("OUTER") && IsKwAt(1, "APPLY")) { Next(); Next(); kind = JoinKind.OuterApply; }
            else if (IsKw("CROSS")) { Next(); ExpectKw("JOIN"); kind = JoinKind.Cross; }
            else if (IsKw("INNER")) { Next(); ExpectKw("JOIN"); kind = JoinKind.Inner; }
            else if (IsKw("LEFT") || IsKw("RIGHT") || IsKw("FULL"))
            {
                kind = Cur.Value switch { "LEFT" => JoinKind.Left, "RIGHT" => JoinKind.Right, _ => JoinKind.Full };
                Next(); AcceptKw("OUTER"); ExpectKw("JOIN");
            }
            else if (IsKw("JOIN")) { Next(); kind = JoinKind.Inner; }
            else if (natural) throw Fail("Expected JOIN after NATURAL");
            else break;

            var join = new JoinTable { Left = left, Kind = kind, Natural = natural };
            join.Right = ParseTablePrimary();
            if (kind is JoinKind.Inner or JoinKind.Left or JoinKind.Right or JoinKind.Full && !natural)
            {
                if (AcceptKw("ON")) join.On = ParseExpr();
                else if (AcceptKw("USING"))
                {
                    ExpectOp("("); join.Using = ParseIdentifierList(); ExpectOp(")");
                }
                else throw Fail($"Expected ON or USING but found {Describe(Cur)}");
            }
            left = Fin(join, start);
        }
        return left;
    }

    private TableReference ParseTablePrimary()
    {
        var start = Cur;
        TableReference t;
        if (IsOp("("))
        {
            Next();
            if (IsKw("VALUES"))
            {
                var vt = new ValuesTable();
                Next();
                do
                {
                    ExpectOp("(");
                    var row = new List<Expression>();
                    do row.Add(ParseExpr()); while (AcceptOp(","));
                    ExpectOp(")");
                    vt.Rows.Add(row);
                } while (AcceptOp(","));
                ExpectOp(")");
                t = vt;
                var alias = ParseOptionalAlias();
                if (alias != null && IsOp("(")) { Next(); vt.ColumnAliases = ParseIdentifierList(); ExpectOp(")"); }
                vt.Alias = alias;
            }
            else if (LooksLikeQuery())
            {
                var q = ParseSelectStatement();
                ExpectOp(")");
                t = new SubqueryTable { Query = q };
            }
            else
            {
                var inner = ParseJoinedTable();
                ExpectOp(")");
                t = new ParenthesizedTable { Inner = inner };
            }
        }
        else if (IsKw("LATERAL") && IsOpAt(1, "("))
        {
            Next(); Next();
            var q = ParseSelectStatement();
            ExpectOp(")");
            t = new SubqueryTable { Lateral = true, Query = q };
        }
        else if (IsKw("XMLTABLE") && IsOpAt(1, "(")) t = ParseXmlTable();
        else if (IsKw("JSON_TABLE") && IsOpAt(1, "(")) t = ParseJsonTable();
        else if (IsKw("GRAPH_TABLE") && IsOpAt(1, "(")) t = ParseGraphTable();
        else if (IsKw("TABLE") && IsOpAt(1, "("))
        {
            Next(); Next();
            var e = ParseExpr();
            ExpectOp(")");
            t = new TableFunction { Expression = e };
        }
        else
        {
            var nt = new NamedTable { Name = ParseDottedName() };
            if (AcceptOp("@"))
            {
                nt.DbLink = ParseDottedName();
            }
            if (IsKw("PARTITION") && IsOpAt(1, "("))
            {
                Next(); Next(); nt.Partition = ParseIdentifier(); ExpectOp(")");
            }
            t = nt;
        }
        return ParseTableModifiers(t, start);
    }

    // ---- DML ----

    private Statement ParseInsert()
    {
        var start = Cur;
        ExpectKw("INSERT");
        var st = new InsertStatement();
        if (Cur.Type == TokenType.Hint) st.Hint = ParseHint(Next());
        if (IsKw("ALL") || IsKw("FIRST") || IsKw("WHEN")) return ParseMultiTableInsert(st.Hint, start);
        ExpectKw("INTO");
        st.Target = ParseTablePrimary();
        if (IsOp("(") && !LooksLikeQueryAfterParen())
        {
            Next(); st.Columns = ParseIdentifierList(); ExpectOp(")");
        }
        if (AcceptKw("VALUES"))
        {
            if (!IsOp("(")) st.RecordValue = ParseExpr();
            else
                do
                {
                    ExpectOp("(");
                    var row = new List<Expression>();
                    do row.Add(ParseExpr()); while (AcceptOp(","));
                    ExpectOp(")");
                    st.ValueRows.Add(row);
                } while (AcceptOp(","));
        }
        else if (IsKw("SELECT") || IsKw("WITH") || IsOp("(")) st.Query = ParseSelectStatement();
        else throw Fail($"Expected VALUES or SELECT but found {Describe(Cur)}");
        ParseDmlTail(st);
        return Fin(st, start);
    }

    private bool LooksLikeQueryAfterParen()
    {
        int n = 1;
        while (IsOpAt(n, "(")) n++;
        return IsKwAt(n, "SELECT") || IsKwAt(n, "WITH");
    }

    private UpdateStatement ParseUpdate()
    {
        var start = Cur;
        ExpectKw("UPDATE");
        var st = new UpdateStatement();
        if (Cur.Type == TokenType.Hint) st.Hint = ParseHint(Next());
        st.Target = ParseTablePrimary();
        ExpectKw("SET");
        st.Set = ParseSetClauses();
        if (AcceptKw("FROM")) { do st.From.Add(ParseJoinedTable()); while (AcceptOp(",")); }
        if (AcceptKw("WHERE")) st.Where = ParseWhereCondition();
        ParseDmlTail(st);
        return Fin(st, start);
    }

    private List<SetClause> ParseSetClauses()
    {
        var list = new List<SetClause>();
        do
        {
            var s = Cur;
            var sc = new SetClause();
            if (IsKw("ROW") && IsOpAt(1, "="))
            {
                Next(); Next();
                sc.Row = true;
                sc.Value = ParseExpr();
                list.Add(Fin(sc, s));
                continue;
            }
            if (AcceptOp("("))
            {
                do sc.Columns.Add(ParseColumnReferenceOnly()); while (AcceptOp(","));
                ExpectOp(")");
            }
            else sc.Columns.Add(ParseColumnReferenceOnly());
            ExpectOp("=");
            sc.Value = ParseExpr();
            list.Add(Fin(sc, s));
        } while (AcceptOp(","));
        return list;
    }

    private DeleteStatement ParseDelete()
    {
        var start = Cur;
        ExpectKw("DELETE");
        var st = new DeleteStatement();
        if (Cur.Type == TokenType.Hint) st.Hint = ParseHint(Next());
        AcceptKw("FROM");
        st.Target = ParseTablePrimary();
        if (AcceptKw("FROM")) { do st.From.Add(ParseJoinedTable()); while (AcceptOp(",")); }
        if (AcceptKw("WHERE")) st.Where = ParseWhereCondition();
        ParseDmlTail(st);
        return Fin(st, start);
    }

    private Expression ParseWhereCondition()
    {
        if (IsKw("CURRENT") && IsKwAt(1, "OF"))
        {
            var s = Cur; Next(); Next();
            return Fin(new CurrentOfExpression { Cursor = ParseIdentifier() }, s);
        }
        return ParseExpr();
    }

    /// <summary>RETURNING ... INTO ... and LOG ERRORS.</summary>
    private void ParseDmlTail(DmlStatement st)
    {
        if (AcceptKw("RETURNING") || AcceptKw("RETURN"))
        {
            do st.Returning.Add(ParseReturningItem()); while (AcceptOp(","));
            if (IsKw("BULK") && IsKwAt(1, "COLLECT")) { Next(); Next(); st.ReturningBulkCollect = true; }
            ExpectKw("INTO");
            do st.ReturningInto.Add(ParsePostfix()); while (AcceptOp(","));
        }
        ParseErrorLogging(st);
    }

    /// <summary>
    /// OLD and NEW are not reserved words, so they only qualify the item when an expression follows them:
    /// `RETURNING OLD a` qualifies, while `RETURNING old, new INTO` and `RETURNING old INTO` name columns.
    /// </summary>
    private ReturningItem ParseReturningItem()
    {
        var s = Cur;
        var item = new ReturningItem();
        var next = PeekTok();
        bool nextStartsItem = next.Type == TokenType.QuotedIdentifier || (next.Type == TokenType.Identifier && !Reserved.Contains(next.Value));
        if ((IsKw("OLD") || IsKw("NEW")) && nextStartsItem)
        {
            item.Image = IsKw("OLD") ? ReturningImage.Old : ReturningImage.New;
            Next();
        }
        item.Expression = ParseExpr();
        return Fin(item, s);
    }

    /// <summary>LOG ERRORS [INTO t] [('tag')] [REJECT LIMIT n | UNLIMITED]; also the last clause of MERGE.</summary>
    private void ParseErrorLogging(DmlStatement st)
    {
        if (IsKw("LOG") && IsKwAt(1, "ERRORS"))
        {
            var s = Next(); Next();
            var el = new ErrorLogging();
            if (AcceptKw("INTO")) el.Table = ParseDottedName();
            if (IsOp("(")) { Next(); el.Tag = ParseExpr(); ExpectOp(")"); }
            if (AcceptKw("REJECT")) { ExpectKw("LIMIT"); if (AcceptKw("UNLIMITED")) el.RejectUnlimited = true; else el.RejectLimit = ParseAdditive(); }
            st.ErrorLogging = Fin(el, s);
        }
    }

    private MergeStatement ParseMerge()
    {
        var start = Cur;
        ExpectKw("MERGE");
        var st = new MergeStatement();
        if (Cur.Type == TokenType.Hint) st.Hint = ParseHint(Next());
        ExpectKw("INTO");
        st.Target = ParseTablePrimary();
        ExpectKw("USING");
        st.Source = ParseTablePrimary();
        ExpectKw("ON");
        st.On = ParseExpr();
        int prog105 = -1;
        while (IsKw("WHEN") && Progress(ref prog105))
        {
            var cs = Next();
            var mc = new MergeClause();
            if (AcceptKw("NOT")) mc.Matched = false; else mc.Matched = true;
            ExpectKw("MATCHED");
            if (AcceptKw("AND")) mc.AndCondition = ParseExpr();
            ExpectKw("THEN");
            if (AcceptKw("UPDATE"))
            {
                ExpectKw("SET");
                mc.Set = ParseSetClauses();
                if (AcceptKw("WHERE")) mc.UpdateWhere = ParseExpr();
                if (AcceptKw("DELETE")) { ExpectKw("WHERE"); mc.DeleteWhere = ParseExpr(); }
            }
            else if (AcceptKw("DELETE")) { if (AcceptKw("WHERE")) mc.DeleteWhere = ParseExpr(); }
            else if (AcceptKw("INSERT"))
            {
                if (AcceptOp("(")) { mc.InsertColumns = ParseIdentifierList(); ExpectOp(")"); }
                ExpectKw("VALUES");
                ExpectOp("(");
                do mc.InsertValues.Add(ParseExpr()); while (AcceptOp(","));
                ExpectOp(")");
                if (AcceptKw("WHERE")) mc.InsertWhere = ParseExpr();
            }
            else throw Fail($"Expected UPDATE, DELETE or INSERT but found {Describe(Cur)}");
            st.Clauses.Add(Fin(mc, cs));
        }
        if (st.Clauses.Count == 0) throw Fail("Expected WHEN clause in MERGE");
        ParseErrorLogging(st);
        return Fin(st, start);
    }

    // ---- identifiers ----

    private Identifier ParseIdentifier()
    {
        var t = Cur;
        if (t.Type is not (TokenType.Identifier or TokenType.QuotedIdentifier))
            throw Fail($"Expected identifier but found {Describe(t)}");
        Next();
        return Fin(new Identifier { Value = t.Value, Quoted = t.Type == TokenType.QuotedIdentifier, Text = t.Type == TokenType.QuotedIdentifier ? t.Value : t.Text }, t);
    }

    private List<Identifier> ParseIdentifierList()
    {
        var list = new List<Identifier>();
        do list.Add(ParseIdentifier()); while (AcceptOp(","));
        return list;
    }

    private List<Identifier> ParseDottedName()
    {
        var parts = new List<Identifier> { ParseIdentifier() };
        int prog106 = -1;
        while (IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier && Progress(ref prog106))
        {
            Next();
            parts.Add(ParseIdentifier());
        }
        return parts;
    }

    private ColumnReference ParseColumnReferenceOnly()
    {
        var s = Cur;
        var cr = new ColumnReference { Parts = ParseDottedName() };
        return Fin(cr, s);
    }
}
