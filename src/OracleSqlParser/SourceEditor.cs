using System.Text;

namespace OracleSqlParser;

/// <summary>
/// Edits a parsed script by splicing text into its original source, so everything the edits do not touch (comments,
/// whitespace, line breaks, keyword and identifier casing) is kept exactly as the user wrote it.
/// </summary>
/// <remarks>
/// <para>
/// Each edit covers a character range of <see cref="ParseResult.Source"/>, taken from the node's <see cref="SqlNode.Start"/>
/// and <see cref="SqlNode.End"/>. Leading trivia is not part of a node's range, so a comment just before a node stays
/// where it is. A node's range ends at its last token, so a trailing comment after it also stays.
/// </para>
/// <para>
/// Statement ranges exclude their terminator: <c>;</c> after a statement, or the <c>/</c> line after a PL/SQL unit.
/// <see cref="InsertAfter"/> on a statement therefore places text before its <c>;</c>. To add a statement after
/// another, include the terminator in the text, for example <c>InsertAfter(first, ";\nSELECT 2 FROM dual")</c>.
/// </para>
/// <para>
/// Edits are validated when they are added: an edit whose node has no source position (built in code, or produced
/// by <see cref="SqlParser.ParseStatement(string)"/> or <see cref="SqlParser.ParseExpression(string)"/>) throws, and so does an
/// edit whose range overlaps an earlier one. Two inserts at the same offset are allowed and keep the order they were added.
/// An insert exactly at the start or end of a replaced range is allowed, and goes before or after the replacement.
/// <see cref="Apply"/> applies the edits from the end of the text backwards, so the offsets stay valid.
/// </para>
/// <para>
/// Replacement nodes are printed with the <see cref="SqlScriptGeneratorOptions"/> given to the constructor.
/// The editor does not change the <see cref="ParseResult"/> or its tree.
/// </para>
/// </remarks>
public sealed class SourceEditor
{
    private readonly ParseResult _result;
    private readonly SqlScriptGenerator _generator;
    private readonly List<Edit> _edits = [];
    private int _seq;

    /// <summary>Creates an editor over <paramref name="result"/>.</summary>
    /// <param name="result">The parse result whose source text is edited. Only nodes from this result can be edited.</param>
    /// <param name="options">Options used to print replacement nodes. Null uses the generator defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public SourceEditor(ParseResult result, SqlScriptGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
        _generator = options is null ? new SqlScriptGenerator() : new SqlScriptGenerator(options);
    }

    /// <summary>
    /// Replaces the source text of <paramref name="original"/> with the generated SQL of <paramref name="replacement"/>.
    /// Comments and layout inside the original's range are lost; text outside it is kept.
    /// </summary>
    /// <param name="original">A node of this parse result that has a source position.</param>
    /// <param name="replacement">The node to print in its place.</param>
    /// <exception cref="InvalidOperationException"><paramref name="original"/> has no source position, or its range overlaps an earlier edit.</exception>
    public void Replace(SqlNode original, SqlNode replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Replace(original, _generator.Generate(replacement));
    }

    /// <summary>
    /// Replaces the source text of <paramref name="original"/> with <paramref name="text"/>, inserted as written.
    /// Use this for a bare name or any text the generator does not produce.
    /// </summary>
    /// <param name="original">A node of this parse result that has a source position.</param>
    /// <param name="text">The raw text to put in its place.</param>
    /// <exception cref="InvalidOperationException"><paramref name="original"/> has no source position, or its range overlaps an earlier edit.</exception>
    public void Replace(SqlNode original, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (start, end) = Span(original);
        AddEdit(new Edit(start, end, text, IsInsert: false, _seq++));
    }

    /// <summary>
    /// Inserts <paramref name="text"/> immediately before <paramref name="anchor"/>'s first character.
    /// Leading comments of the anchor stay above the inserted text.
    /// </summary>
    /// <param name="anchor">A node of this parse result that has a source position.</param>
    /// <param name="text">The raw text to insert.</param>
    /// <exception cref="InvalidOperationException"><paramref name="anchor"/> has no source position, or the insert falls strictly inside an earlier replaced range.</exception>
    public void InsertBefore(SqlNode anchor, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (start, _) = Span(anchor);
        AddEdit(new Edit(start, start, text, IsInsert: true, _seq++));
    }

    /// <summary>
    /// Inserts <paramref name="text"/> immediately after <paramref name="anchor"/>'s last character, before any trailing
    /// comment. For a statement, the text goes before its terminator (see the type remarks).
    /// </summary>
    /// <param name="anchor">A node of this parse result that has a source position.</param>
    /// <param name="text">The raw text to insert.</param>
    /// <exception cref="InvalidOperationException"><paramref name="anchor"/> has no source position, or the insert falls strictly inside an earlier replaced range.</exception>
    public void InsertAfter(SqlNode anchor, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (_, end) = Span(anchor);
        AddEdit(new Edit(end, end, text, IsInsert: true, _seq++));
    }

    /// <summary>
    /// Removes the source text of <paramref name="node"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the node is part of a comma-separated list, the comma that separates it from its neighbour is removed too,
    /// so <c>SELECT a  ,  b FROM t</c> minus <c>a</c> gives <c>SELECT b FROM t</c>, and minus <c>b</c> gives <c>SELECT a FROM t</c>.
    /// </para>
    /// <list type="bullet">
    /// <item><description>A node followed by a comma removes its own text, the comma, and the whitespace up to the next item.
    /// If the next item has a comment in front of it, the removed range stops at that comment, so the comment is kept.</description></item>
    /// <item><description>A node preceded by a comma (the last item) removes from the end of the token before that comma to the end of the node.</description></item>
    /// <item><description>Comments that sit inside the removed range (between the node and its comma) are removed with it.</description></item>
    /// </list>
    /// <para>
    /// A node that is not next to a comma is removed on its own. A node that is only part of a larger node (the alias
    /// <c>x</c> in <c>a x</c>, or <c>x</c> in <c>a.x</c>) is not a list item, so it is removed without a comma; remove the
    /// enclosing item to take the comma too. Removing the only item of a list, or any node whose removal leaves invalid
    /// SQL, produces text that no longer parses; re-parse the output to check.
    /// </para>
    /// <para>
    /// Removing two adjacent items of one list throws, because each takes the comma between them and the two ranges
    /// overlap. Apply the first removal, then remove the second from the new text with another editor.
    /// </para>
    /// </remarks>
    /// <param name="node">A node of this parse result that has a source position.</param>
    /// <exception cref="InvalidOperationException"><paramref name="node"/> has no source position, or its range overlaps an earlier edit.</exception>
    public void Remove(SqlNode node)
    {
        var (start, end) = RemovalRange(node);
        AddEdit(new Edit(start, end, string.Empty, IsInsert: false, _seq++));
    }

    /// <summary>
    /// Returns the source text with every edit applied. With no edits it returns <see cref="ParseResult.Source"/> unchanged.
    /// The editor can be used again after this call.
    /// </summary>
    /// <returns>The edited text.</returns>
    public string Apply()
    {
        var sb = new StringBuilder(_result.Source);
        // Backwards: the largest start first. At the same start, replacements run before inserts (so an insert lands
        // before the replacement text), and inserts run in reverse order of addition (so they come out in order of addition).
        foreach (var e in _edits.OrderByDescending(e => e.Start)
                                .ThenBy(e => e.IsInsert ? 1 : 0)
                                .ThenByDescending(e => e.Seq))
        {
            sb.Remove(e.Start, e.End - e.Start);
            sb.Insert(e.Start, e.Text);
        }
        return sb.ToString();
    }

    /// <summary>Prints a node with this editor's options (used by <see cref="SourceEdits"/>).</summary>
    internal string Print(SqlNode node) => _generator.Generate(node);

    /// <summary>The source text covered by a node's range (used by <see cref="SourceEdits"/>).</summary>
    internal string SourceText(SqlNode node)
    {
        var (start, end) = Span(node);
        return _result.Source.Substring(start, end - start);
    }

    private (int Start, int End) Span(SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var tokens = _result.Tokens;
        if (node.FirstTokenIndex < 0 || node.LastTokenIndex < 0)
            throw new InvalidOperationException(
                $"{node.GetType().Name} has no source position (it was built in code, or came from ParseStatement/ParseExpression), so it cannot be edited in the source text.");
        if (node.LastTokenIndex >= tokens.Count || node.FirstTokenIndex > node.LastTokenIndex ||
            tokens[node.FirstTokenIndex].Start != node.Start || tokens[node.LastTokenIndex].End != node.End)
            throw new InvalidOperationException(
                $"{node.GetType().Name} [{node.Start},{node.End}) does not belong to this ParseResult; only nodes from the parse result given to the SourceEditor can be edited.");
        return (node.Start, node.End);
    }

    private (int Start, int End) RemovalRange(SqlNode node)
    {
        var (start, end) = Span(node);
        var tokens = _result.Tokens;
        int first = node.FirstTokenIndex, last = node.LastTokenIndex;

        // Followed by a comma: take the comma and everything up to the next item (stopping at a comment in front of it).
        // Not when the node is the tail of a larger node (the alias of "a x", the "x" of "a.x"): that is not a list item.
        if (last + 1 < tokens.Count && IsComma(tokens[last + 1]))
        {
            if (HasLargerNodeSharing(node, sharesLastToken: true)) return (start, end);
            var next = last + 2 < tokens.Count ? tokens[last + 2] : tokens[^1];
            int nextStart = next.Start;
            foreach (var trivia in next.LeadingTrivia)
            {
                if (trivia.Kind == TriviaKind.Whitespace) continue;
                nextStart = trivia.Start;
                break;
            }
            return (start, Math.Max(end, nextStart));
        }

        // Preceded by a comma (the last item of a list): take the comma back to the end of the token before it.
        // Not when the node is the head of a larger node (the "b" of "b.c").
        if (first >= 2 && IsComma(tokens[first - 1]) && !HasLargerNodeSharing(node, sharesLastToken: false))
            return (tokens[first - 2].End, end);

        return (start, end);
    }

    /// <summary>
    /// True when a different node, larger than <paramref name="node"/>, ends (or starts) on the same token, so that
    /// <paramref name="node"/> is only part of it. Such a node is not a list item and must not take a comma with it.
    /// </summary>
    private bool HasLargerNodeSharing(SqlNode node, bool sharesLastToken)
    {
        int first = node.FirstTokenIndex, last = node.LastTokenIndex;
        foreach (var n in _result.Script.Descendants())
        {
            if (ReferenceEquals(n, node) || n.FirstTokenIndex < 0) continue;
            bool hit = sharesLastToken
                ? n.LastTokenIndex == last && n.FirstTokenIndex < first
                : n.FirstTokenIndex == first && n.LastTokenIndex > last;
            if (hit) return true;
        }
        return false;
    }

    private static bool IsComma(Token t) => t.Type == TokenType.Operator && t.Text == ",";

    private void AddEdit(Edit edit)
    {
        foreach (var existing in _edits)
        {
            if (Overlaps(existing, edit))
                throw new InvalidOperationException(
                    $"Edit [{edit.Start},{edit.End}) overlaps an earlier edit [{existing.Start},{existing.End}).");
        }
        _edits.Add(edit);
    }

    private static bool Overlaps(Edit a, Edit b)
    {
        bool aPoint = a.End == a.Start, bPoint = b.End == b.Start;
        if (aPoint && bPoint) return false;                                   // two inserts never overlap
        if (aPoint) return b.Start < a.Start && a.Start < b.End;              // an insert strictly inside a range
        if (bPoint) return a.Start < b.Start && b.Start < a.End;
        return Math.Max(a.Start, b.Start) < Math.Min(a.End, b.End);           // two ranges share some character
    }

    private sealed record Edit(int Start, int End, string Text, bool IsInsert, int Seq);
}
