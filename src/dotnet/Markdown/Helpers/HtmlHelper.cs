// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Markdig.Helpers
{
    /// <summary>
    /// Helper to parse several HTML tags.
    /// </summary>
    public static class HtmlHelper
    {
        private static readonly char[] SearchBackAndAmp = { '\\', '&' };
        private static readonly char[] SearchAmp = { '&' };
        private static readonly string[] EscapeUrlsForAscii = new string[128];

        static HtmlHelper()
        {
            for (int i = 0; i < EscapeUrlsForAscii.Length; i++)
            {
                if (i <= 32 || @"""'<>[\]^`{|}~".IndexOf((char)i) >= 0 || i == 127)
                {
                    EscapeUrlsForAscii[i] = $"%{i:X2}";
                }
                else if ((char) i == '&')
                {
                    EscapeUrlsForAscii[i] = "&amp;";
                }
            }
        }

        public static string? EscapeUrlCharacter(char c)
        {
            return c < 128 ? EscapeUrlsForAscii[c] : null;
        }

        public static bool TryParseHtmlTag(ref StringSlice text, [NotNullWhen(true)] out string? htmlTag)
        {
            var builder = StringBuilderCache.Local();
            if (TryParseHtmlTag(ref text, builder))
            {
                htmlTag = builder.GetStringAndReset();
                return true;
            }
            else
            {
                htmlTag = null;
                return false;
            }
        }

        public static bool TryParseHtmlTag(ref StringSlice text, StringBuilder builder)
        {
            if (builder is null) ThrowHelper.ArgumentNullException(nameof(builder));
            var c = text.CurrentChar;
            if (c != '<')
            {
                return false;
            }
            c = text.NextChar();

            builder.Append('<');

            switch (c)
            {
                case '/':
                    return TryParseHtmlCloseTag(ref text, builder);
                case '?':
                    return TryParseHtmlTagProcessingInstruction(ref text, builder);
                case '!':
                    builder.Append(c);
                    c = text.NextChar();
                    if (c == '-')
                    {
                        return TryParseHtmlTagHtmlComment(ref text, builder);
                    }

                    if (c == '[')
                    {
                        return TryParseHtmlTagCData(ref text, builder);
                    }

                    return TryParseHtmlTagDeclaration(ref text, builder);
            }

            return TryParseHtmlTagOpenTag(ref text, builder);
        }

        internal static bool TryParseHtmlTagOpenTag(ref StringSlice text, StringBuilder builder)
        {
            var c = text.CurrentChar;

            // Parse the tagname
            if (!c.IsAlpha())
            {
                return false;
            }
            builder.Append(c);

            while (true)
            {
                c = text.NextChar();
                if (c.IsAlphaNumeric() || c == '-')
                {
                    builder.Append(c);
                }
                else
                {
                    break;
                }
            }

            bool hasAttribute = false;
            while (true)
            {
                var hasWhitespaces = false;
                // Skip any whitespaces
                while (c.IsWhitespace())
                {
                    builder.Append(c);
                    c = text.NextChar();
                    hasWhitespaces = true;
                }

                switch (c)
                {
                    case '\0':
                        return false;
                    case '>':
                        text.SkipChar();
                        builder.Append(c);
                        return true;
                    case '/':
                        builder.Append('/');
                        c = text.NextChar();
                        if (c != '>')
                        {
                            return false;
                        }
                        text.SkipChar();
                        builder.Append('>');
                        return true;
                    case '=':

                        if (!hasAttribute)
                        {
                            return false;
                        }

                        builder.Append('=');

                        // Skip any spaces after
                        c = text.NextChar();
                        while (c.IsWhitespace())
                        {
                            builder.Append(c);
                            c = text.NextChar();
                        }

                        // Parse a quoted string
                        if (c == '\'' || c == '\"')
                        {
                            builder.Append(c);
                            char openingStringChar = c;
                            while (true)
                            {
                                c = text.NextChar();
                                if (c == '\0')
                                {
                                    return false;
                                }
                                if (c != openingStringChar)
                                {
                                    builder.Append(c);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            builder.Append(c);
                            c = text.NextChar();
                        }
                        else
                        {
                            // Parse until we match a space or a special html character
                            int matchCount = 0;
                            while (true)
                            {
                                if (c == '\0')
                                {
                                    return false;
                                }
                                if (c == ' ' || c == '\n' || c == '"' || c == '\'' || c == '=' || c == '<' || c == '>' || c == '`')
                                {
                                    break;
                                }
                                matchCount++;
                                builder.Append(c);
                                c = text.NextChar();
                            }

                            // We need at least one char after '='
                            if (matchCount == 0)
                            {
                                return false;
                            }
                        }

                        hasAttribute = false;
                        continue;
                    default:
                        if (!hasWhitespaces)
                        {
                            return false;
                        }

                        // Parse the attribute name
                        if (!(c.IsAlpha() || c == '_' || c == ':'))
                        {
                            return false;
                        }
                        builder.Append(c);

                        while (true)
                        {
                            c = text.NextChar();
                            if (c.IsAlphaNumeric() || c == '_' || c == ':' || c == '.' || c == '-')
                            {
                                builder.Append(c);
                            }
                            else
                            {
                                break;
                            }
                        }

                        hasAttribute = true;
                        break;
                }
            }
        }

        private static bool TryParseHtmlTagDeclaration(ref StringSlice text, StringBuilder builder)
        {
            var c = text.CurrentChar;
            bool hasAlpha = false;
            while (c.IsAlphaUpper())
            {
                builder.Append(c);
                c = text.NextChar();
                hasAlpha = true;
            }

            if (!hasAlpha || !c.IsWhitespace())
            {
                return false;
            }

            // Regexp: "\\![A-Z]+\\s+[^>\\x00]*>"
            while (true)
            {
                builder.Append(c);
                c = text.NextChar();
                if (c == '\0')
                {
                    return false;
                }

                if (c == '>')
                {
                    text.SkipChar();
                    builder.Append('>');
                    return true;
                }
            }
        }

        private static bool TryParseHtmlTagCData(ref StringSlice text, StringBuilder builder)
        {
            if (text.Match("[CDATA["))
            {
                builder.Append("[CDATA[");
                text.Start += 6;

                char c = '\0';
                while (true)
                {
                    var pc = c;
                    c = text.NextChar();
                    if (c == '\0')
                    {
                        return false;
                    }

                    builder.Append(c);

                    if (c == ']' && pc == ']' && text.PeekChar() == '>')
                    {
                        text.SkipChar();
                        text.SkipChar();
                        builder.Append('>');
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool TryParseHtmlCloseTag(ref StringSlice text, StringBuilder builder)
        {
            // </[A-Za-z][A-Za-z0-9]+\s*>
            builder.Append('/');

            var c = text.NextChar();
            if (!c.IsAlpha())
            {
                return false;
            }
            builder.Append(c);

            bool skipSpaces = false;
            while (true)
            {
                c = text.NextChar();
                if (c == '>')
                {
                    text.SkipChar();
                    builder.Append('>');
                    return true;
                }

                if (skipSpaces)
                {
                    if (c != ' ')
                    {
                        break;
                    }
                }
                else if (c == ' ')
                {
                    skipSpaces = true;
                }
                else if (!(c.IsAlphaNumeric() || c == '-'))
                {
                    break;
                }

                builder.Append(c);
            }
            return false;
        }


        private static bool TryParseHtmlTagHtmlComment(ref StringSlice text, StringBuilder builder)
        {
            var c = text.NextChar();
            if (c != '-')
            {
                return false;
            }
            builder.Append('-');
            builder.Append('-');
            if (text.PeekChar() == '>')
            {
                return false;
            }

            var countHyphen = 0;
            while (true)
            {
                c = text.NextChar();
                if (c == '\0')
                {
                    return false;
                }

                if (countHyphen == 2)
                {
                    if (c == '>')
                    {
                        builder.Append('>');
                        text.SkipChar();
                        return true;
                    }
                    return false;
                }
                countHyphen = c == '-' ? countHyphen + 1 : 0;
                builder.Append(c);
            }
        }

        private static bool TryParseHtmlTagProcessingInstruction(ref StringSlice text, StringBuilder builder)
        {
            builder.Append('?');
            var prevChar = '\0';
            while (true)
            {
                var c = text.NextChar();
                if (c == '\0')
                {
                    return false;
                }

                if (c == '>' && prevChar == '?')
                {
                    builder.Append('>');
                    text.SkipChar();
                    return true;
                }
                prevChar = c;
                builder.Append(c);
            }
        }

        /// <summary>
        /// Destructively unescape a string: remove backslashes before punctuation or symbol characters.
        /// </summary>
        /// <param name="text">The string data that will be changed by unescaping any punctuation or symbol characters.</param>
        /// <param name="removeBackSlash">if set to <c>true</c> [remove back slash].</param>
        /// <returns></returns>
        public static string Unescape(string? text, bool removeBackSlash = true)
        {
            // Credits: code from CommonMark.NET
            // Copyright (c) 2014, Kārlis Gaņģis All rights reserved. 
            // See license for details:  https://github.com/Knagis/CommonMark.NET/blob/master/LICENSE.md
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            // remove backslashes before punctuation chars:
            int searchPos = 0;
            int lastPos = 0;
            char c;
            char[] search = removeBackSlash ? SearchBackAndAmp : SearchAmp;
            StringBuilder? sb = null;

            while ((searchPos = text!.IndexOfAny(search, searchPos)) != -1)
            {
                sb ??= StringBuilderCache.Local();
                c = text[searchPos];
                if (removeBackSlash && c == '\\')
                {
                    searchPos++;

                    if (text.Length == searchPos)
                        break;

                    c = text[searchPos];
                    if (c.IsEscapableSymbol())
                    {
                        sb.Append(text, lastPos, searchPos - lastPos - 1);
                        lastPos = searchPos;
                    }
                }
                else if (c == '&')
                {
                    var match = ScanEntity(new StringSlice(text, searchPos, text.Length - 1), out int numericEntity, out int entityNameStart, out int entityNameLength);
                    if (match == 0)
                    {
                        searchPos++;
                    }
                    else
                    {
                        searchPos += match;

                        if (entityNameLength > 0)
                        {
                            var decoded = EntityHelper.DecodeEntity(text.AsSpan(entityNameStart, entityNameLength));
                            if (decoded != null)
                            {
                                sb.Append(text, lastPos, searchPos - match - lastPos);
                                sb.Append(decoded);
                                lastPos = searchPos;
                            }
                        }
                        else if (numericEntity >= 0)
                        {
                            sb.Append(text, lastPos, searchPos - match - lastPos);
                            EntityHelper.DecodeEntity(numericEntity, sb);
                            lastPos = searchPos;
                        }
                    }
                }
            }

            if (sb is null || lastPos == 0)
                return text;

            sb.Append(text, lastPos, text.Length - lastPos);
            return sb.GetStringAndReset();
        }

        /// <summary>
        /// Scans an entity.
        /// Returns number of chars matched.
        /// </summary>
        public static int ScanEntity<T>(T slice, out int numericEntity, out int namedEntityStart,  out int namedEntityLength) where T : ICharIterator
        {
            // Credits: code from CommonMark.NET
            // Copyright (c) 2014, Kārlis Gaņģis All rights reserved. 
            // See license for details:  https://github.com/Knagis/CommonMark.NET/blob/master/LICENSE.md

            numericEntity = 0;
            namedEntityStart = 0;
            namedEntityLength = 0;

            if (slice.CurrentChar != '&' || slice.PeekChar(3) == '\0')
            {
                return 0;
            }

            var start = slice.Start;
            char c = slice.NextChar();
            int counter = 0;
            
            if (c == '#')
            {
                c = slice.PeekChar();
                if ((c | 0x20) == 'x')
                {
                    c = slice.NextChar(); // skip #
                    // expect 1-6 hex digits starting from pos+3
                    while (c != '\0')
                    {
                        c = slice.NextChar();

                        if (c.IsDigit())
                        {
                            if (++counter == 7) return 0;
                            numericEntity = numericEntity * 16 + (c - '0');
                            continue;
                        }
                        else if ((uint)((c - 'A') & ~0x20) <= ('F' - 'A'))
                        {
                            if (++counter == 7) return 0;
                            numericEntity = numericEntity * 16 + ((c | 0x20) - 'a' + 10);
                            continue;
                        }

                        if (c == ';')
                            return counter == 0 ? 0 : slice.Start - start + 1;

                        return 0;
                    }
                }
                else
                {
                    // expect 1-7 digits starting from pos+2
                    while (c != '\0')
                    {
                        c = slice.NextChar();

                        if (c.IsDigit())
                        {
                            if (++counter == 8) return 0;
                            numericEntity = numericEntity * 10 + (c - '0');
                            continue;
                        }

                        if (c == ';')
                            return counter == 0 ? 0 : slice.Start - start + 1;

                        return 0;
                    }
                }
            }
            else
            {
                // expect a letter and 1-31 letters or digits
                if (!c.IsAlpha())
                    return 0;

                namedEntityStart = slice.Start;
                namedEntityLength++;

                while (c != '\0')
                {
                    c = slice.NextChar();

                    if (c.IsAlphaNumeric())
                    {
                        if (++counter == 32)
                            return 0;
                        namedEntityLength++;
                        continue;
                    }

                    if (c == ';')
                    {
                        return counter == 0 ? 0 : slice.Start - start + 1;
                    }

                    return 0;
                }
            }

            return 0;
        }
    }
}