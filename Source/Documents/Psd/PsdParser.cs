// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Buffers.Binary;
using System.Text;

namespace WhatSchedule.Documents;

/// <summary>A rectangle in Photoshop canvas pixels, stored as its four edges the way the format does.</summary>
public readonly record struct PsdRect(int Left, int Top, int Right, int Bottom)
{
	/// <summary>The rectangle's width in pixels.</summary>
	public int Width => Right - Left;

	/// <summary>The rectangle's height in pixels.</summary>
	public int Height => Bottom - Top;
}

/// <summary>What a layer record represents in the layer tree of a Photoshop file.</summary>
public enum PsdDivider
{
	/// <summary>A normal layer with pixels.</summary>
	None,

	/// <summary>A group's folder record; in the file, which lists layers bottom first, it follows the group's contents.</summary>
	Folder,

	/// <summary>A group's end record; in the file, which lists layers bottom first, it precedes the group's contents.</summary>
	End,
}

/// <summary>A color the format stores as a descriptor: straight RGB bytes.</summary>
public readonly record struct PsdColor(byte R, byte G, byte B);

/// <summary>A layer's color overlay style: the color painted over the layer, its opacity in 255ths, and its Photoshop blend key.</summary>
public sealed record PsdOverlay(PsdColor Color, byte Opacity, string BlendKey);

/// <summary>A single layer read from a Photoshop file; <see cref="Rgba"/> is null for group boundary records and layers without pixels or a fill color, e.g. adjustment layers, and <see cref="Text"/> is set for text layers.</summary>
public sealed record PsdLayer(PsdRect Rect, string Name, byte Opacity, string BlendKey, bool Hidden, PsdDivider Divider, byte[]? Rgba, PsdColor? Fill, PsdOverlay? Overlay, PsdText? Text);

/// <summary>The layers of a Photoshop file.</summary>
public sealed record PsdDocument(int Width, int Height, IReadOnlyList<PsdLayer> Layers);

/// <summary>
/// Reads the layers of a Photoshop (PSD) file, written from Adobe's published file format specification: layer records
/// with their channel data, names, blend keys and group boundaries. A layer mask is multiplied into the layer's alpha
/// channel, since the importer has no separate mask concept. Only 8-bit RGB and grayscale documents are supported.
/// </summary>
public static class PsdParser
{
	/// <summary>Parses the layers of <paramref name="file"/>, which must start with the Photoshop file signature.</summary>
	public static PsdDocument Parse(byte[] file)
	{
		var reader = new PsdReader(file);

		//File header.
		if (reader.Ascii(4) != "8BPS")
			throw new InvalidDataException("The file does not start with the Photoshop signature.");
		if (reader.UInt16() != 1)
			throw new InvalidDataException("Photoshop large document (PSB) files are not supported.");
		reader.Skip(6);
		reader.UInt16(); //Channel count of the merged image.
		var height = (int)reader.UInt32();
		var width = (int)reader.UInt32();
		if (reader.UInt16() != 8)
			throw new InvalidDataException("Only 8-bit-per-channel documents are supported.");
		var colorMode = reader.UInt16();
		if (colorMode is not 1 and not 3)
			throw new InvalidDataException("Only RGB and grayscale documents are supported.");

		//The color mode data and image resources hold palettes, thumbnails and such; the layers do not need them.
		reader.Skip(reader.SectionLength());
		reader.Skip(reader.SectionLength());
		reader.UInt32(); //The layer and mask section's length, which the layers end; the merged image data after it is not read.

		return new PsdDocument(width, height, ReadLayers(reader, colorMode));
	}

	/// <summary>Reads the layer records and their channel data, which the file stores after the records.</summary>
	private static IReadOnlyList<PsdLayer> ReadLayers(PsdReader reader, uint colorMode)
	{
		if (reader.SectionLength() == 0)
			return Array.Empty<PsdLayer>();

		var count = Math.Abs(reader.Int16());
		var records = new List<PsdLayerRecord>(count);
		for (var i = 0; i < count; i++)
			records.Add(PsdLayerRecord.Read(reader));

		var layers = new List<PsdLayer>(count);
		foreach (var record in records)
		{
			var channels = new Dictionary<int, byte[]>(record.Channels.Count);
			foreach (var channel in record.Channels)
			{
				var data = record.ReadChannel(reader, channel);
				if (data is not null)
					channels[channel.Id] = data;
			}
			layers.Add(record.ToLayer(colorMode, channels));
		}

		return layers;
	}

	/// <summary>Decodes one PackBits run of a scanline, as documented by the format: a non-negative header copies the following count + 1 bytes, a negative one repeats the following byte 1 - count times.</summary>
	private static void DecodeRleRow(ReadOnlySpan<byte> packed, Span<byte> row)
	{
		var source = 0;
		var target = 0;
		while (source < packed.Length && target < row.Length)
		{
			var control = (sbyte)packed[source++];
			if (control >= 0)
			{
				var count = Math.Min(control + 1, row.Length - target);
				packed.Slice(source, count).CopyTo(row.Slice(target));
				source += count;
				target += count;
			}
			else if (control != -128)
			{
				var count = Math.Min(1 - control, row.Length - target);
				row.Slice(target, count).Fill(packed[source]);
				source += 1;
				target += count;
			}
		}
	}

	/// <summary>
	/// Reads a descriptor, the format's generic key/type/value structure: a class, an item count, then members. Only the types
	/// the fill color, the color overlay and the type tool data need are understood; objects and lists recurse, scalars convert
	/// to plain .NET values, and raw data ('alis'/'tdta', e.g. the engine data) comes back as its bytes.
	/// </summary>
	private static Dictionary<string, object?> ReadDescriptor(PsdReader reader)
	{
		reader.UnicodeString(); //The descriptor's name.
		reader.Key(); //The descriptor's class.
		var count = (int)reader.UInt32();
		var members = new Dictionary<string, object?>(Math.Max(0, count));
		for (var i = 0; i < count; i++)
			members[reader.Key()] = ReadDescriptorValue(reader, reader.Ascii(4));
		return members;
	}

	/// <summary>Reads one descriptor value; unsupported value types throw, which the best-effort fill and overlay readers turn into a skipped feature.</summary>
	private static object? ReadDescriptorValue(PsdReader reader, string type)
	{
		switch (type)
		{
			case "Obj ": //Object.
				return ReadDescriptor(reader);
			case "VlLs": //List.
				var count = (int)reader.UInt32();
				var items = new List<object?>(Math.Max(0, count));
				for (var i = 0; i < count; i++)
					items.Add(ReadDescriptorValue(reader, reader.Ascii(4)));
				return items;
			case "doub": //Double.
				return reader.Float64();
			case "UntF": //Unit double; the units the fill color needs are percents, which need no conversion.
				reader.Ascii(4);
				return reader.Float64();
			case "UnFl": //Unit float.
				reader.Ascii(4);
				return (double)reader.Float32();
			case "TEXT": //String.
				return reader.UnicodeString();
			case "enum": //Enumerated; the importer only needs the value, e.g. the blend key of a color overlay.
				reader.Key();
				return reader.Key();
			case "long": //Integer.
				return reader.Int32();
			case "comp": //Large integer.
				reader.Skip(8);
				return null;
			case "bool": //Boolean.
				return reader.UInt8() != 0;
			case "alis" or "tdta": //Alias and raw data: a byte count followed by the bytes.
				return reader.Bytes(reader.Int32()).ToArray();
			case "type" or "GlbC": //Class.
				reader.UnicodeString();
				reader.Key();
				return null;
			default:
				throw new InvalidDataException("The descriptor value type '" + type + "' is not supported.");
		}
	}

	/// <summary>Reads the RGB color of a 'Clr ' member: an object whose 'Rd  ', 'Grn ' and 'Bl  ' members hold the channel values; null when the color is not RGB.</summary>
	private static PsdColor? ReadDescriptorColor(Dictionary<string, object?> members)
	{
		if (!members.TryGetValue("Clr ", out var value) || value is not Dictionary<string, object?> color)
			return null;

		if (color.TryGetValue("Rd  ", out var red) && red is double r &&
			color.TryGetValue("Grn ", out var green) && green is double g &&
			color.TryGetValue("Bl  ", out var blue) && blue is double b)
			return new PsdColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));

		return null;
	}

	/// <summary>Reads a fill layer's solid color block ('SoCo'): a version, a descriptor version, then a descriptor carrying the fill color in its 'Clr ' member.</summary>
	private static PsdColor? ReadFillColor(PsdReader reader)
	{
		reader.Skip(8); //The block's and the descriptor's versions.
		return ReadDescriptorColor(ReadDescriptor(reader));
	}

	/// <summary>
	/// Reads a layer effects block ('lfx2' or 'lmfx') and picks out the color overlay: the single 'SoFi' effect, or the first of
	/// the multiple 'solidFillMulti' ones newer Photoshop versions write. Returns null when the layer has no color overlay.
	/// </summary>
	private static PsdOverlay? ReadColorOverlay(PsdReader reader)
	{
		reader.Skip(8); //The block's and the descriptor's versions.
		var effects = ReadDescriptor(reader);

		Dictionary<string, object?>? effect = null;
		if (effects.TryGetValue("SoFi", out var single) && single is Dictionary<string, object?> singleEffect)
			effect = singleEffect;
		else if (effects.TryGetValue("solidFillMulti", out var multiple) && multiple is List<object?> { Count: > 0 } effectList && effectList[0] is Dictionary<string, object?> firstEffect)
			effect = firstEffect;

		if (effect is null)
			return null;

		if (effect.TryGetValue("enab", out var enable) && enable is bool enabled && !enabled)
			return null;

		var color = ReadDescriptorColor(effect);
		if (color is null)
			return null;

		var opacity = 255;
		if (effect.TryGetValue("Opct", out var percent) && percent is double value)
			opacity = (byte)Math.Round(Math.Clamp(value, 0, 100) * 2.55);

		var blendKey = effect.TryGetValue("Md  ", out var mode) && mode is string key ? key : "norm";
		return new PsdOverlay(color.Value, (byte)opacity, blendKey);
	}

	/// <summary>
	/// Reads a text layer's type tool data ('TySh' or 'tySh'), as the format stores it: a version, the text's affine
	/// transform (of which the rotation and anchor are kept), then the text descriptor whose 'EngineData' member carries
	/// the style, then the warp descriptor and the text box's bounds in the text's local coordinates. The layer record's
	/// own bounds carry the rendered pixels' extent.
	/// </summary>
	private static PsdText ReadTypeTool(PsdReader reader)
	{
		reader.UInt16(); //Version.
		var xx = reader.Float64(); //The text's affine transform, xx, xy, yx, yy, tx, ty.
		var xy = reader.Float64();
		reader.Skip(16);
		var tx = reader.Float64();
		var ty = reader.Float64();
		var rotation = Math.Atan2(xy, xx) * 180 / Math.PI; //Photoshop's angle is atan2(xy, xx), clockwise-positive on screen.
		reader.UInt16(); //The text data's version.
		reader.UInt32(); //The text descriptor's version.
		var descriptor = ReadDescriptor(reader);
		reader.UInt16(); //The warp data's version.
		reader.UInt32(); //The warp descriptor's version.
		ReadDescriptor(reader); //The warp, which the importer does not reproduce.
		var box = new PsdRect(reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());

		var text = descriptor.TryGetValue("Txt ", out var content) && content is string value ? value : string.Empty;
		var vertical = descriptor.TryGetValue("Ornt", out var orientation) && orientation is string o && o == "Vrtc";
		var (justification, style, engineText) = descriptor.TryGetValue("EngineData", out var engine) && engine is byte[] raw
			? PsdEngineData.Parse(raw)
			: (0, null, null);
		if (text.Length == 0 && engineText is { Length: > 0 } fallback)
			text = fallback;
		return new PsdText(text, box, vertical, justification, style, rotation, tx, ty);
	}

	/// <summary>A big-endian reader over a Photoshop file's bytes, throwing <see cref="InvalidDataException"/> past the end of the file; a class, so its read position advances for everyone it is passed to.</summary>
	private sealed class PsdReader
	{
		private readonly byte[] data;

		public PsdReader(byte[] file)
		{
			if (file.LongLength > int.MaxValue)
				throw new InvalidDataException("The file is too large to read.");
			data = file;
			Position = 0;
		}

		/// <summary>The read position in the file, in bytes.</summary>
		public int Position { get; private set; }

		/// <summary>The bytes between the read position and the end of the file.</summary>
		public int Remaining => data.Length - Position;

		private void Check(int count)
		{
			if (Position < 0 || count < 0 || Position + count > data.Length)
				throw new InvalidDataException("The file ends before its layer data does.");
		}

		/// <summary>Reads a section length that must fit into the file and returns it as an int, so it can safely bound later reads.</summary>
		public int SectionLength()
		{
			var length = UInt32();
			if (length > (uint)Remaining)
				throw new InvalidDataException("A section's length points outside the file.");
			return (int)length;
		}

		/// <summary>Skips <paramref name="count"/> bytes without reading them.</summary>
		public void Skip(int count)
		{
			Check(count);
			Position += count;
		}

		/// <summary>Moves the read position to <paramref name="position"/>, which must lie inside the file and ahead of the current position.</summary>
		public void Seek(int position)
		{
			if (position < Position)
				throw new InvalidDataException("A block's length points before the start of its data.");
			Check(position - Position);
			Position = position;
		}

		public byte UInt8()
		{
			Check(1);
			return data[Position++];
		}

		public ushort UInt16()
		{
			Check(2);
			var value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(Position, 2));
			Position += 2;
			return value;
		}

		public short Int16() => (short)UInt16();

		public uint UInt32()
		{
			Check(4);
			var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(Position, 4));
			Position += 4;
			return value;
		}

		public int Int32() => (int)UInt32();

		public float Float32()
		{
			Check(4);
			var value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(Position, 4));
			Position += 4;
			return value;
		}

		public double Float64()
		{
			Check(8);
			var value = BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(Position, 8));
			Position += 8;
			return value;
		}

		/// <summary>Reads a descriptor key or class id: a length-prefixed ASCII string whose zero length stands for a four character code.</summary>
		public string Key()
		{
			var length = Int32();
			return length == 0 ? Ascii(4) : Ascii(length);
		}

		/// <summary>Reads <paramref name="length"/> ASCII characters.</summary>
		public string Ascii(int length)
		{
			Check(length);
			var text = Encoding.ASCII.GetString(data, Position, length);
			Position += length;
			return text;
		}

		/// <summary>Reads a length-prefixed big-endian UTF-16 string, as the format stores names, without its trailing null.</summary>
		public string UnicodeString()
		{
			var count = (int)UInt32();
			Check(count);
			var builder = new StringBuilder(Math.Min(count, 256));
			for (var i = 0; i < count; i++)
				builder.Append((char)UInt16());
			return builder.ToString().TrimEnd('\0');
		}

		/// <summary>Returns the next <paramref name="count"/> bytes without interpreting them.</summary>
		public ReadOnlySpan<byte> Bytes(int count)
		{
			Check(count);
			var bytes = data.AsSpan(Position, count);
			Position += count;
			return bytes;
		}
	}

	/// <summary>A channel's id (-1 is the layer's alpha, -2 its mask, 0 and up its colors) and the length of its data.</summary>
	private readonly record struct ChannelInfo(int Id, int Length);

	/// <summary>A layer's mask: its bounds on the canvas, the color that fills the canvas outside them, and its state.</summary>
	private readonly record struct MaskInfo(PsdRect Rect, byte DefaultColor, bool Disabled, bool Inverted);

	/// <summary>A layer record as it lies in the file, before its channel data has been read.</summary>
	private sealed class PsdLayerRecord
	{
		public PsdRect Rect { get; private set; }

		public string Name { get; private set; } = string.Empty;

		public byte Opacity { get; private set; }

		public string BlendKey { get; private set; } = "norm";

		public bool Hidden { get; private set; }

		public PsdDivider Divider { get; private set; } = PsdDivider.None;

		public List<ChannelInfo> Channels { get; } = new();

		public MaskInfo? Mask { get; private set; }

		/// <summary>The layer's solid fill color, when it is a fill layer ('SoCo'); a layer without pixel data draws this color instead.</summary>
		public PsdColor? Fill { get; private set; }

		/// <summary>The layer's color overlay style ('SoFi' inside 'lfx2' or 'lmfx'), when it has one.</summary>
		public PsdOverlay? Overlay { get; private set; }

		/// <summary>The layer's text content and style ('TySh' or 'tySh'), when it is a text layer.</summary>
		public PsdText? Text { get; private set; }

		/// <summary>Whether the layer's shape comes from vector paths ('vmsk' or 'vsms'), which the importer cannot reproduce, so the layer stays skipped even with a fill color.</summary>
		public bool HasVectorMask { get; private set; }

		/// <summary>Reads one layer record: its bounds, channels, appearance, mask, name and additional info.</summary>
		public static PsdLayerRecord Read(PsdReader reader)
		{
			var record = new PsdLayerRecord();
			var top = reader.Int32();
			var left = reader.Int32();
			var bottom = reader.Int32();
			var right = reader.Int32();
			record.Rect = new PsdRect(left, top, right, bottom);

			var channelCount = reader.UInt16();
			for (var i = 0; i < channelCount; i++)
				record.Channels.Add(new ChannelInfo(reader.Int16(), reader.SectionLength()));

			if (reader.Ascii(4) is not "8BIM" and not "8B64")
				throw new InvalidDataException("A layer record does not start with the Photoshop signature.");
			record.BlendKey = reader.Ascii(4);
			record.Opacity = reader.UInt8();
			reader.UInt8(); //Clipping: whether the layer clips the layer below it.
			record.Hidden = (reader.UInt8() & 0x02) != 0;
			reader.UInt8(); //Filler byte.

			var extraLength = reader.SectionLength();
			var extraEnd = reader.Position + extraLength;
			record.ReadMask(reader);
			reader.Skip(reader.SectionLength()); //Blending ranges.
			record.ReadName(reader);
			record.ReadAdditionalInfo(reader, extraEnd);
			reader.Seek(extraEnd);
			return record;
		}

		/// <summary>Reads the layer's mask record, which carries the mask's bounds and the fill color outside them; the record's length is padded past the data the importer needs.</summary>
		private void ReadMask(PsdReader reader)
		{
			var length = reader.SectionLength();
			if (length == 0)
				return;

			var end = reader.Position + length;
			if (length >= 18)
			{
				var top = reader.Int32();
				var left = reader.Int32();
				var bottom = reader.Int32();
				var right = reader.Int32();
				var defaultColor = reader.UInt8();
				var flags = reader.UInt8();
				Mask = new MaskInfo(new PsdRect(left, top, right, bottom), defaultColor, (flags & 0x02) != 0, (flags & 0x04) != 0);
			}
			reader.Seek(end);
		}

		/// <summary>Reads the layer's Pascal string name, padded to a multiple of four bytes.</summary>
		private void ReadName(PsdReader reader)
		{
			var length = reader.UInt8();
			if (length > 0)
				Name = reader.Ascii(length);
			reader.Skip((4 - ((length + 1) % 4)) % 4);
		}

		/// <summary>Reads the record's trailing blocks, of which the format stores many; the importer needs the Unicode name and the group boundaries.</summary>
		private void ReadAdditionalInfo(PsdReader reader, int extraEnd)
		{
			while (reader.Position + 12 <= extraEnd)
			{
				if (reader.Ascii(4) is not "8BIM" and not "8B64")
				{
					reader.Seek(extraEnd); //Trailing data written by another editor; the record ends here.
					return;
				}

				var key = reader.Ascii(4);
				var length = reader.SectionLength();
				var blockEnd = reader.Position + length + length % 2; //Blocks are padded to an even byte count.
				switch (key)
				{
					case "luni":
						Name = reader.UnicodeString();
						break;
					case "lsct" or "lsdk":
						Divider = reader.UInt32() switch
						{
							1 or 2 => PsdDivider.Folder,
							3 => PsdDivider.End,
							_ => PsdDivider.None,
						};
						break;
					case "SoCo":
						TryRead(reader, () => Fill = ReadFillColor(reader));
						break;
					case "lfx2" or "lmfx":
						TryRead(reader, () => Overlay = ReadColorOverlay(reader));
						break;
					case "TySh" or "tySh":
						TryRead(reader, () => Text = ReadTypeTool(reader));
						break;
					case "vmsk" or "vsms":
						HasVectorMask = true;
						break;
				}
				reader.Seek(blockEnd);
			}
		}

		/// <summary>Reads a best-effort feature: a failure abandons only that feature, since the block's recorded end realigns the read afterwards.</summary>
		private static void TryRead(PsdReader reader, Action read)
		{
			try
			{
				read();
			}
			catch (InvalidDataException)
			{
			}
			catch (ArgumentException)
			{
			}
		}

		/// <summary>
		/// Reads and decompresses the channel's pixels; color and alpha channels cover the layer's bounds, the mask channel its own.
		/// Returns null for channels without data, e.g. adjustment layers'.
		/// </summary>
		public byte[]? ReadChannel(PsdReader reader, ChannelInfo channel)
		{
			if (channel.Length == 0 || (channel.Id == -2 && Mask is null))
			{
				reader.Skip(channel.Length);
				return null;
			}

			var bounds = channel.Id == -2 ? Mask!.Value.Rect : Rect;
			var end = reader.Position + channel.Length;
			var width = bounds.Width;
			var height = bounds.Height;
			var pixels = new byte[width * height];

			switch (reader.UInt16())
			{
				case 0: //Raw.
					reader.Bytes(width * height).CopyTo(pixels);
					break;
				case 1: //PackBits, with a table of the packed size of every scanline.
					var rowSizes = new int[height];
					for (var y = 0; y < height; y++)
						rowSizes[y] = reader.UInt16();
					for (var y = 0; y < height; y++)
						DecodeRleRow(reader.Bytes(rowSizes[y]), pixels.AsSpan(y * width, width));
					break;
				default:
					throw new InvalidDataException("ZIP-compressed layer channels are not supported.");
			}

			reader.Seek(end); //Every channel starts at its own recorded position, so decoding cannot desynchronize the file read.
			return pixels;
		}

		/// <summary>Builds the layer, composing its planar channels into straight RGBA pixels with the mask multiplied into the alpha.</summary>
		public PsdLayer ToLayer(uint colorMode, Dictionary<int, byte[]> channels)
		{
			return new PsdLayer(Rect, Name, Opacity, BlendKey, Hidden, Divider, ComposePixels(colorMode, channels), Fill, Overlay, Text);
		}

		/// <summary>Interleaves the layer's color and alpha channels; a present and enabled mask scales every pixel's alpha, and a fill layer's color stands in for missing pixels.</summary>
		private byte[]? ComposePixels(uint colorMode, Dictionary<int, byte[]> channels)
		{
			var width = Rect.Width;
			var height = Rect.Height;
			if (width <= 0 || height <= 0)
				return null;

			channels.TryGetValue(0, out var red);
			channels.TryGetValue(1, out var green);
			channels.TryGetValue(2, out var blue);
			channels.TryGetValue(-1, out var alpha);

			var grayscale = colorMode == 1;
			var fill = Fill;
			if (red is null && green is null && blue is null && (fill is null || HasVectorMask))
				return null; //Group boundaries, adjustment layers and vector shapes carry no pixels the importer can use.

			//A present and enabled mask scales the alpha; outside its bounds it counts as its fill color.
			MaskInfo maskInfo = default;
			byte[]? maskPixels = null;
			if (Mask is { Disabled: false } info)
			{
				maskInfo = info;
				channels.TryGetValue(-2, out maskPixels);
			}

			var rgba = new byte[width * height * 4];
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var index = y * width + x;
					var a = alpha?[index] ?? 255;
					if (maskPixels is not null)
					{
						var value = MaskValue(maskInfo, maskPixels, x, y);
						a = (byte)((a * value + 127) / 255);
					}

					var offset = index * 4;
					rgba[offset] = red?[index] ?? fill?.R ?? 0;
					rgba[offset + 1] = grayscale ? red?[index] ?? fill?.R ?? 0 : green?[index] ?? fill?.G ?? 0;
					rgba[offset + 2] = grayscale ? red?[index] ?? fill?.R ?? 0 : blue?[index] ?? fill?.B ?? 0;
					rgba[offset + 3] = a;
				}
			}

			return rgba;
		}

		/// <summary>The mask value covering the layer pixel at <paramref name="x"/> and <paramref name="y"/>: the mask's own pixel inside its bounds, its fill color outside them.</summary>
		private byte MaskValue(MaskInfo mask, byte[] pixels, int x, int y)
		{
			var canvasX = Rect.Left + x;
			var canvasY = Rect.Top + y;
			if (canvasX < mask.Rect.Left || canvasX >= mask.Rect.Right || canvasY < mask.Rect.Top || canvasY >= mask.Rect.Bottom)
				return mask.Inverted ? (byte)(255 - (mask.DefaultColor == 0 ? 0 : 255)) : mask.DefaultColor == 0 ? (byte)0 : (byte)255;

			var value = pixels[(canvasY - mask.Rect.Top) * mask.Rect.Width + (canvasX - mask.Rect.Left)];
			return mask.Inverted ? (byte)(255 - value) : value;
		}
	}
}
