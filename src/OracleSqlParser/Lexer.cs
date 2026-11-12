using System.Text;

namespace OracleSqlParser;

public enum TokenType { Identifier, QuotedIdentifier, Number, String, Bind, Substitution, Hint, Operator, EOF }

public readonly record struct Token(TokenType Type, string Text, int Start, int End, int Line, int Column)
{
    /// <summary>Normalized value: unquoted identifiers upper-cased, quotes/escapes stripped.</summary>
    public string Value { get; init; } = Text;
}

public sealed class ParseError(string message, int offset, int line, int column)
{
    public string Message { get; } = message;
    public int Offset { get; } = offset;
    public int Line { get; } = line;
    public int Column { get; } = column;
    public override string ToString() => $"({Line}:{Column}) {Message}";
}

public sealed class ParseException(ParseError error) : Exception(error.ToString())
{
    public ParseError Error { get; } = error;
}

/// <summary>Tokenizer for Oracle SQL. Comments are skipped except optimizer hints (/*+ ... */).</summary>
public sealed class Lexer
{
    private readonly string _s;
    private int _i, _line = 1, _lineStart;
    private bool _pendingRest;
    private bool _javaBody;
    private readonly List<string> _words = [];

    public Lexer(string sql) => _s = sql;

    public static List<Token> Tokenize(string sql)
    {
        var lx = new Lexer(sql);
        var list = new List<Token>();
        while (true)
        {
            var t = lx.Next();
            list.Add(t);
            if (t.Type == TokenType.EOF) return list;
        }
    }

    private char Cur => _i < _s.Length ? _s[_i] : '\0';
    private char Peek(int n = 1) => _i + n < _s.Length ? _s[_i + n] : '\0';

    private ParseException Error(string msg, int at) => new(new ParseError(msg, at, _line, at - _lineStart + 1));

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

    public Token Next()
    {
        if (_javaBody)
        {
            _javaBody = false;
            _words.Clear();
            while (_i < _s.Length && char.IsWhiteSpace(Cur)) Advance();
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
            while (_i < _s.Length && Cur != '\n' && char.IsWhiteSpace(Cur)) Advance();
            if (_i < _s.Length && Cur != '\n')
            {
                int rs = _i, rl = _line, rc = _i - _lineStart + 1;
                while (_i < _s.Length && Cur != '\n') Advance();
                return Make(TokenType.String, rs, rl, rc, _s.Substring(rs, _i - rs).TrimEnd());
            }
        }
        SkipTrivia(out var hint);
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
                if (_i >= _s.Length) throw Error("Unterminated quoted identifier", start);
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
            Advance();
            if (Cur == '"') { /* :"name" */ Advance(); while (_i < _s.Length && Cur != '"') Advance(); if (_i < _s.Length) Advance(); }
            else while (char.IsLetterOrDigit(Cur) || Cur is '_' or '$' or '#') Advance();
            return Make(TokenType.Bind, start, line, col);
        }

        string[] two = ["||", "**", "<>", "!=", "^=", "~=", "<=", ">=", "=>", "..", "->", "<<", ">>"];
        foreach (var op in two)
            if (c == op[0] && Peek() == op[1]) { Advance(); Advance(); return Make(TokenType.Operator, start, line, col); }
        if (c == '&' && (char.IsLetterOrDigit(Peek()) || Peek() == '&' || Peek() == '_'))
        {
            Advance();
            bool persistent = Cur == '&';
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

    private void SkipTrivia(out Token? hint)
    {
        hint = null;
        while (_i < _s.Length)
        {
            char c = Cur;
            if (char.IsWhiteSpace(c)) { Advance(); continue; }
            if (c == '-' && Peek() == '-')
            {
                while (_i < _s.Length && Cur != '\n') Advance();
                continue;
            }
            if (c == '/' && Peek() == '*')
            {
                int start = _i, line = _line, col = _i - _lineStart + 1;
                bool isHint = Peek(2) == '+';
                Advance(); Advance();
                while (true)
                {
                    if (_i >= _s.Length) throw Error("Unterminated comment", start);
                    if (Cur == '*' && Peek() == '/') { Advance(); Advance(); break; }
                    Advance();
                }
                if (isHint)
                {
                    var text = _s.Substring(start, _i - start);
                    hint = new Token(TokenType.Hint, text, start, _i, line, col) { Value = text[3..^2].Trim() };
                    return;
                }
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
            if (_i >= _s.Length) throw Error("Unterminated string literal", start);
            if (Cur == '\'') { if (Peek() == '\'') { sb.Append('\''); Advance(); Advance(); continue; } Advance(); break; }
            sb.Append(Cur); Advance();
        }
        return Make(TokenType.String, start, line, col, sb.ToString());
    }

    private Token QuoteString(int start, int line, int col, int prefixLen)
    {
        for (int k = 0; k < prefixLen; k++) Advance();
        Advance(); // opening '
        if (_i >= _s.Length) throw Error("Unterminated q-quote literal", start);
        char open = Cur; Advance();
        char close = open switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', _ => open };
        var sb = new StringBuilder();
        while (true)
        {
            if (_i >= _s.Length) throw Error("Unterminated q-quote literal", start);
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
