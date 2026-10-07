using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;

namespace WhatSchedule.Controls;

public class PreviewCanvas : Control
{
	private readonly FormattedTextCache labelCache = new();

	private double scale = 1;
	private double offsetX;
	private double offsetY;
	private bool hasInitialFit;
	private bool isDragging;
	private Point dragStartPosition;
	private double dragStartOffsetX;
	private double dragStartOffsetY;

	public static readonly StyledProperty<IImage?> ImageProperty = AvaloniaProperty.Register<PreviewCanvas, IImage?>(nameof(Image));

	public IImage? Image
	{
		get => GetValue(ImageProperty);
		set => SetValue(ImageProperty, value);
	}

	static PreviewCanvas()
	{
		AffectsRender<PreviewCanvas>(ImageProperty);
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == ImageProperty)
		{
			hasInitialFit = false;
			InvalidateVisual();
		}
	}

	public PreviewCanvas()
	{
		PointerCaptureLost += (_, _) =>
		{
			isDragging = false;
			Cursor = Cursor.Default;
		};
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var image = Image;
		if (image is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		isDragging = true;
		dragStartPosition = e.GetPosition(this);
		dragStartOffsetX = offsetX;
		dragStartOffsetY = offsetY;
		Cursor = new Cursor(StandardCursorType.SizeAll);
		e.Pointer.Capture(this);
		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		var image = Image;
		if (!isDragging || image is null)
			return;

		var position = e.GetPosition(this);
		offsetX = dragStartOffsetX + position.X - dragStartPosition.X;
		offsetY = dragStartOffsetY + position.Y - dragStartPosition.Y;
		ClampOffsets(image);
		InvalidateVisual();
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		EndDrag(e.Pointer);
		e.Handled = true;
	}

	private void EndDrag(IPointer pointer)
	{
		if (!isDragging)
			return;

		isDragging = false;
		if (pointer.Captured == this)
		{
			pointer.Capture(null);
		}

		Cursor = Cursor.Default;
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		e.Handled = true;

		var image = Image;
		if (image is null || image.Size.Width <= 0 || image.Size.Height <= 0)
			return;

		var factor = Math.Clamp(Math.Pow(ThemeDefinition.WheelFactor, e.Delta.Y), 1 / 1.5, 1.5);
		var newScale = Math.Clamp(scale * factor, ThemeDefinition.MinZoom, ThemeDefinition.MaxZoom);
		if (newScale == scale)
			return;

		// Keep the image point under the cursor fixed while zooming.
		var cursor = e.GetPosition(this);
		var rect = GetImageRect(image);
		var pixelX = (cursor.X - rect.X) / scale;
		var pixelY = (cursor.Y - rect.Y) / scale;

		scale = newScale;
		SetImagePosition(new Rect(
			cursor.X - pixelX * scale,
			cursor.Y - pixelY * scale,
			image.Size.Width * scale,
			image.Size.Height * scale), image);
		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		var bounds = new Rect(Bounds.Size);
		context.FillRectangle(ThemeDefinition.Background, bounds);
		DrawRulerBorders(context, bounds);

		var image = Image;
		if (image is null || image.Size.Width <= 0 || image.Size.Height <= 0)
		{
			return;
		}

		if (!hasInitialFit && bounds.Width > ThemeDefinition.RulerThickness && bounds.Height > ThemeDefinition.RulerThickness)
		{
			var content = GetContentRect(bounds);
			scale = Math.Min(Math.Min(content.Width / image.Size.Width, content.Height / image.Size.Height), 1);
			offsetX = 0;
			offsetY = 0;
			hasInitialFit = true;
		}

		var imageRect = GetImageRect(image);
		context.DrawImage(image, imageRect);

		DrawRulers(context, bounds, imageRect, image.Size);
	}

	private static Rect GetContentRect(Rect bounds)
	{
		return new Rect(
			ThemeDefinition.RulerThickness,
			ThemeDefinition.RulerThickness,
			Math.Max(0, bounds.Width - ThemeDefinition.RulerThickness),
			Math.Max(0, bounds.Height - ThemeDefinition.RulerThickness));
	}

	private Rect GetImageRect(IImage image)
	{
		var content = GetContentRect(new Rect(Bounds.Size));
		var width = image.Size.Width * scale;
		var height = image.Size.Height * scale;
		return new Rect(
			content.X + content.Width / 2 + offsetX - width / 2,
			content.Y + content.Height / 2 + offsetY - height / 2,
			width,
			height);
	}

	private void SetImagePosition(Rect rect, IImage image)
	{
		var content = GetContentRect(new Rect(Bounds.Size));

		offsetX = rect.X - content.X - content.Width / 2 + image.Size.Width * scale / 2;
		offsetY = rect.Y - content.Y - content.Height / 2 + image.Size.Height * scale / 2;
		ClampOffsets(image);
	}

	private void ClampOffsets(IImage image)
	{
		var content = GetContentRect(new Rect(Bounds.Size));
		var width = image.Size.Width * scale;
		var height = image.Size.Height * scale;

		// The image must keep at least MinVisibleEdge of overlap with the content rect
		// on each axis, but its edges are allowed to hang off-screen.
		if (content.Width > 2 * ThemeDefinition.MinVisibleEdge)
		{
			offsetX = Math.Clamp(offsetX, ThemeDefinition.MinVisibleEdge - content.Width / 2 - width / 2, content.Width / 2 - ThemeDefinition.MinVisibleEdge + width / 2);
		}

		if (content.Height > 2 * ThemeDefinition.MinVisibleEdge)
		{
			offsetY = Math.Clamp(offsetY, ThemeDefinition.MinVisibleEdge - content.Height / 2 - height / 2, content.Height / 2 - ThemeDefinition.MinVisibleEdge + height / 2);
		}
	}

	private static void DrawRulerBorders(DrawingContext context, Rect bounds)
	{
		context.DrawLine(ThemeDefinition.RulerBorderPen, new Point(0, ThemeDefinition.RulerThickness), new Point(bounds.Width, ThemeDefinition.RulerThickness));
		context.DrawLine(ThemeDefinition.RulerBorderPen, new Point(ThemeDefinition.RulerThickness, 0), new Point(ThemeDefinition.RulerThickness, bounds.Height));
	}

	private void DrawRulers(DrawingContext context, Rect bounds, Rect imageRect, Size imageSize)
	{
		var horizontalStrip = new Rect(ThemeDefinition.RulerThickness, 0, bounds.Width - ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness);
		var verticalStrip = new Rect(0, ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness, bounds.Height - ThemeDefinition.RulerThickness);

		context.FillRectangle(ThemeDefinition.RulerBackground, horizontalStrip);
		context.FillRectangle(ThemeDefinition.RulerBackground, verticalStrip);

		DrawTicks(context, horizontalStrip, imageRect.X, imageSize.Width, horizontal: true);
		DrawTicks(context, verticalStrip, imageRect.Y, imageSize.Height, horizontal: false);
	}

	private void DrawTicks(DrawingContext context, Rect strip, double imageStart, double imageSize, bool horizontal)
	{
		var step = ChooseStep();
		var minor = step / 5;
		var firstTick = (int)Math.Ceiling(Math.Max(0, ((horizontal ? strip.X : strip.Y) - imageStart) / scale) / minor);

		using var clipState = context.PushClip(strip);
		for (var tick = firstTick; ; tick++)
		{
			var pixel = tick * minor;
			if (pixel > imageSize)
				break;

			var position = imageStart + pixel * scale;
			var stripEnd = horizontal ? strip.Right : strip.Bottom;
			if (position > stripEnd)
				break;

			var isMajor = tick % 5 == 0;
			var label = ((int)Math.Round(pixel)).ToString(CultureInfo.InvariantCulture);

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

	private double ChooseStep()
	{
		var unit = ThemeDefinition.MinMajorTickSpacing / scale;
		var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(unit, 0.000001))));
		var fraction = unit / magnitude;
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
