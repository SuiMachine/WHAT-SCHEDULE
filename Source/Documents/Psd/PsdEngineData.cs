// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Text;

using SkiaSharp;

namespace WhatSchedule.Documents;

/// <summary>
/// Reads the engine data of a text layer: the PostScript-like token stream Photoshop stores in the type tool data's
/// 'EngineData' member, with dictionaries (&lt;&lt; &gt;&gt;), arrays ([ ]), names (/Name), strings (( )) and numbers.
/// The stream's format is not part of Adobe's published file format specification, so parsing is best-effort and
/// never throws past the caller.
/// </summary>
internal static class PsdEngineData
{
	/// <summary>Parses <paramref name="data"/> and picks out the editor's text, the first paragraph's justification and the first style run.</summary>
	public static (int Justification, PsdTextStyle? Style, string? Text) Parse(byte[] data)
	{
		try
		{
			if (new Parser(data).ParseValue() is not Dictionary<string, object?> root)
				return (0, null, null);
			var engine = Member(root, "EngineDict") ?? root;

			var text = String(Member(engine, "Editor"), "Text");

			var justification = 0;
			if (Number(Member(Member(First(Member(engine, "ParagraphRun"), "RunArray"), "ParagraphSheet"), "Properties"), "Justification") is { } justified)
				justification = (int)justified;

			PsdTextStyle? style = null;
			if (Member(First(Member(engine, "StyleRun"), "RunArray"), "StyleSheet") is { } styleSheet &&
				Member(styleSheet, "StyleSheetData") is { } sheet)
			{
				PsdColor? fillColor = null;
				var fillAlpha = (byte)255;
				if (Member(sheet, "FillColor") is { } color &&
					color.TryGetValue("Values", out var values) && values is List<object?> list && list.Count >= 3 &&
					list[0] is double red && list[1] is double green && list[2] is double blue)
				{
					fillColor = new PsdColor(ToByte(red), ToByte(green), ToByte(blue));
					if (list.Count >= 4 && list[3] is double alpha)
						fillAlpha = ToByte(alpha);
				}

				style = new PsdTextStyle(
					FontName(root, (int)(Number(sheet, "Font") ?? 0)),
					Float(sheet, "FontSize"),
					Flag(sheet, "FauxBold", false),
					Flag(sheet, "FauxItalic", false),
					fillColor,
					fillAlpha,
					Float(sheet, "Tracking"),
					Float(sheet, "HorizontalScale"),
					Float(sheet, "Leading"),
					Flag(sheet, "AutoLeading", false));
			}

			return (justification, style, text);
		}
		catch (InvalidDataException)
		{
			return (0, null, null);
		}
	}

	/// <summary>The font at <paramref name="index"/> in the engine data's font set, clamped into it and mapped onto an installed font family when one matches; the set sits in the root's resource or document dictionary.</summary>
	private static string? FontName(Dictionary<string, object?> root, int index)
	{
		var set = Raw(Member(root, "ResourceDict"), "FontSet") ?? Raw(Member(root, "DocumentResources"), "FontSet") ?? Raw(root, "FontSet");
		if (set is not List<object?> fonts || fonts.Count == 0)
			return null;
		return ResolveSystemFont(String(fonts[Math.Clamp(index, 0, fonts.Count - 1)] as Dictionary<string, object?>, "Name"));
	}

	private static string[]? fontFamilies;

	/// <summary>The font families installed on the machine, enumerated once.</summary>
	private static string[] FontFamilies => fontFamilies ??= SKFontManager.Default.GetFontFamilies();

	/// <summary>
	/// Maps a font name from the file onto an installed font family: the name itself when a family carries it exactly, else the
	/// family whose whitespace-free name equals the stored name's, else the stored name unchanged. PostScript names drop
	/// whitespace, so "SadanaSquare" comes back as the installed "Sadana Square".
	/// </summary>
	private static string? ResolveSystemFont(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return name;
		var families = FontFamilies;
		if (Array.IndexOf(families, name) >= 0)
			return name;
		var stripped = WithoutWhitespace(name);
		foreach (var family in families)
			if (WithoutWhitespace(family) == stripped)
				return family;
		return name;
	}

	/// <summary>The name with its whitespace characters removed.</summary>
	private static string WithoutWhitespace(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var c in value)
			if (!char.IsWhiteSpace(c))
				builder.Append(c);
		return builder.ToString();
	}

	/// <summary>A color channel given as a fraction of 1, as a byte.</summary>
	private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

	/// <summary>The value stored under <paramref name="key"/>, when the container is a dictionary holding anything there.</summary>
	private static object? Raw(Dictionary<string, object?>? dict, string key)
		=> dict is not null && dict.TryGetValue(key, out var value) ? value : null;

	/// <summary>The dictionary stored under <paramref name="key"/>, when the container is one and holds a dictionary there.</summary>
	private static Dictionary<string, object?>? Member(object? container, string key)
		=> container is Dictionary<string, object?> dict && dict.TryGetValue(key, out var value) ? value as Dictionary<string, object?> : null;

	/// <summary>The first item of the list stored under <paramref name="key"/>, when the container is a dictionary holding a non-empty list there.</summary>
	private static object? First(object? container, string key)
		=> container is Dictionary<string, object?> dict && dict.TryGetValue(key, out var value) && value is List<object?> { Count: > 0 } list ? list[0] : null;

	/// <summary>The number stored under <paramref name="key"/>, when there is one.</summary>
	private static double? Number(Dictionary<string, object?>? dict, string key)
		=> dict is not null && dict.TryGetValue(key, out var value) && value is double number ? number : null;

	/// <summary>The number stored under <paramref name="key"/> as a float, when there is one.</summary>
	private static float? Float(Dictionary<string, object?>? dict, string key)
		=> Number(dict, key) is { } value ? (float)value : null;

	/// <summary>The boolean stored under <paramref name="key"/>, or <paramref name="fallback"/> when there is none.</summary>
	private static bool Flag(Dictionary<string, object?>? dict, string key, bool fallback)
		=> dict is not null && dict.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;

	/// <summary>The string stored under <paramref name="key"/>, when there is one.</summary>
	private static string? String(Dictionary<string, object?>? dict, string key)
		=> Raw(dict, key) as string;

	/// <summary>A tokenizer over the engine data's bytes, building dictionaries, lists, names, strings, numbers and booleans; throws <see cref="InvalidDataException"/> on malformed input.</summary>
	private sealed class Parser
	{
		private readonly byte[] data;
		private int position;

		public Parser(byte[] data)
		{
			this.data = data;
		}

		private bool End => position >= data.Length;

		/// <summary>Reads the next value: a dictionary, a list, a name, a string or a bare token.</summary>
		public object? ParseValue()
		{
			SkipFiller();
			if (End)
				throw new InvalidDataException("The engine data ends before its value does.");
			var c = data[position];
			if (c == '<' && Peek() == '<')
			{
				position += 2;
				return ParseDictionary();
			}
			if (c == '[')
			{
				position++;
				return ParseList();
			}
			if (c == '/')
			{
				position++;
				return ReadName();
			}
			if (c == '(')
				return ReadString();
			if (IsDelimiter(c))
				throw new InvalidDataException("The engine data holds a stray delimiter.");
			return ParseBare();
		}

		private byte Peek() => position + 1 < data.Length ? data[position + 1] : (byte)0;

		private static bool IsDelimiter(byte c) => char.IsWhiteSpace((char)c) || c is (byte)'/' or (byte)'(' or (byte)')' or (byte)'[' or (byte)']' or (byte)'<' or (byte)'>';

		/// <summary>Skips whitespace and %-comments.</summary>
		private void SkipFiller()
		{
			while (!End)
			{
				var c = data[position];
				if (char.IsWhiteSpace((char)c))
					position++;
				else if (c == '%')
					while (!End && data[position] != '\n' && data[position] != '\r')
						position++;
				else
					break;
			}
		}

		private Dictionary<string, object?> ParseDictionary()
		{
			var dict = new Dictionary<string, object?>();
			while (true)
			{
				SkipFiller();
				if (End)
					throw new InvalidDataException("The engine data ends inside a dictionary.");
				if (data[position] == '>' && Peek() == '>')
				{
					position += 2;
					return dict;
				}
				if (data[position] != '/')
					throw new InvalidDataException("A dictionary member does not start with a name.");
				position++;
				dict[ReadName()] = ParseValue();
			}
		}

		private List<object?> ParseList()
		{
			var list = new List<object?>();
			while (true)
			{
				SkipFiller();
				if (End)
					throw new InvalidDataException("The engine data ends inside a list.");
				if (data[position] == ']')
				{
					position++;
					return list;
				}
				list.Add(ParseValue());
			}
		}

		/// <summary>Reads a name up to the next delimiter.</summary>
		private string ReadName()
		{
			var start = position;
			while (!End && !IsDelimiter(data[position]))
				position++;
			return Encoding.Latin1.GetString(data, start, position - start);
		}

		/// <summary>Reads a bare token and converts it to a number or a boolean when it is one.</summary>
		private object ParseBare()
		{
			var token = ReadName();
			if (token == "true")
				return true;
			if (token == "false")
				return false;
			if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
				return number;
			return token;
		}

		/// <summary>Reads a parenthesized string, honoring nesting and backslash escapes, and decodes it as UTF-16 when it carries a byte order mark.</summary>
		private string ReadString()
		{
			position++; //The opening parenthesis.
			var bytes = new List<byte>();
			var depth = 1;
			while (!End)
			{
				var c = data[position++];
				if (c == '\\' && !End)
				{
					ReadEscape(bytes);
					continue;
				}
				if (c == '(')
					depth++;
				else if (c == ')')
				{
					depth--;
					if (depth == 0)
						break;
				}
				bytes.Add(c);
			}
			if (depth > 0)
				throw new InvalidDataException("The engine data ends inside a string.");
			return Decode(bytes.ToArray());
		}

		/// <summary>Reads the character after a backslash: a named escape, an octal code, a line continuation, or the character itself.</summary>
		private void ReadEscape(List<byte> bytes)
		{
			var escape = data[position++];
			switch (escape)
			{
				case (byte)'n': bytes.Add((byte)'\n'); break;
				case (byte)'r': bytes.Add((byte)'\r'); break;
				case (byte)'t': bytes.Add((byte)'\t'); break;
				case (byte)'b': bytes.Add(8); break;
				case (byte)'f': bytes.Add(12); break;
				case (byte)'\n' or (byte)'\r': break; //Line continuation.
				default:
					if (escape is >= (byte)'0' and <= (byte)'7')
					{
						var value = escape - '0';
						var digits = 1;
						while (digits < 3 && !End && data[position] is >= (byte)'0' and <= (byte)'7')
						{
							value = value * 8 + (data[position] - '0');
							position++;
							digits++;
						}
						bytes.Add((byte)Math.Clamp(value, 0, 255));
					}
					else
						bytes.Add(escape);
					break;
			}
		}

		/// <summary>Decodes a string's bytes as UTF-16 when they start with a byte order mark, else as Latin-1.</summary>
		private static string Decode(byte[] bytes)
		{
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
				return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
				return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			return Encoding.Latin1.GetString(bytes);
		}
	}
}
