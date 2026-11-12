namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- expressions ----

    private Expression ParseExpr() => ParseOr();

    private Expression ParseOr()
    {
        var start = Cur;
        var left = ParseAnd();
        while (IsKw("OR"))
        {
            Next();
            left = Fin(new BinaryExpression { Left = left, Operator = "OR", Right = ParseAnd() }, start);
        }
        return left;
    }

    private Expression ParseAnd()
    {
        var start = Cur;
        var left = ParseNot();
        while (IsKw("AND"))
        {
            Next();
            left = Fin(new BinaryExpression { Left = left, Operator = "AND", Right = ParseNot() }, start);
        }
        return left;
    }

    private Expression ParseNot()
    {
        var start = Cur;
        if (IsKw("NOT"))
        {
            Next();
            return Fin(new UnaryExpression { Operator = "NOT", Operand = ParseNot() }, start);
        }
        return ParseComparison();
    }

    private static readonly HashSet<string> ComparisonOps = ["=", "<>", "!=", "^=", "~=", "<", ">", "<=", ">="];

    private Expression ParseComparison()
    {
        var start = Cur;
        var left = ParseAdditive();
        while (true)
        {
            if (Cur.Type == TokenType.Operator && ComparisonOps.Contains(Cur.Text))
            {
                var op = Next().Text;
                if ((IsKw("ANY") || IsKw("SOME") || IsKw("ALL")) && IsOpAt(1, "("))
                {
                    var q = Next().Value switch { "ANY" => ComparisonQuantifier.Any, "SOME" => ComparisonQuantifier.Some, _ => ComparisonQuantifier.All };
                    left = Fin(new QuantifiedComparison { Left = left, Operator = op, Quantifier = q, Right = ParsePrimary() }, start);
                }
                else left = Fin(new BinaryExpression { Left = left, Operator = op, Right = ParseAdditive() }, start);
                continue;
            }
            if (IsKw("IS")) { left = ParseIs(left, start); continue; }

            bool negated = false;
            int off = 0;
            if (IsKw("NOT")) { negated = true; off = 1; }
            if (IsKwAt(off, "IN"))
            {
                if (negated) Next();
                Next();
                ExpectOp("(");
                var ie = new InExpression { Value = left, Not = negated };
                if (LooksLikeQuery())
                {
                    ie.Subquery = ParseSelectStatement();
                    if (IsOp(",")) throw Fail("Mixed subquery and list in IN is not supported");
                }
                else do ie.List.Add(ParseExpr()); while (AcceptOp(","));
                ExpectOp(")");
                left = Fin(ie, start);
                continue;
            }
            if (IsKwAt(off, "BETWEEN"))
            {
                if (negated) Next();
                Next();
                var low = ParseAdditive();
                ExpectKw("AND");
                var high = ParseAdditive();
                left = Fin(new BetweenExpression { Value = left, Not = negated, Low = low, High = high }, start);
                continue;
            }
            if (IsKwAt(off, "LIKE") || IsKwAt(off, "LIKEC") || IsKwAt(off, "LIKE2") || IsKwAt(off, "LIKE4"))
            {
                if (negated) Next();
                var kind = Next().Value switch { "LIKEC" => LikeKind.Likec, "LIKE2" => LikeKind.Like2, "LIKE4" => LikeKind.Like4, _ => LikeKind.Like };
                var le = new LikeExpression { Value = left, Not = negated, Kind = kind, Pattern = ParseAdditive() };
                if (AcceptKw("ESCAPE")) le.Escape = ParseAdditive();
                left = Fin(le, start);
                continue;
            }
            if (IsKwAt(off, "MEMBER") || IsKwAt(off, "SUBMULTISET"))
            {
                if (negated) Next();
                var word = Next().Value;
                AcceptKw("OF");
                left = Fin(new BinaryExpression { Left = left, Operator = (negated ? "NOT " : "") + word + " OF", Right = ParseAdditive() }, start);
                continue;
            }
            break;
        }
        return left;
    }

    private Expression ParseIs(Expression left, Token start)
    {
        Next(); // IS
        bool not = AcceptKw("NOT");
        var ie = new IsExpression { Value = left, Not = not };
        if (AcceptKw("NULL")) ie.Test = IsTest.Null;
        else if (AcceptKw("NAN")) ie.Test = IsTest.Nan;
        else if (AcceptKw("INFINITE")) ie.Test = IsTest.Infinite;
        else if (AcceptKw("EMPTY")) ie.Test = IsTest.Empty;
        else if (AcceptKw("PRESENT")) ie.Test = IsTest.Present;
        else if (AcceptKw("DANGLING")) ie.Test = IsTest.Dangling;
        else if (IsKw("A") && IsKwAt(1, "SET")) { Next(); Next(); ie.Test = IsTest.ASet; }
        else if (AcceptKw("JSON"))
        {
            ie.Test = IsTest.Json;
            while (IsKw("FORMAT") || IsKw("STRICT") || IsKw("LAX") || IsKw("ALLOW") || IsKw("DISALLOW") ||
                   ((IsKw("WITH") || IsKw("WITHOUT")) && IsKwAt(1, "UNIQUE")))
                ie.JsonClauses.Add(ParseFunctionClause());
        }
        else if (AcceptKw("OF"))
        {
            ie.Test = IsTest.OfType;
            AcceptKw("TYPE");
            ExpectOp("(");
            do
            {
                var s = Cur;
                var ot = new IsOfType { Only = AcceptKw("ONLY") };
                ot.Type = ParseDataType();
                ie.OfTypes.Add(Fin(ot, s));
            } while (AcceptOp(","));
            ExpectOp(")");
        }
        else throw Fail($"Expected NULL, NAN, INFINITE, EMPTY, PRESENT, A SET, JSON, OF or DANGLING after IS but found {Describe(Cur)}");
        return Fin(ie, start);
    }

    private Expression ParseAdditive()
    {
        var start = Cur;
        var left = ParseMultiplicative();
        while (true)
        {
            if (Cur.Type == TokenType.Operator && Cur.Text is "+" or "-" or "||")
            {
                var op = Next().Text;
                left = Fin(new BinaryExpression { Left = left, Operator = op, Right = ParseMultiplicative() }, start);
            }
            else if (IsKw("MULTISET") && (IsKwAt(1, "UNION") || IsKwAt(1, "INTERSECT") || IsKwAt(1, "EXCEPT")))
            {
                Next();
                var op = "MULTISET " + Next().Value;
                if (IsKw("ALL") || IsKw("DISTINCT")) op += " " + Next().Value;
                left = Fin(new BinaryExpression { Left = left, Operator = op, Right = ParseMultiplicative() }, start);
            }
            else break;
        }
        return left;
    }

    private Expression ParseMultiplicative()
    {
        var start = Cur;
        var left = ParseUnary();
        while (Cur.Type == TokenType.Operator && Cur.Text is "*" or "/")
        {
            var op = Next().Text;
            left = Fin(new BinaryExpression { Left = left, Operator = op, Right = ParseUnary() }, start);
        }
        return left;
    }

    private Expression ParseUnary()
    {
        var start = Cur;
        if (Cur.Type == TokenType.Operator && Cur.Text is "+" or "-")
        {
            var op = Next().Text;
            return Fin(new UnaryExpression { Operator = op, Operand = ParseUnary() }, start);
        }
        if (IsKw("PRIOR") || IsKw("CONNECT_BY_ROOT"))
        {
            var op = Next().Value;
            return Fin(new UnaryExpression { Operator = op, Operand = ParseUnary() }, start);
        }
        return ParsePower();
    }

    private Expression ParsePower()
    {
        var start = Cur;
        var left = ParsePostfix();
        while (IsOp("**"))
        {
            Next();
            left = Fin(new BinaryExpression { Left = left, Operator = "**", Right = ParsePostfix() }, start);
        }
        return left;
    }

    private Expression ParsePostfix()
    {
        var start = Cur;
        var e = ParsePrimary();
        while (true)
        {
            if (IsOp("[")) e = ParseSubscripts(e, start);
            else if (IsKw("AT") && (IsKwAt(1, "TIME") || IsKwAt(1, "LOCAL")))
            {
                Next();
                if (AcceptKw("LOCAL")) e = Fin(new AtTimeZoneExpression { Value = e, Local = true }, start);
                else
                {
                    Next(); ExpectKw("ZONE");
                    e = Fin(new AtTimeZoneExpression { Value = e, Zone = ParsePrimary() }, start);
                }
            }
            else if (IsKw("COLLATE") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier)
            {
                Next();
                e = Fin(new CollateExpression { Value = e, Collation = ParseIdentifier() }, start);
            }
            else break;
        }
        return e;
    }

    private Expression ParsePrimary()
    {
        var start = Cur;
        switch (Cur.Type)
        {
            case TokenType.Number:
                Next();
                return Fin(new Literal { Kind = LiteralKind.Number, Value = start.Text }, start);
            case TokenType.String:
                Next();
                return Fin(new Literal { Kind = LiteralKind.String, Value = start.Value, National = start.Text[0] is 'n' or 'N' }, start);
            case TokenType.Substitution:
                Next();
                return Fin(new SubstitutionVariable { Name = start.Value.TrimStart('&'), Persistent = start.Value.StartsWith('&') }, start);
            case TokenType.Bind:
            {
                Next();
                var name = start.Text[1..].Trim('"');
                while (IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier)
                {
                    Next();
                    name += "." + Next().Text;
                }
                return Fin(new BindVariable { Name = name }, start);
            }
            case TokenType.Operator:
                if (IsOp("("))
                {
                    Next();
                    if (LooksLikeQuery())
                    {
                        var q = ParseSelectStatement();
                        ExpectOp(")");
                        return Fin(new SubqueryExpression { Query = q }, start);
                    }
                    if (AcceptOp(")")) return Fin(new ExpressionList(), start);
                    var first = ParseExpr();
                    if (IsOp(","))
                    {
                        var list = new ExpressionList { Items = [first] };
                        while (AcceptOp(",")) list.Items.Add(ParseExpr());
                        ExpectOp(")");
                        return Fin(list, start);
                    }
                    ExpectOp(")");
                    return Fin(new ParenthesizedExpression { Inner = first }, start);
                }
                if (IsOp("*")) { Next(); return Fin(new StarExpression(), start); }
                throw Fail($"Unexpected {Describe(Cur)} in expression");
            case TokenType.EOF:
                throw Fail("Unexpected end of input in expression");
            case TokenType.Hint:
                throw Fail("Unexpected optimizer hint");
        }

        if (Cur.Type == TokenType.Identifier)
        {
            switch (Cur.Value)
            {
                case "NULL": Next(); return Fin(new Literal { Kind = LiteralKind.Null }, start);
                case "DEFAULT": Next(); return Fin(new Literal { Kind = LiteralKind.Default }, start);
                case "CASE": return ParseCase();
                case "CAST" or "XMLCAST" or "TREAT" or "VALIDATE_CONVERSION" or "XMLSERIALIZE" when IsOpAt(1, "("): return ParseCastLike();
                case "EXTRACT" when IsOpAt(1, "(") && PeekTok(2).Type == TokenType.Identifier && IsKwAt(3, "FROM"): return ParseExtract();
                case "TRIM" when IsOpAt(1, "("): return ParseTrim();
                case "TRUE" or "FALSE" when !IsOpAt(1, ".") && !IsOpAt(1, "("):
                    Next();
                    return Fin(new Literal { Kind = LiteralKind.Boolean, Value = start.Value }, start);
                case "CURSOR" when IsOpAt(1, "("):
                {
                    Next(); Next();
                    var cq = ParseSelectStatement();
                    ExpectOp(")");
                    return Fin(new CursorExpression { Query = cq }, start);
                }
                case "ANY" when !IsOpAt(1, "("):
                    Next();
                    return Fin(new Literal { Kind = LiteralKind.Any }, start);
                case "EXISTS" when IsOpAt(1, "("):
                {
                    Next(); Next();
                    var sub = ParseSelectStatement();
                    ExpectOp(")");
                    return Fin(new ExistsExpression { Subquery = sub }, start);
                }
                case "DATE" or "TIMESTAMP" when PeekTok().Type == TokenType.String:
                {
                    Next();
                    var lit = Next();
                    return Fin(new Literal { Kind = start.Value == "DATE" ? LiteralKind.Date : LiteralKind.Timestamp, Value = lit.Value }, start);
                }
                case "INTERVAL" when PeekTok().Type is TokenType.String or TokenType.Bind:
                    return ParseInterval();
                case "NEW" when PeekTok().Type == TokenType.Identifier && (IsOpAt(2, "(") || IsOpAt(2, ".")):
                {
                    Next();
                    var e = ParseNameOrFunction();
                    if (e is FunctionCall f) { f.New = true; f.Start = start.Start; return f; }
                    throw Fail("Expected constructor call after NEW");
                }
            }
            if (Reserved.Contains(Cur.Value) && !ExpressionKeywords.Contains(Cur.Value))
                throw Fail($"Unexpected keyword {Cur.Text}");
        }
        return ParseNameOrFunction();
    }

    private Expression ParseNameOrFunction()
    {
        var start = Cur;
        var parts = new List<Identifier> { ParseIdentifier() };
        while (IsOp("."))
        {
            if (IsOpAt(1, "*"))
            {
                Next(); Next();
                return Fin(new StarExpression { Qualifier = parts }, start);
            }
            if (PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier)
            {
                Next();
                parts.Add(ParseIdentifier());
            }
            else break;
        }

        string? attribute = null;
        if (IsOp("%") && PeekTok().Type == TokenType.Identifier)
        {
            Next();
            attribute = Next().Value;
        }

        if (attribute == null && IsOp("(") && IsOpAt(1, "+") && IsOpAt(2, ")"))
        {
            Next(); Next(); Next();
            return Fin(new ColumnReference { Parts = parts, OuterJoin = true }, start);
        }
        Expression result;
        if (IsOp("("))
        {
            var fc = ParseFunctionCall(parts, start);
            fc.Attribute = attribute;
            result = fc;
        }
        else result = Fin(new ColumnReference { Parts = parts, Attribute = attribute }, start);

        // member access on call/collection results: f(x).field, coll(i).field(j)
        while (result is FunctionCall && IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier)
        {
            Next();
            var m = ParseIdentifier();
            result = Fin(new MemberAccess { Target = result, Member = m }, start);
            if (IsOp("(")) result = ParseFunctionCall([m], start);
        }
        return result;
    }

    // ---- function calls ----

    private static readonly HashSet<string> LeadingArgWords = new(StringComparer.Ordinal)
        { "NAME", "EVALNAME", "DOCUMENT", "CONTENT", "KEY" };

    private FunctionCall ParseFunctionCall(List<Identifier> name, Token start)
    {
        var fc = new FunctionCall { Name = name };
        ExpectOp("(");
        string simple = name.Count == 1 && !name[0].Quoted ? name[0].Value : "";

        if (!IsOp(")"))
        {
            if (AcceptKw("DISTINCT")) fc.Quantifier = QuantifierKind.Distinct;
            else if (AcceptKw("UNIQUE")) fc.Quantifier = QuantifierKind.Unique;
            else if (IsKw("ALL") && !IsOpAt(1, ",") && !IsOpAt(1, ")")) { Next(); fc.Quantifier = QuantifierKind.All; }

            if (simple == "JSON_TRANSFORM") ParseJsonTransformArgs(fc);
            else
            {
                do
                {
                    var arg = ParseArgument();
                    if (IsKw("DEFAULT"))
                    {
                        var beh = ParseBehaviorClause();
                        if (beh.Event == BehaviorEvent.ConversionError) arg.OnConversionError = beh;
                        else fc.Clauses.Add(beh);
                        arg.End = Prev.End;
                    }
                    fc.Arguments.Add(arg);
                } while (AcceptOp(","));
            }

            ParseNullTreatment(fc);
            if (IsKw("ORDER") && IsKwAt(1, "BY"))
            {
                Next(); Next();
                fc.OrderBy = ParseOrderByList();
            }
            while (!IsOp(")") && Cur.Type != TokenType.EOF)
                fc.Clauses.Add(ParseFunctionClause());
        }
        ExpectOp(")");

        while (true)
        {
            if (IsKw("WITHIN") && IsKwAt(1, "GROUP"))
            {
                Next(); Next(); ExpectOp("(");
                ExpectKw("ORDER"); ExpectKw("BY");
                fc.WithinGroup = ParseOrderByList();
                ExpectOp(")");
            }
            else if (IsKw("KEEP") && IsOpAt(1, "("))
            {
                var ks = Next(); Next();
                ExpectKw("DENSE_RANK");
                var keep = new KeepClause();
                if (AcceptKw("FIRST")) keep.First = true; else ExpectKw("LAST");
                ExpectKw("ORDER"); ExpectKw("BY");
                keep.OrderBy = ParseOrderByList();
                ExpectOp(")");
                fc.Keep = Fin(keep, ks);
            }
            else if (IsKw("IGNORE") || IsKw("RESPECT")) ParseNullTreatment(fc);
            else if (IsKw("OVER") && (IsOpAt(1, "(") || PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier))
            {
                Next();
                fc.Over = ParseWindowSpec();
            }
            else break;
        }
        return Fin(fc, start);
    }

    private Argument ParseArgument()
    {
        var a = Cur;
        var arg = new Argument();
        if (IsOp("*")) { Next(); arg.Value = Fin(new StarExpression(), a); return Fin(arg, a); }
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier && IsOpAt(1, "=>"))
        {
            arg.Name = ParseIdentifier();
            Next();
        }
        if (Cur.Type == TokenType.Identifier && LeadingArgWords.Contains(Cur.Value) &&
            PeekTok().Type is TokenType.Identifier or TokenType.String or TokenType.QuotedIdentifier or TokenType.Number or TokenType.Bind)
        {
            arg.Keyword = Next().Value switch
            {
                "NAME" => ArgumentKeyword.Name, "EVALNAME" => ArgumentKeyword.EvalName, "DOCUMENT" => ArgumentKeyword.Document,
                "CONTENT" => ArgumentKeyword.Content, _ => ArgumentKeyword.Key,
            };
        }
        arg.Value = ParseExpr();
        if (AcceptKw("VALUE")) arg.MappedValue = ParseExpr();
        else if (Cur.Type == TokenType.Bind && Cur.Text == ":") { Next(); arg.MappedValue = ParseExpr(); }
        if (IsKw("FORMAT") && IsKwAt(1, "JSON")) arg.Format = (FormatClause)ParseFunctionClause();
        if (IsKw("AS") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier) { Next(); arg.Alias = ParseIdentifier(); }
        return Fin(arg, a);
    }

    private void ParseNullTreatment(FunctionCall fc)
    {
        if ((IsKw("IGNORE") || IsKw("RESPECT")) && IsKwAt(1, "NULLS"))
        {
            if (Next().Value == "IGNORE") fc.IgnoreNulls = true; else fc.RespectNulls = true;
            Next();
        }
    }

    // ---- function clauses ----

    private static readonly Dictionary<string, BehaviorEvent> BehaviorEvents = new()
    {
        ["ERROR"] = BehaviorEvent.Error, ["EMPTY"] = BehaviorEvent.Empty, ["MISMATCH"] = BehaviorEvent.Mismatch,
        ["NULL"] = BehaviorEvent.Null, ["MISSING"] = BehaviorEvent.Missing, ["EXISTING"] = BehaviorEvent.Existing,
    };

    private bool AtBehaviorClause()
    {
        if (IsKw("DEFAULT")) return true;
        if (IsKw("EMPTY") && (IsKwAt(1, "ON") || ((IsKwAt(1, "ARRAY") || IsKwAt(1, "OBJECT")) && IsKwAt(2, "ON")))) return true;
        return Cur.Type == TokenType.Identifier &&
               Cur.Value is "ERROR" or "TRUE" or "FALSE" or "NULL" or "IGNORE" or "REMOVE" or "CREATE" or "REPLACE" &&
               IsKwAt(1, "ON") && !(IsKw("NULL") && IsKwAt(2, "NULL"));
    }

    private OnBehaviorClause ParseBehaviorClause()
    {
        var s = Cur;
        var b = new OnBehaviorClause();
        if (AcceptKw("DEFAULT")) { b.Behavior = BehaviorKind.Default; b.Value = ParseExpr(); }
        else if (AcceptKw("EMPTY"))
            b.Behavior = AcceptKw("ARRAY") ? BehaviorKind.EmptyArray : AcceptKw("OBJECT") ? BehaviorKind.EmptyObject : BehaviorKind.Empty;
        else
            b.Behavior = Next().Value switch
            {
                "ERROR" => BehaviorKind.Error, "TRUE" => BehaviorKind.True, "FALSE" => BehaviorKind.False, "NULL" => BehaviorKind.Null,
                "IGNORE" => BehaviorKind.Ignore, "REMOVE" => BehaviorKind.Remove, "CREATE" => BehaviorKind.Create, _ => BehaviorKind.Replace,
            };
        ExpectKw("ON");
        if (IsKw("CONVERSION")) { Next(); ExpectKw("ERROR"); b.Event = BehaviorEvent.ConversionError; }
        else if (Cur.Type == TokenType.Identifier && BehaviorEvents.TryGetValue(Cur.Value, out var ev)) { Next(); b.Event = ev; }
        else throw Fail($"Expected ERROR, EMPTY, MISMATCH, NULL, MISSING, EXISTING or CONVERSION ERROR but found {Describe(Cur)}");
        return Fin(b, s);
    }

    private FunctionClause ParseFunctionClause()
    {
        var s = Cur;
        if (Cur.Type != TokenType.Identifier) throw Fail($"Unexpected {Describe(Cur)} in function call");
        if (AtBehaviorClause()) return ParseBehaviorClause();
        FunctionClause Flag(FunctionFlag f, int words = 1)
        {
            for (int i = 0; i < words; i++) Next();
            return Fin(new FlagClause { Flag = f }, s);
        }
        switch (Cur.Value)
        {
            case "RETURNING":
            {
                Next();
                var rc = new ReturningClause();
                if (AcceptKw("CONTENT")) rc.Content = true; else rc.Type = ParseDataType();
                return Fin(rc, s);
            }
            case "FORMAT":
            {
                Next(); ExpectKw("JSON");
                var fc = new FormatClause { Kind = FormatKind.Json };
                if (AcceptKw("ENCODING")) fc.Encoding = ParseIdentifier();
                return Fin(fc, s);
            }
            case "ABSENT":
                Next(); ExpectKw("ON"); ExpectKw("NULL");
                return Fin(new NullOnClause { Absent = true }, s);
            case "NULL" when IsKwAt(1, "ON") && IsKwAt(2, "NULL"):
                Next(); Next(); Next();
                return Fin(new NullOnClause(), s);
            case "WITH" or "WITHOUT":
            {
                bool with = Cur.Value == "WITH";
                if (IsKwAt(1, "UNIQUE")) return Flag(with ? FunctionFlag.WithUniqueKeys : FunctionFlag.WithoutUniqueKeys, 3);
                Next();
                var w = new WrapperClause { With = with };
                if (AcceptKw("CONDITIONAL")) w.Conditional = true; else if (AcceptKw("UNCONDITIONAL")) w.Unconditional = true;
                w.Array = AcceptKw("ARRAY");
                ExpectKw("WRAPPER");
                return Fin(w, s);
            }
            case "KEEP" or "OMIT":
            {
                bool keep = Next().Value == "KEEP";
                ExpectKw("QUOTES");
                var q = new QuotesClause { Keep = keep };
                if (IsKw("ON") && IsKwAt(1, "SCALAR")) { Next(); Next(); ExpectKw("STRING"); q.OnScalarString = true; }
                return Fin(q, s);
            }
            case "STRICT": return Flag(FunctionFlag.Strict);
            case "LAX": return Flag(FunctionFlag.Lax);
            case "PRETTY": return Flag(FunctionFlag.Pretty);
            case "ASCII": return Flag(FunctionFlag.Ascii);
            case "TRUNCATE": return Flag(FunctionFlag.Truncate);
            case "ORDERED": return Flag(FunctionFlag.Ordered);
            case "WELLFORMED": return Flag(FunctionFlag.Wellformed);
            case "EXISTS": return Flag(FunctionFlag.Exists);
            case "DOCUMENT": return Flag(FunctionFlag.Document);
            case "CONTENT": return Flag(FunctionFlag.Content);
            case "NO" when IsKwAt(1, "INDENT"): return Flag(FunctionFlag.NoIndent, 2);
            case "INDENT":
            {
                var flag = Flag(FunctionFlag.Indent);
                return flag;
            }
            case "SIZE":
            {
                Next(); ExpectOp("=");
                return Fin(new ValueClause { Kind = ValueClauseKind.IndentSize, Value = ParseAdditive() }, s);
            }
            case "HIDE": return Flag(FunctionFlag.HideDefaults, 2);
            case "SHOW": return Flag(FunctionFlag.ShowDefaults, 2);
            case "ALLOW" when IsKwAt(1, "SCALARS"): return Flag(FunctionFlag.AllowScalars, 2);
            case "DISALLOW" when IsKwAt(1, "SCALARS"): return Flag(FunctionFlag.DisallowScalars, 2);
            case "ALLOW" when IsKwAt(1, "ARRAY"): return Flag(FunctionFlag.AllowArray, 2);
            case "DISALLOW" when IsKwAt(1, "ARRAY"): return Flag(FunctionFlag.DisallowArray, 2);
            case "ENCODING": Next(); return Fin(new ValueClause { Kind = ValueClauseKind.Encoding, Value = ParseAdditive() }, s);
            case "VERSION": Next(); return Fin(new ValueClause { Kind = ValueClauseKind.Version, Value = ParseAdditive() }, s);
            case "STANDALONE":
            {
                Next();
                ValueClauseKind k;
                if (AcceptKw("YES")) k = ValueClauseKind.StandaloneYes;
                else { ExpectKw("NO"); k = AcceptKw("VALUE") ? ValueClauseKind.StandaloneNoValue : ValueClauseKind.StandaloneNo; }
                return Fin(new ValueClause { Kind = k }, s);
            }
            case "ON" when IsKwAt(1, "OVERFLOW"):
            {
                Next(); Next();
                var oc = new OverflowClause();
                if (AcceptKw("TRUNCATE"))
                {
                    oc.Truncate = true;
                    if (Cur.Type == TokenType.String) oc.Indicator = ParsePrimary();
                    if (IsKw("WITH") && IsKwAt(1, "COUNT")) { Next(); Next(); oc.WithCount = true; }
                    else if (IsKw("WITHOUT") && IsKwAt(1, "COUNT")) { Next(); Next(); oc.WithCount = false; }
                }
                else ExpectKw("ERROR");
                return Fin(oc, s);
            }
            case "PASSING":
            {
                Next();
                var pc = new PassingClause();
                if (IsKw("BY")) { Next(); ExpectKw("VALUE"); pc.ByValue = true; }
                do pc.Items.Add((ExpressionSelectItem)ParseSelectItem()); while (AcceptOp(","));
                return Fin(pc, s);
            }
            case "USING":
            {
                Next();
                if (Cur.Type == TokenType.Identifier && Cur.Value is "CHAR_CS" or "NCHAR_CS")
                    return Fin(new UsingCharsetClause { Charset = ParseIdentifier() }, s);
                var ul = new UsingListClause();
                do ul.Items.Add(ParseSelectItem()); while (AcceptOp(","));
                return Fin(ul, s);
            }
        }
        throw Fail($"Unexpected {Describe(Cur)} in function call");
    }

    private static readonly Dictionary<string, JsonTransformOp> JsonOps = new()
    {
        ["SET"] = JsonTransformOp.Set, ["INSERT"] = JsonTransformOp.Insert, ["REPLACE"] = JsonTransformOp.Replace,
        ["APPEND"] = JsonTransformOp.Append, ["PREPEND"] = JsonTransformOp.Prepend, ["REMOVE"] = JsonTransformOp.Remove,
        ["KEEP"] = JsonTransformOp.Keep, ["RENAME"] = JsonTransformOp.Rename, ["SORT"] = JsonTransformOp.Sort,
    };

    private void ParseJsonTransformArgs(FunctionCall fc)
    {
        fc.Arguments.Add(ParseArgument());
        while (AcceptOp(","))
        {
            var s = Cur;
            if (Cur.Type != TokenType.Identifier || !JsonOps.TryGetValue(Cur.Value, out var op))
                throw Fail($"Unsupported JSON_TRANSFORM operation {Describe(Cur)}");
            Next();
            var jo = new JsonTransformOperation { Op = op, Path = ParseAdditive() };
            if (op == JsonTransformOp.Rename) { ExpectKw("WITH"); jo.Value = ParseAdditive(); }
            else if (op == JsonTransformOp.Sort) { if (AcceptKw("DESC")) jo.Descending = true; else AcceptKw("ASC"); }
            else if (op is not (JsonTransformOp.Remove or JsonTransformOp.Keep))
            {
                ExpectOp("=");
                jo.Value = ParseAdditive();
                if (IsKw("FORMAT")) jo.Format = (FormatClause)ParseFunctionClause();
            }
            while (AtBehaviorClause() && !IsKw("DEFAULT")) jo.Clauses.Add(ParseBehaviorClause());
            fc.Operations.Add(Fin(jo, s));
        }
    }

    // ---- window specs ----

    private WindowSpec ParseWindowSpec()
    {
        var start = Cur;
        var ws = new WindowSpec();
        if (!IsOp("("))
        {
            ws.Name = ParseIdentifier();
            return Fin(ws, start);
        }
        Next();
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier &&
            !(IsKw("PARTITION") || IsKw("ORDER") || IsKw("ROWS") || IsKw("RANGE") || IsKw("GROUPS")))
            ws.Name = ParseIdentifier();
        if (IsKw("PARTITION"))
        {
            Next(); ExpectKw("BY");
            do ws.PartitionBy.Add(ParseExpr()); while (AcceptOp(","));
        }
        if (IsKw("ORDER"))
        {
            Next(); ExpectKw("BY");
            ws.OrderBy = ParseOrderByList();
        }
        if (IsKw("ROWS") || IsKw("RANGE") || IsKw("GROUPS"))
        {
            var fs = Cur;
            var frame = new WindowFrame { Unit = Next().Value switch { "ROWS" => FrameUnit.Rows, "RANGE" => FrameUnit.Range, _ => FrameUnit.Groups } };
            if (AcceptKw("BETWEEN"))
            {
                frame.StartBound = ParseFrameBound();
                ExpectKw("AND");
                frame.EndBound = ParseFrameBound();
            }
            else frame.StartBound = ParseFrameBound();
            ws.Frame = Fin(frame, fs);
        }
        ExpectOp(")");
        return Fin(ws, start);
    }

    private FrameBound ParseFrameBound()
    {
        var start = Cur;
        var fb = new FrameBound();
        if (AcceptKw("UNBOUNDED"))
        {
            if (AcceptKw("PRECEDING")) fb.Kind = FrameBoundKind.UnboundedPreceding;
            else { ExpectKw("FOLLOWING"); fb.Kind = FrameBoundKind.UnboundedFollowing; }
        }
        else if (AcceptKw("CURRENT")) { ExpectKw("ROW"); fb.Kind = FrameBoundKind.CurrentRow; }
        else
        {
            fb.Offset = ParseAdditive();
            if (AcceptKw("PRECEDING")) fb.Kind = FrameBoundKind.Preceding;
            else { ExpectKw("FOLLOWING"); fb.Kind = FrameBoundKind.Following; }
        }
        return Fin(fb, start);
    }

    // ---- CASE / CAST / EXTRACT / TRIM / INTERVAL ----

    private Expression ParseCase()
    {
        var start = Cur;
        ExpectKw("CASE");
        var ce = new CaseExpression();
        if (!IsKw("WHEN")) ce.Operand = ParseExpr();
        while (IsKw("WHEN"))
        {
            var ws = Next();
            var w = new WhenClause { When = ParseExpr() };
            ExpectKw("THEN");
            w.Then = ParseExpr();
            ce.Whens.Add(Fin(w, ws));
        }
        if (ce.Whens.Count == 0) throw Fail("Expected WHEN in CASE expression");
        if (AcceptKw("ELSE")) ce.Else = ParseExpr();
        ExpectKw("END");
        return Fin(ce, start);
    }

    private Expression ParseCastLike()
    {
        var start = Cur;
        var cast = new CastExpression
        {
            Kind = Next().Value switch
            {
                "XMLCAST" => CastKind.XmlCast, "TREAT" => CastKind.Treat, "VALIDATE_CONVERSION" => CastKind.ValidateConversion,
                "XMLSERIALIZE" => CastKind.XmlSerialize, _ => CastKind.Cast,
            },
        };
        ExpectOp("(");
        if (cast.Kind == CastKind.XmlSerialize && (IsKw("DOCUMENT") || IsKw("CONTENT"))) cast.Clauses.Add(ParseFunctionClause());
        if (IsKw("MULTISET") && IsOpAt(1, "(")) { Next(); cast.Multiset = true; cast.Value = ParsePostfix(); }
        else cast.Value = ParseExpr();
        ExpectKw("AS");
        if (cast.Kind == CastKind.Treat) cast.Ref = AcceptKw("REF");
        cast.Type = ParseDataType();
        while (!IsOp(")") && Cur.Type != TokenType.EOF)
        {
            if (IsKw("DEFAULT")) cast.OnConversionError = ParseBehaviorClause();
            else if (AcceptOp(","))
            {
                cast.Format = ParseExpr();
                if (AcceptOp(",")) cast.NlsParam = ParseExpr();
            }
            else cast.Clauses.Add(ParseFunctionClause());
        }
        ExpectOp(")");
        return Fin(cast, start);
    }

    private static readonly Dictionary<string, ExtractField> ExtractFields = new()
    {
        ["YEAR"] = ExtractField.Year, ["MONTH"] = ExtractField.Month, ["DAY"] = ExtractField.Day, ["HOUR"] = ExtractField.Hour,
        ["MINUTE"] = ExtractField.Minute, ["SECOND"] = ExtractField.Second, ["TIMEZONE_HOUR"] = ExtractField.TimezoneHour,
        ["TIMEZONE_MINUTE"] = ExtractField.TimezoneMinute, ["TIMEZONE_REGION"] = ExtractField.TimezoneRegion,
        ["TIMEZONE_ABBR"] = ExtractField.TimezoneAbbr,
    };

    private Expression ParseExtract()
    {
        var start = Cur;
        Next(); Next();
        if (!ExtractFields.TryGetValue(Cur.Value, out var field)) throw Fail($"Unknown EXTRACT field {Describe(Cur)}");
        Next(); Next(); // field, FROM
        var ex = new ExtractExpression { Field = field, Source = ParseExpr() };
        ExpectOp(")");
        return Fin(ex, start);
    }

    private Expression ParseTrim()
    {
        var start = Cur;
        Next(); Next();
        var tr = new TrimExpression();
        if (IsKw("LEADING") || IsKw("TRAILING") || IsKw("BOTH"))
        {
            tr.Spec = Next().Value switch { "LEADING" => TrimSpec.Leading, "TRAILING" => TrimSpec.Trailing, _ => TrimSpec.Both };
            if (!IsKw("FROM")) tr.Characters = ParseExpr();
            ExpectKw("FROM");
            tr.Source = ParseExpr();
        }
        else
        {
            var first = ParseExpr();
            if (AcceptKw("FROM")) { tr.Characters = first; tr.Source = ParseExpr(); }
            else tr.Source = first;
        }
        ExpectOp(")");
        return Fin(tr, start);
    }

    // ---- data types ----

    private static readonly HashSet<string> TypeWords = new(StringComparer.Ordinal)
    {
        "PRECISION","VARYING","WITH","LOCAL","TIME","ZONE","TO","YEAR","MONTH","DAY","HOUR","MINUTE","SECOND",
        "CHARACTER","CHAR","RAW","WITHOUT","NATIONAL",
    };

    private DataType ParseDataType()
    {
        var start = Cur;
        var dt = new DataType();
        var words = new List<string>();
        var first = ParseIdentifier();
        words.Add(first.Value);
        while (IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier)
        {
            Next();
            words[^1] += "." + ParseIdentifier().Value;
        }
        if (IsOp("%") && (IsKwAt(1, "TYPE") || IsKwAt(1, "ROWTYPE")))
        {
            Next();
            dt.Attribute = Next().Value;
            dt.Name = words[0];
            return Fin(dt, start);
        }
        while (true)
        {
            if (IsOp("("))
            {
                Next();
                do
                {
                    if (IsOp("*")) { var st = Next(); dt.Arguments.Add(Fin(new StarExpression(), st)); }
                    else dt.Arguments.Add(ParseAdditive());
                    if (IsKw("CHAR")) { Next(); dt.Semantics = CharSemantics.Char; }
                    else if (IsKw("BYTE")) { Next(); dt.Semantics = CharSemantics.Byte; }
                } while (AcceptOp(","));
                ExpectOp(")");
            }
            else if (Cur.Type == TokenType.Identifier && TypeWords.Contains(Cur.Value)) words.Add(Next().Value);
            else break;
        }
        dt.Name = string.Join(" ", words);
        return Fin(dt, start);
    }

    private static readonly Dictionary<string, IntervalUnit> IntervalUnits = new()
    {
        ["YEAR"] = IntervalUnit.Year, ["MONTH"] = IntervalUnit.Month, ["DAY"] = IntervalUnit.Day,
        ["HOUR"] = IntervalUnit.Hour, ["MINUTE"] = IntervalUnit.Minute, ["SECOND"] = IntervalUnit.Second,
    };

    private Expression ParseInterval()
    {
        var start = Cur;
        Next(); // INTERVAL
        var il = new IntervalLiteral { Value = ParsePrimary() };
        il.From = ParseIntervalUnit();
        il.FromPrecision = ParseIntervalPrecision();
        if (AcceptKw("TO"))
        {
            il.To = ParseIntervalUnit();
            il.ToPrecision = ParseIntervalPrecision();
        }
        return Fin(il, start);
    }

    private IntervalUnit ParseIntervalUnit()
    {
        if (Cur.Type != TokenType.Identifier || !IntervalUnits.TryGetValue(Cur.Value, out var u))
            throw Fail($"Expected interval unit but found {Describe(Cur)}");
        Next();
        return u;
    }

    private int? ParseIntervalPrecision()
    {
        if (!IsOp("(")) return null;
        Next();
        if (Cur.Type != TokenType.Number) throw Fail($"Expected number but found {Describe(Cur)}");
        var v = int.Parse(Next().Text);
        ExpectOp(")");
        return v;
    }
}
