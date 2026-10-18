using Avalonia;
using Avalonia.Media;
using System.Globalization;

namespace WhatSchedule.Controls;

/// <summary>Draws the horizontal and vertical ruler strips for the preview canvas.</summary>
internal sealed class RulerRenderer
{
	public enum RulerUnit
	{
		Pixels,
		Percent,
	}


	private readonly FormattedTextCache labelCache = new();

	public RulerUnit Unit { get; set; } = RulerUnit.Pixels;

	public static void DrawBorders(DrawingContext context, Rect bounds)
	{
		context.DrawLine(ThemeDefinition.RulerBorderPen, new Point(-1, ThemeDefinition.RulerThickness - 0.5), new Point(bounds.Width + 1, ThemeDefinition.RulerThickness - 0.5));
		context.DrawLine(ThemeDefinition.RulerBorderPen, new Point(ThemeDefinition.RulerThickness - 0.5, -1), new Point(ThemeDefinition.RulerThickness - 0.5, bounds.Height + 1));
	}

	public void Draw(DrawingContext context, Rect bounds, Rect imageRect, Size imageSize, double scale)
	{
		var horizontalStrip = new Rect(ThemeDefinition.RulerThickness, -1, bounds.Width + 1 - ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness);
		var verticalStrip = new Rect(-1, ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness, bounds.Height + 1 - ThemeDefinition.RulerThickness);

		var corner = new Rect(-1, -1, ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness);
		context.FillRectangle(ThemeDefinition.RulerBackground, corner);

		context.FillRectangle(ThemeDefinition.RulerBackground, horizontalStrip);
		context.FillRectangle(ThemeDefinition.RulerBackground, verticalStrip);

		DrawTicks(context, horizontalStrip, imageRect.X, imageSize.Width, Unit, scale, horizontal: true);
		DrawTicks(context, verticalStrip, imageRect.Y, imageSize.Height, Unit, scale, horizontal: false);
	}

	private void DrawTicks(DrawingContext context, Rect strip, double imageStart, double imageSize, RulerUnit unit, double scale, bool horizontal)
	{
		double unitsPerPixel = unit == RulerUnit.Percent ? 100 / imageSize : 1;
		double step = ChooseStep(ThemeDefinition.MinMajorTickSpacing * unitsPerPixel / scale);
		if (unit == RulerUnit.Percent && step > 100)
			step = 100;

		double minorPixels = step / unitsPerPixel / 5;
		int firstTick = (int)Math.Ceiling(Math.Max(0, ((horizontal ? strip.X : strip.Y) - imageStart) / scale) / minorPixels);

		using var clipState = context.PushClip(strip);
		for (var tick = firstTick; ; tick++)
		{
			var pixel = tick * minorPixels;
			if (pixel > imageSize)
				break;

			var position = imageStart + pixel * scale;
			var stripEnd = horizontal ? strip.Right : strip.Bottom;
			if (position > stripEnd)
				break;

			var isMajor = tick % 5 == 0;
			var label = FormatTickLabel(pixel * unitsPerPixel, unit);

			if (horizontal)
			{
				var majorTop = ThemeDefinition.RulerThickness - 9;
				var minorTop = ThemeDefinition.RulerThickness - 5;
				context.DrawLine(ThemeDefinition.TickPen, new Point(position, isMajor ? majorTop : minorTop), new Point(position, ThemeDefinition.RulerThickness - 1));
				if (isMajor)
				{
					context.DrawText(labelCache.Get(label), new Point(position + 3, 2));
				}
			}
			else
			{
				var majorStart = ThemeDefinition.RulerThickness - 9;
				var minorStart = ThemeDefinition.RulerThickness - 5;
				context.DrawLine(ThemeDefinition.TickPen, new Point(isMajor ? majorStart : minorStart, position), new Point(ThemeDefinition.RulerThickness - 1, position));
				if (isMajor)
				{
					var formatted = labelCache.Get(label);
					using var transformState = context.PushTransform(
						Matrix.CreateRotation(-Math.PI / 2) *
						Matrix.CreateTranslation(ThemeDefinition.RulerThickness - 3 - formatted.Height, position + 2 + formatted.Width));
					context.DrawText(formatted, new Point());
				}
			}
		}
	}

	private static string FormatTickLabel(double value, RulerUnit unit)
	{
		return unit switch
		{
			RulerUnit.Pixels => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
			RulerUnit.Percent => value.ToString("0.##", CultureInfo.InvariantCulture) + "%",
			_ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
		};
	}

	private static double ChooseStep(double minUnits)
	{
		var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(minUnits, 0.000001))));
		var fraction = minUnits / magnitude;
		var coefficient = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
		return coefficient * magnitude;
	}

	private sealed class FormattedTextCache
	{
		private readonly Dictionary<string, FormattedText> cache = new();

		public FormattedText Get(string text)
		{
			if (!cache.TryGetValue(text, out var formatted))
			{
				formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeDefinition.LabelTypeface, ThemeDefinition.LabelFontSize, ThemeDefinition.RulerLabel);
				cache[text] = formatted;
			}

			return formatted;
		}
	}
}
