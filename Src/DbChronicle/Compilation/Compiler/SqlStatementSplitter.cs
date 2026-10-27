using System.Collections.Generic;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Compiler
{
    public static class SqlStatementSplitter
    {
        public static IReadOnlyList<string> Split(string script, string dialectName, string customDelimiter = null)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(script))
            {
                return result;
            }

            var dialect = (dialectName ?? "").Trim().ToLowerInvariant();
            var text = SqlStatement.Normalize(script);
            var delimiter = string.IsNullOrEmpty(customDelimiter) ? ";" : customDelimiter;

            SplitCore(text, dialect, result, delimiter);
            return result;
        }

        private static void SplitCore(string text, string dialect, List<string> result, string initialDelimiter)
        {
            var isSqlServer = dialect == "sqlserver";
            var isMySql = dialect == "mysql" || dialect == "mariadb";
            var supportsDollarQuote = dialect == "postgresql";
            var supportsBracket = dialect == "sqlite" || dialect == "sqlserver";
            var supportsNestedComment = dialect == "postgresql" || dialect == "sqlserver";

            var buffer = new StringBuilder();
            var state = SqlScanState.Normal;
            var blockDepth = 0;
            var commentDepth = 0;
            var delimiter = initialDelimiter;
            var dollarTag = "";
            var escapeBackslash = false;
            var index = 0;

            while (index < text.Length)
            {
                var c = text[index];

                if (state == SqlScanState.Normal)
                {
                    if (isSqlServer && IsLineStart(text, index) && TryReadGo(text, index, out var goCount, out var goLength))
                    {
                        FlushBatch(buffer, result, goCount);
                        index += goLength;
                        continue;
                    }

                    if (!isSqlServer && IsLineStart(text, index) && TryReadDelimiterDirective(text, index, out var newDelimiter, out var directiveLength))
                    {
                        delimiter = newDelimiter;
                        index += directiveLength;
                        continue;
                    }
                }

                switch (state)
                {
                    case SqlScanState.Normal:
                        if (blockDepth == 0 && MatchesAt(text, index, delimiter))
                        {
                            FlushBatch(buffer, result, 1);
                            index += delimiter.Length;
                            continue;
                        }

                        if (c == '\'')
                        {
                            escapeBackslash = isMySql || IsEscapeStringPrefix(text, index);
                            state = SqlScanState.SingleQuote;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '"')
                        {
                            state = SqlScanState.DoubleQuote;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '`' && isMySql)
                        {
                            state = SqlScanState.Backtick;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '[' && supportsBracket)
                        {
                            state = SqlScanState.Bracket;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '-' && index + 1 < text.Length && text[index + 1] == '-')
                        {
                            state = SqlScanState.LineComment;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '#' && isMySql)
                        {
                            state = SqlScanState.LineComment;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '/' && index + 1 < text.Length && text[index + 1] == '*')
                        {
                            state = SqlScanState.BlockComment;
                            commentDepth = 1;
                            buffer.Append(c);
                            index++;
                            continue;
                        }

                        if (c == '$' && supportsDollarQuote && TryReadDollarTag(text, index, out var tag, out var tagLength))
                        {
                            state = SqlScanState.DollarQuote;
                            dollarTag = tag;
                            buffer.Append(text, index, tagLength);
                            index += tagLength;
                            continue;
                        }

                        if (char.IsLetter(c))
                        {
                            var word = ReadWord(text, ref index);
                            buffer.Append(word);
                            if (!isSqlServer)
                            {
                                UpdateBlockDepth(text, word, ref index, ref blockDepth, buffer);
                            }
                            continue;
                        }

                        buffer.Append(c);
                        index++;
                        continue;

                    case SqlScanState.SingleQuote:
                        buffer.Append(c);
                        if (c == '\'')
                        {
                            if (index + 1 < text.Length && text[index + 1] == '\'')
                            {
                                buffer.Append('\'');
                                index += 2;
                                continue;
                            }

                            state = SqlScanState.Normal;
                            index++;
                            continue;
                        }

                        if (c == '\\' && escapeBackslash && index + 1 < text.Length)
                        {
                            buffer.Append(text[index + 1]);
                            index += 2;
                            continue;
                        }

                        index++;
                        continue;

                    case SqlScanState.DoubleQuote:
                        buffer.Append(c);
                        if (c == '"')
                        {
                            if (index + 1 < text.Length && text[index + 1] == '"')
                            {
                                buffer.Append('"');
                                index += 2;
                                continue;
                            }

                            state = SqlScanState.Normal;
                        }

                        index++;
                        continue;

                    case SqlScanState.Backtick:
                        buffer.Append(c);
                        if (c == '`')
                        {
                            if (index + 1 < text.Length && text[index + 1] == '`')
                            {
                                buffer.Append('`');
                                index += 2;
                                continue;
                            }

                            state = SqlScanState.Normal;
                        }

                        index++;
                        continue;

                    case SqlScanState.Bracket:
                        buffer.Append(c);
                        if (c == ']')
                        {
                            if (index + 1 < text.Length && text[index + 1] == ']')
                            {
                                buffer.Append(']');
                                index += 2;
                                continue;
                            }

                            state = SqlScanState.Normal;
                        }

                        index++;
                        continue;

                    case SqlScanState.LineComment:
                        buffer.Append(c);
                        if (c == '\n')
                        {
                            state = SqlScanState.Normal;
                        }

                        index++;
                        continue;

                    case SqlScanState.BlockComment:
                        buffer.Append(c);
                        if (c == '*' && index + 1 < text.Length && text[index + 1] == '/')
                        {
                            buffer.Append('/');
                            index += 2;
                            commentDepth--;
                            if (commentDepth <= 0)
                            {
                                state = SqlScanState.Normal;
                            }
                            continue;
                        }

                        if (supportsNestedComment && c == '/' && index + 1 < text.Length && text[index + 1] == '*')
                        {
                            buffer.Append('*');
                            index += 2;
                            commentDepth++;
                            continue;
                        }

                        index++;
                        continue;

                    case SqlScanState.DollarQuote:
                        if (string.CompareOrdinal(text, index, dollarTag, 0, dollarTag.Length) == 0)
                        {
                            buffer.Append(dollarTag);
                            index += dollarTag.Length;
                            state = SqlScanState.Normal;
                            continue;
                        }

                        buffer.Append(c);
                        index++;
                        continue;
                }
            }

            if (state != SqlScanState.Normal)
            {
                throw new InvalidOperationException("SQL 拆分失败：存在未闭合的 " + DescribeState(state) + "（位置 " + index + "）");
            }

            if (blockDepth != 0)
            {
                throw new InvalidOperationException("SQL 拆分失败：语句块未闭合（BEGIN / CASE / IF / LOOP 与 END 不配对）");
            }

            FlushBatch(buffer, result, 1);
        }

        private static void UpdateBlockDepth(string text, string word, ref int index, ref int blockDepth, StringBuilder buffer)
        {
            if (string.Equals(word, "END", StringComparison.OrdinalIgnoreCase))
            {
                if (blockDepth > 0)
                {
                    blockDepth--;
                }

                var qualifier = PeekWord(text, index);
                if (IsBlockQualifier(qualifier))
                {
                    var cursor = index;
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    {
                        cursor++;
                    }

                    buffer.Append(text, index, cursor - index);
                    index = cursor;
                    buffer.Append(ReadWord(text, ref index));
                }
                return;
            }

            if (!OpensBlock(text, word, index))
            {
                return;
            }

            blockDepth++;
        }

        private static bool OpensBlock(string text, string word, int index)
        {
            if (string.Equals(word, "BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                var next = PeekWord(text, index);
                return !string.Equals(next, "TRANSACTION", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(next, "WORK", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(next, "DISTRIBUTED", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(word, "CASE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "LOOP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "WHILE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "REPEAT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(word, "IF", StringComparison.OrdinalIgnoreCase))
            {
                if (NextNonSpaceChar(text, index) == '(')
                {
                    return false;
                }

                var next = PeekWord(text, index);
                return !string.Equals(next, "EXISTS", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(next, "NOT", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static char NextNonSpaceChar(string text, int index)
        {
            var cursor = index;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            return cursor < text.Length ? text[cursor] : '\0';
        }

        private static bool IsBlockQualifier(string word)
        {
            return string.Equals(word, "IF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "LOOP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "CASE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "WHILE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "REPEAT", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEscapeStringPrefix(string text, int quoteIndex)
        {
            var i = quoteIndex - 1;
            return i >= 0 && (text[i] == 'E' || text[i] == 'e');
        }

        private static string PeekWord(string text, int index)
        {
            var cursor = index;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            var start = cursor;
            while (cursor < text.Length && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
            {
                cursor++;
            }

            return cursor > start ? text.Substring(start, cursor - start) : "";
        }

        private static bool IsLineStart(string text, int index)
        {
            var cursor = index;
            while (cursor > 0 && (text[cursor - 1] == ' ' || text[cursor - 1] == '\t'))
            {
                cursor--;
            }

            return cursor == 0 || text[cursor - 1] == '\n';
        }

        private static bool TryReadGo(string text, int index, out int count, out int length)
        {
            count = 1;
            length = 0;

            var cursor = index;
            while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t'))
            {
                cursor++;
            }

            if (cursor + 2 > text.Length || !string.Equals(text.Substring(cursor, 2), "GO", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (cursor > 0 && !IsLineStart(text, cursor))
            {
                return false;
            }

            var afterWord = cursor + 2;
            var digitsStart = afterWord;
            while (digitsStart < text.Length && (text[digitsStart] == ' ' || text[digitsStart] == '\t'))
            {
                digitsStart++;
            }

            var digitsEnd = digitsStart;
            while (digitsEnd < text.Length && char.IsDigit(text[digitsEnd]))
            {
                digitsEnd++;
            }

            if (digitsEnd > digitsStart)
            {
                int parsed;
                if (int.TryParse(text.Substring(digitsStart, digitsEnd - digitsStart), out parsed) && parsed > 0)
                {
                    count = parsed;
                }
            }

            var tail = digitsEnd;
            while (tail < text.Length && (text[tail] == ' ' || text[tail] == '\t'))
            {
                tail++;
            }

            if (tail < text.Length && text[tail] != '\n' && text[tail] != '\r' && text[tail] != '-')
            {
                return false;
            }

            if (tail < text.Length && text[tail] == '-' && (tail + 1 >= text.Length || text[tail + 1] != '-'))
            {
                return false;
            }

            var lineEnd = text.IndexOf('\n', tail);
            length = (lineEnd < 0 ? text.Length : lineEnd) - index;
            if (lineEnd >= 0)
            {
                length++;
            }

            return true;
        }

        private static bool TryReadDelimiterDirective(string text, int index, out string delimiter, out int length)
        {
            delimiter = null;
            length = 0;

            if (index + "DELIMITER".Length > text.Length)
            {
                return false;
            }

            if (string.Compare(text, index, "DELIMITER", 0, "DELIMITER".Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            var lineEnd = text.IndexOf('\n', index);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var line = text.Substring(index, lineEnd - index).Trim();
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            delimiter = parts[1];
            length = lineEnd - index;
            if (lineEnd < text.Length)
            {
                length++;
            }

            return true;
        }

        private static bool TryReadDollarTag(string text, int index, out string tag, out int length)
        {
            tag = null;
            length = 0;

            var cursor = index + 1;
            while (cursor < text.Length && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
            {
                cursor++;
            }

            if (cursor >= text.Length || text[cursor] != '$')
            {
                return false;
            }

            var body = text.Substring(index + 1, cursor - index - 1);
            if (body.Length > 0 && char.IsDigit(body[0]))
            {
                return false;
            }

            tag = text.Substring(index, cursor - index + 1);
            length = tag.Length;
            return true;
        }

        private static bool MatchesAt(string text, int index, string value)
        {
            return value.Length > 0
                && index + value.Length <= text.Length
                && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
        }

        private static string ReadWord(string text, ref int index)
        {
            var start = index;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
            {
                index++;
            }

            return text.Substring(start, index - start);
        }

        private static void FlushBatch(StringBuilder buffer, List<string> result, int repeat)
        {
            var statement = buffer.ToString().Trim();
            buffer.Clear();
            if (statement.Length == 0)
            {
                return;
            }

            for (var i = 0; i < repeat; i++)
            {
                result.Add(statement);
            }
        }

        private static string DescribeState(SqlScanState state)
        {
            switch (state)
            {
                case SqlScanState.SingleQuote:
                    return "单引号字符串";
                case SqlScanState.DoubleQuote:
                    return "双引号标识符";
                case SqlScanState.Backtick:
                    return "反引号标识符";
                case SqlScanState.Bracket:
                    return "方括号标识符";
                case SqlScanState.BlockComment:
                    return "块注释";
                case SqlScanState.DollarQuote:
                    return "美元引用字符串";
                default:
                    return "语句";
            }
        }
    }
}