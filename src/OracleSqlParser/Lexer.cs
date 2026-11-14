using System.Text;

namespace OracleSqlParser;

public enum TokenType { Identifier, QuotedIdentifier, Number, String, Bind, Substitution, Hint, Operator, EOF }

public enum TriviaKind { Whitespace, LineComment, BlockComment }

/// <summary>
/// Source text that is not a token: whitespace or a comment. <see cref="Start"/> and <see cref="End"/> are offsets into
/// the source; <see cref="Text"/> is the source substring.
/// </summary>
public readonly record struct Trivia(TriviaKind Kind, string Text, int Start, int End);

public readonly record struct Token(TokenType Type, string Text, int Start, int End, int Line, int Column)
{
    /// <summary>Normalized value: unquoted identifiers upper-cased, quotes/escapes stripped.</summary>
    public string Value { get; init; } = Text;

    /// <summary>
    /// Whitespace and comments between the previous token and this one, in source order. Optimizer hints are
    /// tokens, not trivia. Concatenating every token's trivia and <c>source[Start..End]</c> reproduces the source.
    /// </summary>
    public IReadOnlyList<Trivia> LeadingTrivia { get; init; } = [];

    /// <summary>Index of this token in <c>ParseResult.Tokens</c>, or -1 when the token is not part of one.</summary>
    internal int TokenIndex { get; init; } = -1;
}

/// <summary>
/// Tokenizer for Oracle SQL. Comments are skipped except optimizer hints (/*+ ... */).
/// Lexical errors (unterminated string, quoted identifier, comment) are recorded in <see cref="Errors"/> and
/// tokenising continues: an unterminated string or quoted identifier ends at end-of-line (followed by a
/// synthetic ';' so the damaged statement is delimited), an unterminated comment ends at end-of-input.
/// </summary>
public sealed class Lexer
{
    private readonly string _s;
    private int _i, _line = 1, _lineStart;
    private bool _pendingRest;
    private bool _javaBody;
    private readonly List<string> _words = [];
    private bool _pendingSemi;
    private readonly List<Trivia> _trivia = [];
    // Substitution variables: '&' (or the character set by SQL*Plus "SET DEFINE <char>") starts one while _define is on.
    private bool _define;
    private char _prefix = '&';
    private int _setState; // SQL*Plus SET DEFINE detection: 1 = saw SET at line start, 2 = saw SET DEFINE
    private Token _prev; // the previous token returned by Next(), for context-sensitive ':'

    /// <summary>Lexical errors recorded so far (the lexer recovers and keeps going).</summary>
    public List<ParseError> Errors { get; } = [];

    /// <param name="sql">The text to tokenise.</param>
    /// <param name="substitutionEnabled">False when substitution variables are off from the start (<see cref="ParseOptions.SubstitutionEnabled"/>).</param>
    public Lexer(string sql, bool substitutionEnabled = true)
    {
        _s = sql;
        _define = substitutionEnabled;
    }

    /// <summary>Tokenises the whole input; throws <see cref="ParseException"/> for the first lexical error.</summary>
    public static List<Token> Tokenize(string sql, bool substitutionEnabled = true)
    {
        var lx = new Lexer(sql, substitutionEnabled);
        var list = new List<Token>();
        while (true)
        {
            var t = lx.Next();
            list.Add(t);
            if (lx.Errors.Count > 0) throw new ParseException(lx.Errors[0]);
            if (t.Type == TokenType.EOF) return list;
        }
    }

    private char Cur => _i < _s.Length ? _s[_i] : '\0';
    private char Peek(int n = 1) => _i + n < _s.Length ? _s[_i + n] : '\0';

    private void Error(string msg, int at, int line, int col) =>
        Errors.Add(new ParseError(msg, at, line, col, Math.Max(0, _i - at), ParseErrorCodes.UnterminatedLiteral));

    private void Advance()
    {
        if (_s[_i] == '\n') { _line++; _lineStart = _i + 1; }
        _i++;
    }

    private Token Make(TokenType type, int start, int line, int col, string? value = null)
    {
        var text = _s.Substring(start, _i - start);
        return new Token(type, text, start, _i, line, col) { Value = value ?? text };
    }

    /// <summary>Returns the next token; whitespace and comments skipped before it are attached as its <see cref="Token.LeadingTrivia"/>.</summary>
    public Token Next()
    {
        var t = NextCore();
        ObserveSetDefine(t);
        if (_trivia.Count > 0)
        {
            t = t with { LeadingTrivia = _trivia.ToArray() };
            _trivia.Clear();
        }
        _prev = t;
        return t;
    }

    private Token NextCore()
    {
        if (_pendingSemi)
        {
            _pendingSemi = false;
            return new Token(TokenType.Operator, ";", _i, _i, _line, _i - _lineStart + 1);
        }
        if (_javaBody)
        {
            _javaBody = false;
            _words.Clear();
            ScanWhitespace(stopAtNewline: false);
            int js = _i, jl = _line, jc = _i - _lineStart + 1;
            while (_i < _s.Length)
            {
                bool atLineStart = _s.AsSpan(_lineStart, _i - _lineStart).Trim().IsEmpty;
                if (atLineStart && Cur == '/' && (_i + 1 >= _s.Length || _s.AsSpan(_i + 1).TrimStart(' ').StartsWith("\n") || _s.AsSpan(_i + 1).Trim().IsEmpty)) break;
                Advance();
            }
            return Make(TokenType.String, js, jl, jc, _s.Substring(js, _i - js).TrimEnd());
        }
        if (_pendingRest)
        {
            _pendingRest = false;
            ScanWhitespace(stopAtNewline: true);
            if (_i < _s.Length && Cur != '\n')
            {
                int rs = _i, rl = _line, rc = _i - _lineStart + 1;
                while (_i < _s.Length && Cur != '\n') Advance();
                return Make(TokenType.String, rs, rl, rc, _s.Substring(rs, _i - rs).TrimEnd());
            }
        }
        SkipTrivia(out var hint);
        if (_pendingSemi) { _pendingSemi = false; return new Token(TokenType.Operator, ";", _i, _i, _line, _i - _lineStart + 1); }
        if (hint is { } h) return h;
        int start = _i, line = _line, col = _i - _lineStart + 1;
        if (_i >= _s.Length) return new Token(TokenType.EOF, "", _i, _i, line, col);
        char c = Cur;

        // q'[...]' / nq'[...]' alternative quoting, n'...' national strings
        if ((c is 'q' or 'Q') && Peek() == '\'' ) return QuoteString(start, line, col, 1);
        if ((c is 'n' or 'N') && (Peek() is 'q' or 'Q') && Peek(2) == '\'') return QuoteString(start, line, col, 2);
        if ((c is 'n' or 'N') && Peek() == '\'') { Advance(); return PlainString(start, line, col); }

        if (char.IsLetter(c) || c == '_')
        {
            bool lineStart = _s.AsSpan(_lineStart, _i - _lineStart).Trim().IsEmpty;
            while (char.IsLetterOrDigit(Cur) || Cur is '_' or '$' or '#') Advance();
            var text = _s.Substring(start, _i - start);
            var up = text.ToUpperInvariant();
            if (lineStart && up is "REM" or "REMARK" or "PROMPT" && (Cur == '\n' || Cur == '\r' || Cur == ' ' || Cur == '\t' || _i >= _s.Length))
            {
                _pendingRest = true;
            }
            _words.Add(up);
            if (up is "AS" or "IS" && _words.Count > 3 && _words[0] == "CREATE")
            {
                int ji = _words.IndexOf("JAVA");
                if (ji >= 0 && ji + 1 < _words.Count && _words[ji + 1] == "SOURCE" && _words.IndexOf("NAMED") > ji) _javaBody = true;
            }
            return Make(TokenType.Identifier, start, line, col, up);
        }
        if (c == '"')
        {
            Advance();
            var sb = new StringBuilder();
            while (true)
            {
                if (_i >= _s.Length || Cur == '\n')
                {
                    Error("Unterminated quoted identifier", start, line, col);
                    _pendingSemi = true;
                    return Make(TokenType.QuotedIdentifier, start, line, col, sb.ToString().TrimEnd('\r'));
                }
                if (Cur == '"') { if (Peek() == '"') { sb.Append('"'); Advance(); Advance(); continue; } Advance(); break; }
                sb.Append(Cur); Advance();
            }
            return Make(TokenType.QuotedIdentifier, start, line, col, sb.ToString());
        }
        if (c == '\'') return PlainString(start, line, col);
        if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek()))) return Number(start, line, col);
        if (c == ':')
        {
            if (Peek() == '=') { Advance(); Advance(); return Make(TokenType.Operator, start, line, col); }
            // 'key':value (JSON_OBJECT) and (expr):value: a colon right after a string, quoted name or ')' is the key/value
            // separator, never the start of a bind. Identifiers are excluded: INTO :x and RETURN :x are real binds.
            if (_prev.Type is TokenType.String or TokenType.QuotedIdentifier || _prev is { Type: TokenType.Operator, Text: ")" })
            {
                Advance();
                return Make(TokenType.Operator, start, line, col);
            }
            Advance();
            if (Cur == '"') { /* :"name" */ Advance(); while (_i < _s.Length && Cur != '"') Advance(); if (_i < _s.Length) Advance(); }
            else while (char.IsLetterOrDigit(Cur) || Cur is '_' or '$' or '#') Advance();
            return Make(TokenType.Bind, start, line, col);
        }

        // PL/SQL conditional compilation: $IF/$THEN/$ELSIF/$ELSE/$END/$ERROR directives, and $$name inquiry directives
        if (c == '$' && Peek() == '$' && (char.IsLetter(Peek(2)) || Peek(2) == '_'))
        {
            Advance(); Advance();
            while (char.IsLetterOrDigit(Cur) || Cur is '_' or '$' or '#') Advance();
            return Make(TokenType.Identifier, start, line, col, _s.Substring(start, _i - start).ToUpperInvariant());
        }
        if (c == '$' && char.IsLetter(Peek()))
        {
            int e = start + 1;
            while (e < _s.Length && char.IsLetterOrDigit(_s[e])) e++;
            var word = _s.Substring(start + 1, e - start - 1).ToUpperInvariant();
            if (word is "IF" or "THEN" or "ELSIF" or "ELSE" or "END" or "ERROR")
            {
                while (_i < e) Advance();
                return Make(TokenType.Identifier, start, line, col, "$" + word);
            }
        }

        string[] two = ["||", "**", "<>", "!=", "^=", "~=", "<=", ">=", "=>", "..", "->", "<<", ">>"];
        foreach (var op in two)
            if (c == op[0] && Peek() == op[1]) { Advance(); Advance(); return Make(TokenType.Operator, start, line, col); }
        if (_define && c == _prefix && (char.IsLetterOrDigit(Peek()) || Peek() == _prefix || Peek() == '_'))
        {
            Advance();
            bool persistent = Cur == _prefix;
            if (persistent) Advance();
            int ns = _i;
            while (char.IsLetterOrDigit(Cur) || Cur is '_' or '$' or '#') Advance();
            if (Cur == '.' ) Advance(); // optional terminator
            var nm = _s.Substring(ns, _i - ns).TrimEnd('.');
            return Make(TokenType.Substitution, start, line, col, (persistent ? "&" : "") + nm);
        }
        if ("()[],;.+-*/=<>@%|&!^~?{}$".Contains(c))
        {
            Advance();
            if (c == ';') _words.Clear();
            return Make(TokenType.Operator, start, line, col);
        }
        Advance();
        return Make(TokenType.Operator, start, line, col); // unknown character: let the parser decide (SQL*Plus lines may contain any text)
    }

    /// <summary>
    /// Recognises a SQL*Plus <c>SET DEFINE ON | OFF | &lt;char&gt;</c> (also <c>DEF</c>) that is the first thing on its
    /// line and is followed only by the end of the line or ';'. Later tokens are lexed with the new setting.
    /// </summary>
    private void ObserveSetDefine(Token t)
    {
        switch (_setState)
        {
            case 1 when t.Type == TokenType.Identifier && t.Value is "DEFINE" or "DEF":
                _setState = 2;
                return;
            case 2:
                _setState = 0;
                if (!IsRestOfLineBlank(t.End)) return;
                if (t.Type == TokenType.Identifier && t.Value == "OFF") _define = false;
                else if (t.Type == TokenType.Identifier && t.Value == "ON") { _define = true; _prefix = '&'; }
                else if (t.Type == TokenType.Operator && t.Text.Length == 1 && t.Text != ";") { _define = true; _prefix = t.Text[0]; }
                return;
        }
        _setState = t.Type == TokenType.Identifier && t.Value == "SET" && IsAtLineStart(t.Start) ? 1 : 0;
    }

    private bool IsAtLineStart(int start)
    {
        int i = start;
        while (i > 0 && _s[i - 1] is ' ' or '\t') i--;
        return i == 0 || _s[i - 1] == '\n';
    }

    private bool IsRestOfLineBlank(int end)
    {
        int i = end;
        while (i < _s.Length && _s[i] is ' ' or '\t' or '\r') i++;
        return i >= _s.Length || _s[i] is '\n' or ';' || (_s[i] == '-' && i + 1 < _s.Length && _s[i + 1] == '-');
    }

    private void AddTrivia(TriviaKind kind, int start) =>
        _trivia.Add(new Trivia(kind, _s.Substring(start, _i - start), start, _i));

    private void ScanWhitespace(bool stopAtNewline)
    {
        int ws = _i;
        while (_i < _s.Length && char.IsWhiteSpace(Cur) && !(stopAtNewline && Cur == '\n')) Advance();
        if (_i > ws) AddTrivia(TriviaKind.Whitespace, ws);
    }

    private void SkipTrivia(out Token? hint)
    {
        hint = null;
        while (_i < _s.Length)
        {
            char c = Cur;
            if (char.IsWhiteSpace(c)) { ScanWhitespace(stopAtNewline: false); continue; }
            if (c == '-' && Peek() == '-')
            {
                int cs = _i;
                while (_i < _s.Length && Cur != '\n') Advance();
                AddTrivia(TriviaKind.LineComment, cs);
                continue;
            }
            if (c == '/' && Peek() == '*')
            {
                int start = _i, line = _line, col = _i - _lineStart + 1;
                bool isHint = Peek(2) == '+';
                Advance(); Advance();
                while (true)
                {
                    if (_i >= _s.Length) { Error("Unterminated comment", start, line, col); AddTrivia(TriviaKind.BlockComment, start); return; }
                    if (Cur == '*' && Peek() == '/') { Advance(); Advance(); break; }
                    Advance();
                }
                if (isHint)
                {
                    var text = _s.Substring(start, _i - start);
                    hint = new Token(TokenType.Hint, text, start, _i, line, col) { Value = text[3..^2].Trim() };
                    return;
                }
                AddTrivia(TriviaKind.BlockComment, start);
                continue;
            }
            break;
        }
    }

    private Token PlainString(int start, int line, int col)
    {
        Advance(); // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (_i >= _s.Length || Cur == '\n')
            {
                Error("Unterminated string literal", start, line, col);
                _pendingSemi = true;
                return Make(TokenType.String, start, line, col, sb.ToString().TrimEnd('\r'));
            }
            if (Cur == '\'') { if (Peek() == '\'') { sb.Append('\''); Advance(); Advance(); continue; } Advance(); break; }
            sb.Append(Cur); Advance();
        }
        return Make(TokenType.String, start, line, col, sb.ToString());
    }

    private Token QuoteString(int start, int line, int col, int prefixLen)
    {
        for (int k = 0; k < prefixLen; k++) Advance();
        Advance(); // opening '
        if (_i >= _s.Length) { Error("Unterminated q-quote literal", start, line, col); return Make(TokenType.String, start, line, col, ""); }
        char open = Cur; Advance();
        char close = open switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', _ => open };
        var sb = new StringBuilder();
        while (true)
        {
            if (_i >= _s.Length) { Error("Unterminated q-quote literal", start, line, col); return Make(TokenType.String, start, line, col, sb.ToString()); }
            if (Cur == close && Peek() == '\'') { Advance(); Advance(); break; }
            sb.Append(Cur); Advance();
        }
        return Make(TokenType.String, start, line, col, sb.ToString());
    }

    private Token Number(int start, int line, int col)
    {
        while (char.IsAsciiDigit(Cur)) Advance();
        if (Cur == '.' && Peek() != '.') { Advance(); while (char.IsAsciiDigit(Cur)) Advance(); }
        if (Cur is 'e' or 'E' && (char.IsAsciiDigit(Peek()) || (Peek() is '+' or '-' && char.IsAsciiDigit(Peek(2)))))
        {
            Advance(); if (Cur is '+' or '-') Advance();
            while (char.IsAsciiDigit(Cur)) Advance();
        }
        if (Cur is 'f' or 'F' or 'd' or 'D' && !(char.IsLetterOrDigit(Peek()) || Peek() == '_')) Advance();
        else if (Cur is 'K' or 'k' or 'M' or 'm' or 'G' or 'g' or 'T' or 't' or 'P' or 'p' or 'E' or 'e' && !(char.IsLetterOrDigit(Peek()) || Peek() == '_')) Advance(); // size suffix: 64K, 1M
        return Make(TokenType.Number, start, line, col);
    }
}
