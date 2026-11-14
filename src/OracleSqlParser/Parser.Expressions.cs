namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- expressions ----

    private Expression ParseExpr()
    {
        Enter();
        var e = ParseOr();
        Leave();
        return e;
    }

    private Expression ParseOr()
    {
        var start = Cur;
        var left = ParseAnd();
        int prog107 = -1;
        while (IsKw("OR") && Progress(ref prog107))
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
        int prog108 = -1;
        while (IsKw("AND") && Progress(ref prog108))
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
            Enter();
            var operand = ParseNot();
            Leave();
            return Fin(new UnaryExpression { Operator = "NOT", Operand = operand }, start);
        }
        return ParseComparison();
    }

    private static readonly HashSet<string> ComparisonOps = ["=", "<>", "!=", "^=", "~=", "<", ">", "<=", ">="];

    private Expression ParseComparison()
    {
        var start = Cur;
        var left = ParseAdditive();
        int prog5 = -1;
        while (Progress(ref prog5))
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
        else if (AcceptKw("TRUE")) ie.Test = IsTest.True; // Oracle 23ai boolean predicates
        else if (AcceptKw("FALSE")) ie.Test = IsTest.False;
        else if (IsKw("A") && IsKwAt(1, "SET")) { Next(); Next(); ie.Test = IsTest.ASet; }
        else if (AcceptKw("JSON"))
        {
            ie.Test = IsTest.Json;
            int prog109 = -1;
            while ((IsKw("FORMAT") || IsKw("STRICT") || IsKw("LAX") || IsKw("ALLOW") || IsKw("DISALLOW") ||
                   ((IsKw("WITH") || IsKw("WITHOUT")) && IsKwAt(1, "UNIQUE"))) && Progress(ref prog109))
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
        else throw Fail($"Expected NULL, NAN, INFINITE, EMPTY, PRESENT, A SET, JSON, OF, DANGLING, TRUE or FALSE after IS but found {Describe(Cur)}");
        return Fin(ie, start);
    }

    private Expression ParseAdditive()
    {
        var start = Cur;
        var left = ParseMultiplicative();
        int prog6 = -1;
        while (Progress(ref prog6))
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
            Enter();
            var operand = ParseUnary();
            Leave();
            return Fin(new UnaryExpression { Operator = op, Operand = operand }, start);
        }
        if (IsKw("PRIOR") || IsKw("CONNECT_BY_ROOT"))
        {
            var op = Next().Value;
            Enter();
            var operand = ParseUnary();
            Leave();
            return Fin(new UnaryExpression { Operator = op, Operand = operand }, start);
        }
        return ParsePower();
    }

    private Expression ParsePower()
    {
        var start = Cur;
        var left = ParsePostfix();
        int prog110 = -1;
        while (IsOp("**") && Progress(ref prog110))
        {
            if (!_plsql) throw Fail("'**' is a PL/SQL operator and is not valid in SQL (use POWER)");
            Next();
            left = Fin(new BinaryExpression { Left = left, Operator = "**", Right = ParsePostfix() }, start);
        }
        return left;
    }

    private Expression ParsePostfix()
    {
        var start = Cur;
        var e = ParsePrimary();
        int prog7 = -1;
        while (Progress(ref prog7))
        {
            if (IsOp("[")) { if (_plsql) throw Fail("Array steps are not valid in PL/SQL expressions (PLS-00103)"); e = ParseSubscripts(e, start); }
            else if (e is CellReference or ChainedCall && IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier) e = ParseMemberChain(e, start);
            else if (e is FunctionCall or CellReference or ChainedCall && IsOp("(")) e = ParseChainedCall(e, start);
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
            else if (e is ParenthesizedExpression && AtIntervalExpression()) e = ParseIntervalExpression(e, start);
            else break;
        }
        return e;
    }

    /// <summary>For a q'[..]' / nq'[..]' token, the opening delimiter character; otherwise null.</summary>
    private static char? QuoteDelimiterOf(string text)
    {
        int q = text.IndexOf('\'');
        if (q <= 0 || q + 1 >= text.Length) return null;
        return text.AsSpan(0, q).IndexOfAny('q', 'Q') >= 0 ? text[q + 1] : null;
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
                return Fin(new Literal { Kind = LiteralKind.String, Value = start.Value, National = start.Text[0] is 'n' or 'N', QuoteDelimiter = QuoteDelimiterOf(start.Text) }, start);
            case TokenType.Substitution:
                Next();
                return Fin(new SubstitutionVariable { Name = start.Value.TrimStart('&'), Persistent = start.Value.StartsWith('&'), Prefix = start.Text[0] }, start);
            case TokenType.Bind:
            {
                Next();
                var name = start.Text[1..].Trim('"');
                int prog111 = -1;
                while (IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier && Progress(ref prog111))
                {
                    Next();
                    name += "." + Next().Text;
                }
                return Fin(new BindVariable { Name = name, Quoted = start.Text.Length > 1 && start.Text[1] == '"' }, start);
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
                        int prog112 = -1;
                        while (AcceptOp(",") && Progress(ref prog112)) list.Items.Add(ParseExpr());
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
                    if (e is FunctionCall f) { f.New = true; f.Start = start.Start; f.FirstTokenIndex = start.TokenIndex; return f; }
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
        if (Cur.Type == TokenType.Identifier && Cur.Value.StartsWith("$$", StringComparison.Ordinal))
            return Fin(new InquiryDirective { Name = ParseIdentifier() }, start);
        var parts = new List<Identifier> { ParseIdentifier() };
        int prog113 = -1;
        while (IsOp(".") && Progress(ref prog113))
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

        return ParseMemberChain(result, start);
    }

    /// <summary>
    /// Continues a postfix chain of '.field' and '.method(args)' steps on a call, collection element or member access:
    /// f(x).field, coll(i).field(j), t.c.a[0].b (JSON dot-notation after an array step).
    /// </summary>
    /// <summary>Parses the argument list of a call whose callee is itself a call or subscript: f(x)(y).</summary>
    private Expression ParseChainedCall(Expression target, Token start)
    {
        Next(); // (
        var cc = new ChainedCall { Target = target };
        if (!IsOp(")")) do cc.Arguments.Add(ParseArgument()); while (AcceptOp(","));
        ExpectOp(")");
        return Fin(cc, start);
    }

    private Expression ParseMemberChain(Expression result, Token start)
    {
        int prog114 = -1;
        while (result is FunctionCall or MethodCall or MemberAccess or CellReference or ChainedCall && IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier && Progress(ref prog114))
        {
            Next();
            var m = ParseIdentifier();
            if (IsOp("("))
            {
                var call = ParseFunctionCall([m], Cur);
                result = Fin(new MethodCall { Target = result, Name = m, Arguments = call.Arguments }, start);
            }
            else result = Fin(new MemberAccess { Target = result, Member = m }, start);
        }
        return result;
    }

    // ---- function calls ----

    private static readonly HashSet<string> LeadingArgWords = new(StringComparer.Ordinal)
        { "NAME", "EVALNAME", "DOCUMENT", "CONTENT", "KEY" };

    private FunctionCall ParseFunctionCall(List<Identifier> name, Token start)
    {
        using var _ = SetJsonObjectArgs(name is [{ Value: "JSON_OBJECT" or "JSON_OBJECTAGG" }]);
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
                        arg.LastTokenIndex = Prev.TokenIndex;
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
            int prog115 = -1;
            while (!IsOp(")") && Cur.Type != TokenType.EOF && Progress(ref prog115))
                fc.Clauses.Add(ParseFunctionClause());
        }
        ExpectOp(")");

        int prog8 = -1;
        while (Progress(ref prog8))
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
        // PL/SQL qualified expression choices (OTHERS and the iterator cannot be a FunctionCall argument name)
        if (IsKw("OTHERS") && Cur.Type == TokenType.Identifier && IsOpAt(1, "=>"))
        {
            Next(); Next();
            arg.Value = Fin(new QualifiedExpression { Others = true, Value = ParseExpr() }, a);
            return Fin(arg, a);
        }
        if (IsKw("FOR") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier && IsKwAt(2, "IN"))
        {
            var fs = Next();
            var it = new IndexIterator { Variable = ParseIdentifier() };
            ExpectKw("IN");
            it.Low = ParseAdditive();
            ExpectOp("..");
            it.High = ParseAdditive();
            ExpectOp("=>");
            arg.Value = Fin(new QualifiedExpression { Iterator = Fin(it, fs), Value = ParseExpr() }, a);
            return Fin(arg, a);
        }
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
        if (IsOp("=>") && arg.Name == null)
        {
            // index => value with a non-name index: a PL/SQL qualified expression
            Next();
            arg.Value = Fin(new QualifiedExpression { Choice = arg.Value, Value = ParseExpr() }, a);
            return Fin(arg, a);
        }
        if (AcceptKw("VALUE")) arg.MappedValue = ParseExpr();
        else if (Cur.Text == ":" && Cur.Type is TokenType.Operator or TokenType.Bind) { Next(); arg.MappedValue = ParseExpr(); } // 'key' : value
        else if (_jsonObjectArgs && Cur.Type == TokenType.Bind && Cur.Text.Length > 1 && char.IsDigit(Cur.Text[1]))
        {
            // 'key':1 with no space lexes ':1' as a bind; in this position it is a colon and a number
            var bt = Next();
            arg.MappedValue = Fin(new Literal { Kind = LiteralKind.Number, Value = bt.Text[1..] }, bt);
        }
        if (IsKw("FORMAT") && IsKwAt(1, "JSON")) arg.Format = (FormatClause)ParseFunctionClause();
        if (IsKw("AS") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier) { Next(); arg.Alias = ParseIdentifier(); }
        return Fin(arg, a);
    }

    private void ParseNullTreatment(FunctionCall fc)
    {
        if (IsKw("IGNORE") || IsKw("RESPECT"))
        {
            bool ignore = Next().Value == "IGNORE";
            ExpectKw("NULLS");
            if (ignore) fc.IgnoreNulls = true; else fc.RespectNulls = true;
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
                do pc.Items.Add(ParseExpressionSelectItem()); while (AcceptOp(","));
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
        int prog116 = -1;
        while (AcceptOp(",") && Progress(ref prog116))
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
            int prog117 = -1;
            while (AtBehaviorClause() && !IsKw("DEFAULT") && Progress(ref prog117)) jo.Clauses.Add(ParseBehaviorClause());
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
        int prog118 = -1;
        while (IsKw("WHEN") && Progress(ref prog118))
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
        int prog119 = -1;
        while (!IsOp(")") && Cur.Type != TokenType.EOF && Progress(ref prog119))
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
        int prog120 = -1;
        while (IsOp(".") && PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier && Progress(ref prog120))
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
        int prog9 = -1;
        while (Progress(ref prog9))
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

    /// <summary>
    /// True at <c>DAY [(p)] TO</c> or <c>YEAR [(p)] TO</c>. DAY and YEAR are not reserved, so the lookahead requires
    /// the TO that makes this an interval suffix; otherwise they remain an alias or a name.
    /// </summary>
    private bool AtIntervalExpression()
    {
        if (!(IsKw("DAY") || IsKw("YEAR"))) return false;
        if (IsKwAt(1, "TO")) return true;
        return IsOpAt(1, "(") && PeekTok(2).Type == TokenType.Number && IsOpAt(3, ")") && IsKwAt(4, "TO");
    }

    /// <summary>Parses the suffix of <c>(a - b) DAY(p) TO SECOND(fs)</c> or <c>(a - b) YEAR(p) TO MONTH</c>.</summary>
    private Expression ParseIntervalExpression(Expression value, Token start)
    {
        var ie = new IntervalExpression { Value = value };
        ie.From = ParseIntervalUnit();
        ie.FromPrecision = ParseIntervalPrecision();
        ExpectKw("TO");
        ie.To = ParseIntervalUnit();
        bool valid = ie.From == IntervalUnit.Day ? ie.To == IntervalUnit.Second : ie.From == IntervalUnit.Year && ie.To == IntervalUnit.Month;
        if (!valid) throw Fail("Interval expressions are DAY TO SECOND or YEAR TO MONTH");
        ie.ToPrecision = ParseIntervalPrecision();
        return Fin(ie, start);
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
        var v = ParseSmallInt();
        ExpectOp(")");
        return v;
    }

    /// <summary>Consumes a Number token that must be a non-negative integer fitting in an int.</summary>
    private int ParseSmallInt()
    {
        if (Cur.Type != TokenType.Number) throw Fail($"Expected integer but found {Describe(Cur)}");
        if (!int.TryParse(Cur.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v))
            throw Fail($"Expected a small integer but found '{Cur.Text}'");
        Next();
        return v;
    }

    /// <summary>A select-list style item where '*' is not allowed (PASSING, MEASURES, MODEL columns, ...).</summary>
    private ExpressionSelectItem ParseExpressionSelectItem()
    {
        var s = Cur;
        var item = ParseSelectItem();
        if (item is ExpressionSelectItem e) return e;
        throw new ParseException(new ParseError("'*' is not allowed here", s.Start, s.Line, s.Column));
    }
}
