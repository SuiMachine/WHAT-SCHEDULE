using SkiaSharp;

namespace WhatSchedule.Rendering.Definitions;

/// <summary>
/// Renders styled text overlays (fill, outline, shadow) onto images using SkiaSharp.
/// </summary>
public static class ImageTextRenderer
{
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

			using var typeface = SKTypeface.FromFamilyName(options.FontFamily) is { } queried
				? queried
				: null;
			using var font = new SKFont(typeface ?? SKTypeface.Default, options.FontSize)
			{
				Edging = SKFontEdging.Antialias,
			};

			DrawShadow(canvas, options, font);
			DrawOutline(canvas, options, font);
			DrawFill(canvas, options, font);
		}

		return result;
	}

	private static void DrawShadow(SKCanvas canvas, TextOverlayOptions options, SKFont font)
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
			options.Position.X + options.ShadowOffset.X,
			options.Position.Y + options.ShadowOffset.Y,
			font,
			paint);
	}

	private static void DrawOutline(SKCanvas canvas, TextOverlayOptions options, SKFont font)
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
		canvas.DrawText(options.Text, options.Position.X, options.Position.Y, font, paint);
	}

	private static void DrawFill(SKCanvas canvas, TextOverlayOptions options, SKFont font)
	{
		using var paint = new SKPaint
		{
			Color = options.TextColor,
			IsAntialias = true,
		};
		canvas.DrawText(options.Text, options.Position.X, options.Position.Y, font, paint);
	}
}
