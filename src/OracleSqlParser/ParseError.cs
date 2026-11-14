namespace OracleSqlParser;

/// <summary>
/// Stable diagnostic codes carried by <see cref="ParseError.Code"/>. Codes never change meaning between releases;
/// <see cref="ParseError.Message"/> wording may. Match on the code, not the message, when handling errors.
/// </summary>
public static class ParseErrorCodes
{
    /// <summary>PSP0001: a token that cannot start or continue the construct being parsed (for example "Unexpected 'FROM'").</summary>
    public const string UnexpectedToken = "PSP0001";

    /// <summary>PSP0002: a specific token, keyword or description was required; <see cref="ParseError.Expected"/> lists the alternatives.</summary>
    public const string ExpectedToken = "PSP0002";

    /// <summary>PSP0003: a string literal, quoted identifier, q-quote literal or block comment is not closed.</summary>
    public const string UnterminatedLiteral = "PSP0003";

    /// <summary>PSP0004: nesting exceeds <see cref="ParseOptions.MaxNestingDepth"/>, or the stack is nearly exhausted.</summary>
    public const string NestingTooDeep = "PSP0004";

    /// <summary>PSP0005: the construct is valid Oracle but needs a newer release than <see cref="ParseOptions.OracleVersion"/>.</summary>
    public const string VersionGated = "PSP0005";

    /// <summary>PSP0006: the construct is recognised but breaks a rule of its context (for example PUBLIC on CREATE ROLE, or ** in SQL).</summary>
    public const string InvalidCombination = "PSP0006";

    /// <summary>PSP0007: a form the parser does not implement yet, although Oracle accepts it (for example an unsupported GRAPH_TABLE quantifier).</summary>
    public const string UnsupportedForm = "PSP0007";

    /// <summary>PSP0008: <c>SqlParser.ParseSelect</c> found input that is not exactly one SELECT statement.</summary>
    public const string NotSingleSelect = "PSP0008";

    /// <summary>PSP0099: an internal parser defect was contained. Please report it; the input itself is usually valid.</summary>
    public const string InternalError = "PSP0099";
}

/// <summary>
/// A lexical, syntax or version diagnostic. Positions are 1-based <see cref="Line"/> and <see cref="Column"/> and a
/// 0-based <see cref="Offset"/> into the source. <see cref="ToString"/> gives "(line:col) message".
/// </summary>
public sealed class ParseError
{
    /// <summary>Creates a diagnostic. <paramref name="length"/> is clamped to zero or more.</summary>
    /// <param name="message">Human-readable text.</param>
    /// <param name="offset">0-based offset of the first character of the problem.</param>
    /// <param name="line">1-based line of <paramref name="offset"/>.</param>
    /// <param name="column">1-based column of <paramref name="offset"/>.</param>
    /// <param name="length">Number of characters the problem spans; 0 for a point such as end of input.</param>
    /// <param name="code">One of the <see cref="ParseErrorCodes"/> values.</param>
    /// <param name="expected">The alternatives that were expected here, if known.</param>
    public ParseError(string message, int offset, int line, int column, int length = 0,
        string code = ParseErrorCodes.UnexpectedToken, IReadOnlyList<string>? expected = null)
    {
        Message = message;
        Offset = offset;
        Line = line;
        Column = column;
        Length = Math.Max(0, length);
        Code = code;
        Expected = expected ?? [];
    }

    /// <summary>The stable diagnostic code, one of the <see cref="ParseErrorCodes"/> values.</summary>
    public string Code { get; }

    /// <summary>Human-readable description. Its wording may change between releases; use <see cref="Code"/> to match errors.</summary>
    public string Message { get; }

    /// <summary>0-based offset of the first character of the offending text in the source.</summary>
    public int Offset { get; }

    /// <summary>
    /// Number of characters of the offending token (or of the unterminated literal or comment). Zero at end of input
    /// and for the synthetic ';' the lexer inserts after an unterminated literal.
    /// </summary>
    public int Length { get; }

    /// <summary>1-based line of <see cref="Offset"/>.</summary>
    public int Line { get; }

    /// <summary>
    /// 1-based column of <see cref="Offset"/>, counted in characters (a tab is one column). <see cref="FormatWithCaret"/>
    /// keeps tabs in the caret line so the caret lines up in any tab width.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// The tokens or descriptions the parser was looking for, in the order named in the message: keywords in upper
    /// case (<c>"TABLE"</c>), operator text (<c>"("</c>), or a short description (<c>"identifier"</c>). Empty when the
    /// parser did not expect anything in particular.
    /// </summary>
    public IReadOnlyList<string> Expected { get; }

    /// <summary>Returns "(line:col) message", the same form the error has always had.</summary>
    public override string ToString() => $"({Line}:{Column}) {Message}";

    /// <summary>
    /// Returns the source line containing <see cref="Offset"/>, followed on the next line by a caret under the span:
    /// <c>^</c> for a point (<see cref="Length"/> 0 or 1) and <c>^~~~</c> for a longer span. A span that runs past the
    /// end of its line is clipped at the line end. Tabs in the line are copied into the caret line so the caret stays
    /// aligned. An offset at end of input yields an empty source line with the caret at column 1 of that line.
    /// </summary>
    /// <param name="source">The text the error was reported against (<see cref="ParseResult.Source"/>).</param>
    /// <returns>Two lines separated by '\n', without a trailing newline.</returns>
    public string FormatWithCaret(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int offset = Math.Clamp(Offset, 0, source.Length);
        int lineStart = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        int lineEnd = source.IndexOf('\n', offset);
        if (lineEnd < 0) lineEnd = source.Length;
        int contentEnd = lineEnd > lineStart && source[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
        contentEnd = Math.Max(contentEnd, offset);

        var sb = new System.Text.StringBuilder();
        sb.Append(source, lineStart, contentEnd - lineStart).Append('\n');
        for (int i = lineStart; i < offset; i++) sb.Append(source[i] == '\t' ? '\t' : ' ');
        int span = Math.Min(Length, contentEnd - offset);
        sb.Append('^');
        if (span > 1) sb.Append('~', span - 1);
        return sb.ToString();
    }
}

/// <summary>
/// Thrown by the parser entry points that return a single result (<see cref="SqlParser.ParseStatement(string, ParseOptions?)"/>,
/// <see cref="SqlParser.ParseExpression(string, ParseOptions?)"/>, <c>SqlParser.ParseSelect</c>).
/// <see cref="Errors"/> holds every diagnostic that was found, and <see cref="Error"/> the first one.
/// </summary>
public sealed class ParseException : Exception
{
    /// <summary>Creates an exception for one diagnostic.</summary>
    public ParseException(ParseError error) : this([error]) { }

    /// <summary>Creates an exception for one or more diagnostics. The list must not be empty.</summary>
    public ParseException(IReadOnlyList<ParseError> errors)
        : base(errors.Count > 0 ? errors[0].ToString() : "Parsing failed.")
    {
        ArgumentOutOfRangeException.ThrowIfZero(errors.Count);
        Errors = errors;
    }

    /// <summary>The first diagnostic; the same as <c>Errors[0]</c>.</summary>
    public ParseError Error => Errors[0];

    /// <summary>All diagnostics that caused the failure, in source order of discovery. Never empty.</summary>
    public IReadOnlyList<ParseError> Errors { get; }
}
