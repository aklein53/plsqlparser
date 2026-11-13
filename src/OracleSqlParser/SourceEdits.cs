namespace OracleSqlParser;

/// <summary>Common edits built on <see cref="SourceEditor"/>.</summary>
public static class SourceEdits
{
    /// <summary>
    /// Adds <paramref name="predicate"/> to the WHERE clause of <paramref name="query"/> with AND, keeping the user's text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the query has a WHERE, its condition is replaced by <c>(</c> + the original condition text + <c>) AND (</c> +
    /// the generated predicate + <c>)</c>. The parentheses keep an existing OR grouped, and comments inside the
    /// condition are kept.
    /// </para>
    /// <para>
    /// If the query has no WHERE, <c> WHERE </c> + the generated predicate is inserted after the last token of the FROM
    /// clause, so it lands before any trailing comment. A query with no FROM gets it after its last select-list item.
    /// </para>
    /// </remarks>
    /// <param name="editor">The editor over the parse result that <paramref name="query"/> belongs to.</param>
    /// <param name="query">The query to change. It must have a source position.</param>
    /// <param name="predicate">The condition to add. It is printed with the editor's options.</param>
    /// <exception cref="InvalidOperationException">The query or one of the nodes used as anchors has no source position, or the edit overlaps another edit.</exception>
    public static void AndWhere(this SourceEditor editor, QuerySpecification query, Expression predicate)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(predicate);

        string condition = editor.Print(predicate);
        if (query.Where is { } where)
        {
            editor.Replace(where, "(" + editor.SourceText(where) + ") AND (" + condition + ")");
        }
        else if (query.From.Count > 0)
        {
            // The FROM item that ends last (for a join, its own span covers the joined tables and ON condition).
            var lastFrom = query.From.MaxBy(t => t.LastTokenIndex)!;
            editor.InsertAfter(lastFrom, " WHERE " + condition);
        }
        else
        {
            if (query.SelectList.Count == 0)
                throw new InvalidOperationException("The query has no FROM and no select list, so there is nowhere to add a WHERE clause.");
            editor.InsertAfter(query.SelectList[^1], " WHERE " + condition);
        }
    }
}
