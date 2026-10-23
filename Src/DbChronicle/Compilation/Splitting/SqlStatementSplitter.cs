using System;
using System.Collections.Generic;
using System.Text;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Compilation.Splitting;

public static class SqlStatementSplitter
{
	private enum ScanState
	{
		Normal,
		SingleQuote,
		DoubleQuote,
		Backtick,
		LineComment,
		BlockComment
	}

	public static IReadOnlyList<string> Split(string script, string dialectName, string customDelimiter = null)
	{
		List<string> result = new List<string>();
		if (string.IsNullOrEmpty(script))
		{
			return result;
		}
		string text = (dialectName ?? "").ToLowerInvariant();
		if (text == "sqlserver")
		{
			SplitByGo(script, result);
			return result;
		}
		SplitBySemicolon(script, result, string.IsNullOrEmpty(customDelimiter) ? ";" : customDelimiter);
		return result;
	}

	private static void SplitByGo(string script, List<string> result)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string[] array = SqlStatement.Normalize(script).Split(new char[1] { '\n' });
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (string.Equals(text2, "GO", StringComparison.OrdinalIgnoreCase) || text2.StartsWith("GO ", StringComparison.OrdinalIgnoreCase))
			{
				Flush(stringBuilder, result);
			}
			else
			{
				stringBuilder.AppendLine(text);
			}
		}
		Flush(stringBuilder, result);
	}

	private static void SplitBySemicolon(string script, List<string> result, string initialDelimiter)
	{
		StringBuilder stringBuilder = new StringBuilder();
		ScanState scanState = ScanState.Normal;
		int num = 0;
		string text = initialDelimiter;
		int index = 0;
		string text2 = SqlStatement.Normalize(script);
		while (index < text2.Length)
		{
			char c = text2[index];
			bool flag = index == 0 || text2[index - 1] == '\n';
			if (scanState == ScanState.Normal && flag && IsDelimiterDirective(text2, index, out var delimiter, out var consumed))
			{
				text = delimiter;
				index += consumed;
				continue;
			}
			switch (scanState)
			{
			case ScanState.Normal:
				switch (c)
				{
				case '\'':
					scanState = ScanState.SingleQuote;
					stringBuilder.Append(c);
					index++;
					goto end_IL_0079;
				case '"':
					scanState = ScanState.DoubleQuote;
					stringBuilder.Append(c);
					index++;
					goto end_IL_0079;
				case '`':
					scanState = ScanState.Backtick;
					stringBuilder.Append(c);
					index++;
					goto end_IL_0079;
				case '-':
					if (index + 1 < text2.Length && text2[index + 1] == '-')
					{
						scanState = ScanState.LineComment;
						stringBuilder.Append(c);
						index++;
						goto end_IL_0079;
					}
					break;
				}
				switch (c)
				{
				case '#':
					scanState = ScanState.LineComment;
					stringBuilder.Append(c);
					index++;
					goto end_IL_0079;
				case '/':
					if (index + 1 < text2.Length && text2[index + 1] == '*')
					{
						scanState = ScanState.BlockComment;
						stringBuilder.Append(c);
						index++;
						goto end_IL_0079;
					}
					break;
				}
				if (char.IsLetter(c))
				{
					string text3 = ReadWord(text2, ref index);
					if (string.Equals(text3, "BEGIN", StringComparison.OrdinalIgnoreCase))
					{
						num++;
					}
					else if (string.Equals(text3, "END", StringComparison.OrdinalIgnoreCase) && num > 0)
					{
						num--;
					}
					stringBuilder.Append(text3);
				}
				else if (num == 0 && text.Length > 0 && index + text.Length <= text2.Length && string.Compare(text2, index, text, 0, text.Length, StringComparison.Ordinal) == 0)
				{
					Flush(stringBuilder, result);
					index += text.Length;
				}
				else
				{
					stringBuilder.Append(c);
					index++;
				}
				break;
			case ScanState.SingleQuote:
				stringBuilder.Append(c);
				switch (c)
				{
				case '\'':
					if (index + 1 < text2.Length && text2[index + 1] == '\'')
					{
						stringBuilder.Append('\'');
						index += 2;
						goto end_IL_0079;
					}
					scanState = ScanState.Normal;
					break;
				case '\\':
					if (index + 1 >= text2.Length)
					{
						break;
					}
					stringBuilder.Append(text2[index + 1]);
					index += 2;
					goto end_IL_0079;
				}
				index++;
				break;
			case ScanState.DoubleQuote:
				stringBuilder.Append(c);
				if (c == '"')
				{
					scanState = ScanState.Normal;
				}
				index++;
				break;
			case ScanState.Backtick:
				stringBuilder.Append(c);
				if (c == '`')
				{
					scanState = ScanState.Normal;
				}
				index++;
				break;
			case ScanState.LineComment:
				stringBuilder.Append(c);
				if (c == '\n')
				{
					scanState = ScanState.Normal;
				}
				index++;
				break;
			case ScanState.BlockComment:
				{
					stringBuilder.Append(c);
					if (c == '*' && index + 1 < text2.Length && text2[index + 1] == '/')
					{
						stringBuilder.Append('/');
						index += 2;
						scanState = ScanState.Normal;
					}
					else
					{
						index++;
					}
					break;
				}
				end_IL_0079:
				break;
			}
		}
		Flush(stringBuilder, result);
	}

	private static bool IsDelimiterDirective(string text, int index, out string delimiter, out int consumed)
	{
		delimiter = null;
		consumed = 0;
		if (index + "DELIMITER".Length > text.Length)
		{
			return false;
		}
		if (string.Compare(text, index, "DELIMITER", 0, "DELIMITER".Length, StringComparison.OrdinalIgnoreCase) != 0)
		{
			return false;
		}
		int num = text.IndexOf('\n', index);
		if (num < 0)
		{
			num = text.Length;
		}
		string text2 = text.Substring(index, num - index).Trim();
		string[] array = text2.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 2)
		{
			return false;
		}
		delimiter = array[1];
		consumed = num - index;
		return true;
	}

	private static string ReadWord(string text, ref int index)
	{
		int num = index;
		while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
		{
			index++;
		}
		return text.Substring(num, index - num);
	}

	private static void Flush(StringBuilder buffer, List<string> result)
	{
		string text = buffer.ToString().Trim();
		buffer.Clear();
		if (text.Length > 0)
		{
			result.Add(text);
		}
	}
}
