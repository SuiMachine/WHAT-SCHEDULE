using SkiaSharp;

namespace WhatSchedule.Rendering.Definitions;

/// <summary>
/// Renders styled text overlays (fill, outline, shadow) using SkiaSharp.
/// </summary>
public static class ImageTextRenderer
{
	private static readonly Dictionary<string, SKTypeface> TypefaceCache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Measures the bounding box of the text described by <paramref name="options"/>,
	/// ignoring its position. The height covers the font's ascent and descent.
	/// </summary>
	public static SKSize Measure(TextOverlayOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		using var font = CreateFont(options);
		var width = MeasureWidth(font, options.Text);
		var metrics = font.Metrics;
		return new SKSize(width, -metrics.Ascent + metrics.Descent);
	}

	/// <summary>
	/// Draws the styled text described by <paramref name="options"/> onto
	/// <paramref name="canvas"/>. <see cref="TextOverlayOptions.Position"/> is
	/// the top-left corner of the text block.
	/// </summary>
	public static void Draw(SKCanvas canvas, TextOverlayOptions options)
	{
		ArgumentNullException.ThrowIfNull(canvas);
		ArgumentNullException.ThrowIfNull(options);

		using var font = CreateFont(options);
		var baseline = new SKPoint(options.Position.X, options.Position.Y - font.Metrics.Ascent);

		//A blend mode other than SrcOver must composite the whole label (shadow, outline, fill)
		//as one group, so it draws through a layer whose paint carries the mode.
		using var blendPaint = options.BlendMode == SKBlendMode.SrcOver ? null : new SKPaint { BlendMode = options.BlendMode };
		var layer = blendPaint is null ? 0 : canvas.SaveLayer(blendPaint);

		DrawShadow(canvas, options, font, baseline);
		DrawOutline(canvas, options, font, baseline);
		DrawFill(canvas, options, font, baseline);

		if (blendPaint is not null)
			canvas.RestoreToCount(layer);
	}

	/// <summary>
	/// Draws the text described by <paramref name="options"/> onto a copy of
	/// <paramref name="source"/>. The source bitmap is never modified.
	/// </summary>
	/// <returns>A new bitmap with the overlay applied.</returns>
	public static SKBitmap Apply(SKBitmap source, TextOverlayOptions options)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Text))
		{
			throw new ArgumentException("Overlay text must not be empty.", nameof(options));
		}

		var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
		using (var canvas = new SKCanvas(result))
		{
			canvas.DrawBitmap(source, 0, 0);
			Draw(canvas, options);
		}

		return result;
	}

	private static SKFont CreateFont(TextOverlayOptions options)
	{
		if (!TypefaceCache.TryGetValue(options.FontFamily, out var typeface))
		{
			typeface = SKTypeface.FromFamilyName(options.FontFamily) ?? SKTypeface.Default;
			TypefaceCache[options.FontFamily] = typeface;
		}

		return new SKFont(typeface, options.FontSize)
		{
			Edging = SKFontEdging.SubpixelAntialias,
			//Skia applies hinting, baseline snapping and whole-pixel glyph positions only while
			//the text matrix is axis-aligned, so at 0 degrees the glyph advances were quantized
			//to the pixel grid and at every other angle they were not, making the characters
			//shift a little as the label rotates. Linear, unsnapped, subpixel metrics are the
			//same at every angle, which keeps the rotation rigid.
			Hinting = SKFontHinting.None,
			BaselineSnap = false,
			LinearMetrics = true,
			Subpixel = true,
		};
	}

	private static float MeasureWidth(SKFont font, string text)
	{
		var glyphs = new ushort[font.CountGlyphs(text)];
		font.GetGlyphs(text, glyphs);
		return font.MeasureText(glyphs, null);
	}

	private static void DrawShadow(SKCanvas canvas, TextOverlayOptions options, SKFont font, SKPoint baseline)
	{
		if (options.ShadowColor.Alpha == 0)
		{
			return;
		}

		using var paint = new SKPaint
		{
			Color = options.ShadowColor,
			IsAntialias = true,
			ImageFilter = SKImageFilter.CreateBlur(options.ShadowBlur, options.ShadowBlur),
		};
		canvas.DrawText(
			options.Text,
			baseline.X + options.ShadowOffset.X,
			baseline.Y + options.ShadowOffset.Y,
			font,
			paint);
	}

	private static void DrawOutline(SKCanvas canvas, TextOverlayOptions options, SKFont font, SKPoint baseline)
	{
		if (options.OutlineWidth <= 0)
		{
			return;
		}

		using var paint = new SKPaint
		{
			Color = options.OutlineColor,
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = options.OutlineWidth,
			StrokeJoin = SKStrokeJoin.Round,
			StrokeCap = SKStrokeCap.Round,
		};
		canvas.DrawText(options.Text, baseline.X, baseline.Y, font, paint);
	}

	private static void DrawFill(SKCanvas canvas, TextOverlayOptions options, SKFont font, SKPoint baseline)
	{
		using var paint = new SKPaint
		{
			Color = options.TextColor,
			IsAntialias = true,
		};
		canvas.DrawText(options.Text, baseline.X, baseline.Y, font, paint);
	}
}
