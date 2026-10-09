namespace ActualChat.Mui;

// Accepts exactly one SQL statement (an optional trailing semicolon is dropped).
// The scanner knows PostgreSQL comments, string literals (including E'...' escapes), quoted identifiers
// and dollar-quoted strings, so a semicolon inside any of them doesn't count. Anything it can't
// classify with certainty is rejected: this is a defense layer, not the only one.
public static class MuiSqlGuard
{
    public static string GetSingleStatement(string? sql)
    {
        var text = sql ?? "";
        var firstSemicolon = -1;
        var hasCode = false;
        var hasCodeAfterSemicolon = false;
        var i = 0;
        while (i < text.Length) {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '-' && next == '-') {
                i = SkipLineComment(text, i);
                continue;
            }
            if (c == '/' && next == '*') {
                i = SkipBlockComment(text, i);
                continue;
            }

            if (firstSemicolon >= 0)
                hasCodeAfterSemicolon = true;
            hasCode = true;
            switch (c) {
            case ';':
                if (firstSemicolon < 0) {
                    firstSemicolon = i;
                    hasCode = i > 0 && HasCodeBefore(text, i);
                }
                i++;
                break;
            case '\'':
                i = SkipString(text, i, IsEscapeString(text, i));
                break;
            case '"':
                i = SkipQuoted(text, i, '"');
                break;
            case '$':
                i = SkipDollar(text, i);
                break;
            default:
                i++;
                break;
            }
        }

        if (hasCodeAfterSemicolon)
            throw StandardError.Constraint("Only a single SQL statement is allowed.");

        var statement = firstSemicolon >= 0 ? text[..firstSemicolon] : text;
        if (!hasCode || statement.Trim().Length == 0)
            throw StandardError.Constraint("The SQL statement is empty.");

        return statement.Trim();
    }

    // Private methods

    private static bool HasCodeBefore(string text, int end)
    {
        var i = 0;
        while (i < end) {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (char.IsWhiteSpace(c))
                i++;
            else if (c == '-' && next == '-')
                i = SkipLineComment(text, i);
            else if (c == '/' && next == '*')
                i = SkipBlockComment(text, i);
            else
                return true;
        }
        return false;
    }

    private static int SkipLineComment(string text, int start)
    {
        var end = text.IndexOf('\n', start);
        return end < 0 ? text.Length : end + 1;
    }

    private static int SkipBlockComment(string text, int start)
    {
        var depth = 0;
        var i = start;
        while (i < text.Length) {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*') {
                depth++;
                i += 2;
            }
            else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/') {
                depth--;
                i += 2;
                if (depth == 0)
                    return i;
            }
            else
                i++;
        }
        throw StandardError.Constraint("The SQL contains an unterminated comment.");
    }

    // E'...' is the only string form where a backslash escapes the next character
    private static bool IsEscapeString(string text, int quoteIndex)
    {
        if (quoteIndex == 0)
            return false;

        var prefix = text[quoteIndex - 1];
        if (prefix != 'E' && prefix != 'e')
            return false;

        return quoteIndex < 2 || !IsIdentifierChar(text[quoteIndex - 2]);
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int SkipString(string text, int start, bool hasEscapes)
    {
        var i = start + 1;
        while (i < text.Length) {
            var c = text[i];
            if (hasEscapes && c == '\\') {
                i += 2;
                continue;
            }
            if (c == '\'') {
                if (i + 1 < text.Length && text[i + 1] == '\'') {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        throw StandardError.Constraint("The SQL contains an unterminated string.");
    }

    private static int SkipQuoted(string text, int start, char quote)
    {
        var i = start + 1;
        while (i < text.Length) {
            if (text[i] == quote) {
                if (i + 1 < text.Length && text[i + 1] == quote) {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        throw StandardError.Constraint("The SQL contains an unterminated quoted identifier.");
    }

    // $tag$ ... $tag$ (the tag may be empty); a lone $ or $1 is not a quote
    private static int SkipDollar(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) {
            if (i == start + 1 && char.IsDigit(text[i]))
                return start + 1; // a positional parameter such as $1
            i++;
        }
        if (i >= text.Length || text[i] != '$')
            return start + 1;

        var tag = text[start..(i + 1)];
        var end = text.IndexOf(tag, i + 1);
        if (end < 0)
            throw StandardError.Constraint("The SQL contains an unterminated dollar-quoted string.");

        return end + tag.Length;
    }
}
