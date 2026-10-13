using SkiaSharp;

namespace WhatSchedule.Rendering.Definitions;

/// <summary>
/// Renders styled text overlays (fill, outline, second outline, drop shadow, glow) using SkiaSharp.
/// </summary>
public static class ImageTextRenderer
{
	private static readonly Dictionary<string, SKTypeface> TypefaceCache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Measures the bounding box of the text described by <paramref name="options"/>,
	/// ignoring its position. The height covers every line, scaled by the line height.
	/// </summary>
	public static SKSize Measure(TextOverlayOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		using var layout = BuildLayout(options);
		return new SKSize(layout.Width, layout.Height);
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

		using var layout = BuildLayout(options);

		//A blend mode other than SrcOver, whole-label opacity and the whole-label blur all
		//composite the label as one group, so it draws through a layer whose paint carries them.
		var composited = options.BlendMode != SKBlendMode.SrcOver || options.Opacity < 1 || options.Blur > 0;
		using var blurFilter = options.Blur > 0 ? SKImageFilter.CreateBlur(options.Blur, options.Blur) : null;
		using var layerPaint = composited
			? new SKPaint
			{
				BlendMode = options.BlendMode,
				Color = SKColors.White.WithAlpha((byte)Math.Round(byte.MaxValue * Math.Clamp(options.Opacity, 0, 1))),
				ImageFilter = blurFilter,
			}
			: null;
		var layer = composited ? canvas.SaveLayer(layerPaint) : 0;

		DrawGlow(canvas, options, layout);
		DrawShadow(canvas, options, layout);
		DrawOutline(canvas, options, layout, second: true);
		DrawOutline(canvas, options, layout, second: false);
		DrawFill(canvas, options, layout);

		if (composited)
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
		//The cache key carries the style too, so the bold and regular cuts of a family never collide.
		var cacheKey = $"{options.FontFamily}|{options.FontStyle.Weight}|{options.FontStyle.Width}|{options.FontStyle.Slant}";
		if (!TypefaceCache.TryGetValue(cacheKey, out var typeface))
		{
			typeface = SKTypeface.FromFamilyName(options.FontFamily, options.FontStyle) ?? SKTypeface.Default;
			TypefaceCache[cacheKey] = typeface;
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
			ScaleX = options.Stretch / 100f,
			SkewX = -MathF.Tan(options.Skew * MathF.PI / 180f),
		};
	}

	/// <summary>Lays the text out: one positioned glyph run per line, with the transform and
	/// letter spacing applied. Measure and Draw share it, so what is measured is what is drawn.</summary>
	private static TextLayout BuildLayout(TextOverlayOptions options)
	{
		using var font = CreateFont(options);
		var metrics = font.Metrics;
		var lineHeight = (-metrics.Ascent + metrics.Descent) * options.LineHeight;

		//The editor commits Windows line breaks; a stray '\r' would render as a junk glyph.
		var lines = options.Text.Replace("\r\n", "\n").Split('\n');
		var blobs = new SKTextBlob?[lines.Length];
		var lineWidths = new float[lines.Length];
		var width = 0f;
		for (var i = 0; i < lines.Length; i++)
		{
			//The baselines are baked into the runs, so every pass draws the same layout.
			blobs[i] = CreateLine(TransformText(lines[i], options.Transform), font, i * lineHeight - metrics.Ascent, options.LetterSpacing, out lineWidths[i]);
			width = Math.Max(width, lineWidths[i]);
		}

		//The alignment places each line within the block's width; the offsets stay outside
		//the glyph runs, so the same runs serve every alignment without rebuilding.
		var offsets = new float[lines.Length];
		for (var i = 0; i < lines.Length; i++)
		{
			offsets[i] = options.Alignment switch
			{
				TextAlignment.Center => (width - lineWidths[i]) / 2,
				TextAlignment.Right => width - lineWidths[i],
				_ => 0,
			};
		}

		return new TextLayout(blobs, offsets, width, lines.Length * lineHeight);
	}

	private static SKTextBlob? CreateLine(string line, SKFont font, float baseline, float spacing, out float width)
	{
		width = 0;
		var advances = font.GetGlyphWidths(line, null);
		if (advances.Length == 0)
			return null;

		var positions = new float[advances.Length];
		var x = 0f;
		for (var i = 0; i < advances.Length; i++)
		{
			positions[i] = x;
			x += advances[i] + spacing;
		}

		//The letter spacing trails the last glyph too, so the block width subtracts it once.
		width = x - spacing;
		return SKTextBlob.CreateHorizontal(line, font, positions, baseline);
	}

	private static string TransformText(string text, TextTransform transform) => transform switch
	{
		TextTransform.Upper => text.ToUpperInvariant(),
		TextTransform.Lower => text.ToLowerInvariant(),
		_ => text,
	};

	private static void DrawGlow(SKCanvas canvas, TextOverlayOptions options, TextLayout layout)
	{
		if (options.GlowColor.Alpha == 0 || options.GlowBlur <= 0)
			return;

		using var paint = new SKPaint
		{
			Color = options.GlowColor,
			IsAntialias = true,
			ImageFilter = SKImageFilter.CreateBlur(options.GlowBlur, options.GlowBlur),
		};
		DrawLines(canvas, layout, options.Position, paint);
	}

	private static void DrawShadow(SKCanvas canvas, TextOverlayOptions options, TextLayout layout)
	{
		if (options.ShadowColor.Alpha == 0)
			return;

		using var paint = new SKPaint
		{
			Color = options.ShadowColor,
			IsAntialias = true,
			BlendMode = options.ShadowBlendMode,
			ImageFilter = SKImageFilter.CreateBlur(options.ShadowBlur, options.ShadowBlur),
		};
		DrawLines(canvas, layout, options.Position + options.ShadowOffset, paint);
	}

	private static void DrawOutline(SKCanvas canvas, TextOverlayOptions options, TextLayout layout, bool second)
	{
		var color = second ? options.SecondOutlineColor : options.OutlineColor;
		if (color.Alpha == 0 || options.OutlineWidth <= 0)
			return;

		//The second outline is a wider stroke drawn under the main one, so exactly its
		//width remains visible as a ring around the main outline. "Outside" positions
		//double the strokes, letting the fill cover their inner halves.
		var width = options.OutlineWidth * (options.OutlinePosition == OutlinePosition.Outside ? 2 : 1);
		if (second)
		{
			if (options.SecondOutlineWidth <= 0)
				return;

			width += 2 * options.SecondOutlineWidth;
		}

		using var paint = new SKPaint
		{
			Color = color,
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = width,
			StrokeJoin = SKStrokeJoin.Round,
			StrokeCap = SKStrokeCap.Round,
		};
		DrawLines(canvas, layout, options.Position, paint);
	}

	private static void DrawFill(SKCanvas canvas, TextOverlayOptions options, TextLayout layout)
	{
		using var paint = new SKPaint
		{
			Color = options.TextColor,
			IsAntialias = true,
		};

		if (options.GradientFill)
		{
			var center = options.Position + new SKPoint(layout.Width / 2, layout.Height / 2);
			var radians = options.GradientAngle * MathF.PI / 180f;
			var vector = new SKPoint(MathF.Cos(radians) * layout.Width / 2, MathF.Sin(radians) * layout.Height / 2);
			using var shader = SKShader.CreateLinearGradient(
				center - vector,
				center + vector,
				new[] { options.TextColor, options.GradientColor },
				null,
				SKShaderTileMode.Clamp);
			paint.Shader = shader;
		}

		DrawLines(canvas, layout, options.Position, paint);
	}

	private static void DrawLines(SKCanvas canvas, TextLayout layout, SKPoint position, SKPaint paint)
	{
		for (var i = 0; i < layout.Lines.Length; i++)
		{
			if (layout.Lines[i] is { } line)
				canvas.DrawText(line, position.X + layout.Offsets[i], position.Y, paint);
		}
	}

	/// <summary>The positioned glyph runs of a text block plus its measured size; the shared layout of Measure and Draw.</summary>
	private sealed class TextLayout(SKTextBlob?[] lines, float[] offsets, float width, float height) : IDisposable
	{
		public SKTextBlob?[] Lines { get; } = lines;

		/// <summary>Where each line sits within the block's width, per its alignment.</summary>
		public float[] Offsets { get; } = offsets;

		public float Width { get; } = width;

		public float Height { get; } = height;

		public void Dispose()
		{
			foreach (var line in Lines)
				line?.Dispose();
		}
	}
}
