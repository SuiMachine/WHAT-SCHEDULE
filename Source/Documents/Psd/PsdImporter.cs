// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Runtime.InteropServices;
using SkiaSharp;
using WhatSchedule.Elements;
using WhatSchedule.Rendering.Definitions;

namespace WhatSchedule.Documents;

/// <summary>What a Photoshop import produces: the file's layers as elements and the document background the file's size comes from.</summary>
public sealed record PsdImportResult(List<CanvasElement> Elements, SKBitmap Background);

/// <summary>Turns the layers of a Photoshop (PSD) file into canvas elements, the way the importer supports them: text layers become label elements, pixel layers become image elements, groups become group elements, hidden layers and layers without pixels are skipped, and layer masks arrive already baked into the alpha channel.</summary>
public static class PsdImporter
{
	/// <summary>Photoshop blend keys mapped onto the blend modes the inspector offers; every other key imports as normal blending.</summary>
	private static readonly Dictionary<string, SKBlendMode> BlendKeys = new()
	{
		["norm"] = SKBlendMode.SrcOver,
		["mul "] = SKBlendMode.Multiply,
		["scrn"] = SKBlendMode.Screen,
		["over"] = SKBlendMode.Overlay,
		["dark"] = SKBlendMode.Darken,
		["lite"] = SKBlendMode.Lighten,
		["diff"] = SKBlendMode.Difference,
		["smud"] = SKBlendMode.Exclusion,
		["hLit"] = SKBlendMode.HardLight,
		["sLit"] = SKBlendMode.SoftLight,
		["hue "] = SKBlendMode.Hue,
		["sat "] = SKBlendMode.Saturation,
		["colr"] = SKBlendMode.Color,
		["lum "] = SKBlendMode.Luminosity,
		["lddg"] = SKBlendMode.Plus,
	};

	/// <summary>
	/// Reads <paramref name="stream"/> into the elements the import adds, placed at their Photoshop canvas positions and bottom layer first,
	/// and the background the document takes, sized after the Photoshop canvas. Throws <see cref="InvalidDataException"/> when the file is
	/// not a Photoshop document the parser supports.
	/// </summary>
	public static PsdImportResult Import(Stream stream)
	{
		byte[] file;
		using (var memory = new MemoryStream())
		{
			stream.CopyTo(memory);
			file = memory.ToArray();
		}

		return Import(PsdParser.Parse(file));
	}

	/// <summary>Places the parsed layers into the element tree, in the file's bottom-first order, and builds the document background.</summary>
	public static PsdImportResult Import(PsdDocument psd)
	{
		var backgroundLayer = FindBackgroundLayer(psd);
		var root = new List<CanvasElement>();
		var stack = new Stack<List<CanvasElement>>();

		void Place(CanvasElement element)
		{
			if (stack.Count > 0)
				stack.Peek().Add(element);
			else
				root.Add(element);
		}

		foreach (var layer in psd.Layers)
		{
			switch (layer.Divider)
			{
				case PsdDivider.End:
					stack.Push(new List<CanvasElement>());
					break;
				case PsdDivider.Folder:
					//The folder record closes the group; its contents are the records collected since the group's end record.
					var children = stack.Count > 0 ? stack.Pop() : new List<CanvasElement>();
					if (children.Count > 0)
					{
						var group = new GroupElement { Name = layer.Name, IsHidden = layer.Hidden };
						foreach (var child in children)
							group.InsertChild(child, group.Children.Count);
						group.RebaseToContentBounds();
						Place(group);
					}
					break;
				default:
					//The background layer becomes the document background instead of an element; a layer hidden in
					//the file arrives hidden, holding its pixels and place until the hierarchy view shows it again.
					if (!ReferenceEquals(layer, backgroundLayer) && CreateElement(layer) is { } element)
					{
						if (layer.Hidden)
							element.IsHidden = true;
						Place(element);
					}
					break;
			}
		}

		//Groups whose folder record is missing collapse back into what contains them.
		while (stack.Count > 0)
		{
			foreach (var element in stack.Pop())
				Place(element);
		}

		return new PsdImportResult(root, CreateBackground(psd, backgroundLayer));
	}

	/// <summary>
	/// Finds the file's flattened background layer: the bottom-most layer Photoshop names "Background" that spans the whole canvas.
	/// </summary>
	private static PsdLayer? FindBackgroundLayer(PsdDocument psd)
	{
		return psd.Layers.FirstOrDefault(layer =>
			layer.Divider == PsdDivider.None &&
			!layer.Hidden &&
			string.Equals(layer.Name, "Background", StringComparison.OrdinalIgnoreCase) &&
			layer.Rect.Left <= 0 && layer.Rect.Top <= 0 &&
			layer.Rect.Right >= psd.Width && layer.Rect.Bottom >= psd.Height);
	}

	/// <summary>Builds the document background from the background layer's pixels, cropped to the Photoshop canvas; a file without a background layer gets a blank canvas-sized one, so the document still takes the file's size.</summary>
	private static SKBitmap CreateBackground(PsdDocument psd, PsdLayer? layer)
	{
		var bitmap = new SKBitmap(new SKImageInfo(psd.Width, psd.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		if (layer?.Rgba is not { Length: > 0 } pixels)
			return bitmap;

		var canvasPixels = new byte[psd.Width * psd.Height * 4];
		for (var y = 0; y < psd.Height; y++)
		{
			var sourceY = y - layer.Rect.Top;
			if (sourceY < 0 || sourceY >= layer.Rect.Height)
				continue;

			for (var x = 0; x < psd.Width; x++)
			{
				var sourceX = x - layer.Rect.Left;
				if (sourceX < 0 || sourceX >= layer.Rect.Width)
					continue;

				var source = (sourceY * layer.Rect.Width + sourceX) * 4;
				var target = (y * psd.Width + x) * 4;
				canvasPixels[target] = pixels[source];
				canvasPixels[target + 1] = pixels[source + 1];
				canvasPixels[target + 2] = pixels[source + 2];
				canvasPixels[target + 3] = pixels[source + 3];
			}
		}

		Marshal.Copy(canvasPixels, 0, bitmap.GetPixels(), canvasPixels.Length);
		return bitmap;
	}

	/// <summary>Photoshop color overlay keys the element's tint feature can express; the overlay becomes the element's tint instead of altering the pixels.</summary>
	private static readonly Dictionary<string, SKBlendMode> OverlayTintModes = new()
	{
		["norm"] = SKBlendMode.SrcIn,
		["mul "] = SKBlendMode.Multiply,
		["scrn"] = SKBlendMode.Screen,
		["over"] = SKBlendMode.Overlay,
		["sLit"] = SKBlendMode.SoftLight,
		["hLit"] = SKBlendMode.HardLight,
		["lddg"] = SKBlendMode.Plus,
	};

	/// <summary>Photoshop color overlay keys the tint feature cannot express; these bake the overlay into the pixels on import.</summary>
	private static readonly Dictionary<string, SKBlendMode> OverlayBakeModes = new()
	{
		["dark"] = SKBlendMode.Darken,
		["lite"] = SKBlendMode.Lighten,
		["diff"] = SKBlendMode.Difference,
		["smud"] = SKBlendMode.Exclusion,
		["idiv"] = SKBlendMode.ColorBurn,
		["div "] = SKBlendMode.ColorDodge,
		["hue "] = SKBlendMode.Hue,
		["sat "] = SKBlendMode.Saturation,
		["colr"] = SKBlendMode.Color,
		["lum "] = SKBlendMode.Luminosity,
	};

	/// <summary>Builds the element for a layer: a text layer becomes a label, a pixel layer an image element; returns null for layers without anything to show.</summary>
	private static CanvasElement? CreateElement(PsdLayer layer)
	{
		//Vertical text has no label counterpart, so it stays a rasterized image.
		if (layer.Text is { } text && !text.Vertical)
			return CreateLabel(layer, text);
		return CreateImage(layer);
	}

	/// <summary>PostScript font name suffixes mapped onto the style cuts the inspector offers; a name without a known suffix keeps its whole name and the normal cut.</summary>
	private static readonly (string Suffix, SKFontStyle Style)[] FontNameCuts =
	{
		("-BoldItalic", SKFontStyle.BoldItalic),
		("-BoldOblique", SKFontStyle.BoldItalic),
		("-Bold", SKFontStyle.Bold),
		("-Italic", SKFontStyle.Italic),
		("-Oblique", SKFontStyle.Italic),
		("-Regular", SKFontStyle.Normal),
	};

	/// <summary>Splits a PostScript font name like "MyriadPro-BoldItalic" into the family name and the style cut.</summary>
	private static (string Family, SKFontStyle Cut) SplitFontName(string name)
	{
		foreach (var (suffix, cut) in FontNameCuts)
		{
			if (name.EndsWith(suffix, StringComparison.Ordinal))
				return (name[..^suffix.Length], cut);
		}
		return (name, SKFontStyle.Normal);
	}

	/// <summary>
	/// Builds the label element for a text layer: the file's text, font, size, color, alignment and text box mapped onto
	/// the label's features, at the layer's canvas position, with its opacity, blend mode and name.
	/// </summary>
	private static LabelElement CreateLabel(PsdLayer layer, PsdText text)
	{
		var label = new LabelElement
		{
			Text = text.Text.Replace('\r', '\n'),
			Opacity = layer.Opacity / 255f,
			BlendMode = BlendKeys.GetValueOrDefault(layer.BlendKey, SKBlendMode.SrcOver),
		};
		if (layer.Name.Length > 0)
			label.Name = layer.Name;

		//A text box from the file becomes the label's box; point text's empty box sizes the label to its text, at the layer's bounds.
		if (text.Box is { Width: > 0, Height: > 0 } box)
		{
			label.Position = new SKPoint(box.Left, box.Top);
			label.Width = box.Width;
			label.Height = box.Height;
		}
		else
			label.Position = new SKPoint(layer.Rect.Left, layer.Rect.Top);

		if (text.Style is { } style)
		{
			if (style.FontName is { Length: > 0 } fontName)
			{
				var (family, cut) = SplitFontName(fontName);
				label.FontFamily = family;
				var bold = cut == SKFontStyle.Bold || cut == SKFontStyle.BoldItalic || style.Bold;
				var italic = cut == SKFontStyle.Italic || cut == SKFontStyle.BoldItalic || style.Italic;
				label.FontStyle = (bold, italic) switch
				{
					(true, true) => SKFontStyle.BoldItalic,
					(true, false) => SKFontStyle.Bold,
					(false, true) => SKFontStyle.Italic,
					_ => SKFontStyle.Normal,
				};
			}
			if (style.FontSize is { } fontSize && fontSize > 0)
				label.FontSize = Math.Max(ThemeDefinition.MinFontSize, fontSize);
			if (style.FillColor is { } fill)
				label.TextColor = new SKColor(fill.R, fill.G, fill.B, style.FillAlpha);
			//Photoshop numbers the justification 0 left, 1 right, 2 center; the justify-all variants import as left.
			label.Alignment = text.Justification switch
			{
				1 => TextAlignment.Right,
				2 or 5 => TextAlignment.Center,
				_ => TextAlignment.Left,
			};
			if (style.Tracking is { } tracking) //Tracking is in thousandths of an em.
				label.LetterSpacing = Math.Max(0f, tracking / 1000f * label.FontSize);
			if (style.HorizontalScale is { } scale)
				label.Stretch = Math.Clamp(scale * 100f, 50f, 200f);
			if (style.AutoLeading)
				label.LineHeight = 1.2f; //Photoshop's automatic leading.
			else if (style.Leading is { } leading && label.FontSize > 0)
				label.LineHeight = Math.Clamp(leading / label.FontSize, 0.5f, 3f);
		}
		return label;
	}

	/// <summary>Builds the image element for a pixel layer, with its bounds, opacity, blend mode and name; returns null for empty layers.</summary>
	private static ImageElement? CreateImage(PsdLayer layer)
	{
		var width = layer.Rect.Width;
		var height = layer.Rect.Height;
		if (layer.Rgba is not { Length: > 0 } pixels || width <= 0 || height <= 0)
			return null;

		var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

		//The color overlay becomes the element's tint when the tint feature can express its blend mode; otherwise it bakes into the pixels.
		SKColor? tint = null;
		var tintMode = SKBlendMode.SrcOver;
		if (layer.Overlay is { } overlay)
		{
			var color = new SKColor(overlay.Color.R, overlay.Color.G, overlay.Color.B, overlay.Opacity);
			if (OverlayTintModes.TryGetValue(overlay.BlendKey, out var mode))
			{
				tint = color;
				tintMode = mode;
			}
			else if (OverlayBakeModes.TryGetValue(overlay.BlendKey, out var bakeMode))
			{
				bitmap.Dispose();
				bitmap = BakeOverlay(pixels, width, height, color, bakeMode);
			}
		}

		var element = new ImageElement(bitmap, new SKPoint(layer.Rect.Left, layer.Rect.Top))
		{
			Opacity = layer.Opacity / 255f,
			BlendMode = BlendKeys.GetValueOrDefault(layer.BlendKey, SKBlendMode.SrcOver),
		};
		if (layer.Name.Length > 0)
			element.Name = layer.Name;
		if (tint is { } tintColor)
		{
			element.TintColor = tintColor;
			element.TintMode = tintMode;
		}
		return element;
	}

	/// <summary>Composites the overlay color over the layer's pixels with <paramref name="blendMode"/>, baking the overlay into the imported image.</summary>
	private static SKBitmap BakeOverlay(byte[] pixels, int width, int height, SKColor color, SKBlendMode blendMode)
	{
		var baked = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		Marshal.Copy(pixels, 0, baked.GetPixels(), pixels.Length);

		using var canvas = new SKCanvas(baked);
		using var paint = new SKPaint { BlendMode = blendMode, Color = color };
		canvas.DrawRect(SKRect.Create(width, height), paint);
		return baked;
	}
}
