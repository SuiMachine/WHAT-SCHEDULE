using Avalonia.Input;
using SkiaSharp;
using WhatSchedule.Rendering.Definitions;

namespace WhatSchedule.Elements;

/// <summary>
/// A text element rendered with fill, outline and drop shadow.
/// </summary>
public sealed class LabelElement : CanvasElement
{
	public string Text { get; set; } = "Label";

	public float FontSize { get; set; } = 48f;

	public SKColor TextColor { get; set; } = SKColors.White;

	public SKColor OutlineColor { get; set; } = SKColors.Black;

	public float OutlineWidth { get; set; } = 2f;

	public SKColor ShadowColor { get; set; } = SKColors.Black.WithAlpha(140);

	public float ShadowBlur { get; set; } = 6f;

	public SKPoint ShadowOffset { get; set; } = new(3f, 3f);

	public override SKSize Size => ImageTextRenderer.Measure(BuildOptions(SKPoint.Empty));

	public override void Draw(SKCanvas canvas) => ImageTextRenderer.Draw(canvas, BuildOptions(Position));

	public override ScaleState BeginScale() => new LabelScaleState(Size.Width, FontSize, OutlineWidth, ShadowBlur, ShadowOffset);

	public override void ScaleTo(SKPoint cursor, SKPoint anchor, ScaleState state, PointerEventArgs pointerEvent)
	{
		if (state is not LabelScaleState start)
			return;

		// Labels keep their text proportions: the horizontal drag scales the font
		// size and the height follows from the measured text. Sizes are applied from
		// the snapshot taken when the drag started, so the scale never drifts.
		var width = Math.Max(ThemeDefinition.MinElementSize, Math.Abs(cursor.X - anchor.X));
		var factor = (float)Math.Max(width / Math.Max(start.Width, 1), ThemeDefinition.MinFontSize / start.FontSize);

		FontSize = start.FontSize * factor;
		OutlineWidth = start.OutlineWidth * factor;
		ShadowBlur = start.ShadowBlur * factor;
		ShadowOffset = new SKPoint(start.ShadowOffset.X * factor, start.ShadowOffset.Y * factor);

		var size = Size;
		Position = new SKPoint(
			anchor.X <= cursor.X ? anchor.X : anchor.X - size.Width,
			anchor.Y <= cursor.Y ? anchor.Y : anchor.Y - size.Height);
	}

	public override ElementState CaptureState() => new LabelElementState(Position, Text, FontSize, TextColor, OutlineColor, OutlineWidth, ShadowColor, ShadowBlur, ShadowOffset);

	public override void RestoreState(ElementState state)
	{
		if (state is not LabelElementState snapshot)
			return;

		Position = snapshot.Position;
		Text = snapshot.Text;
		FontSize = snapshot.FontSize;
		TextColor = snapshot.TextColor;
		OutlineColor = snapshot.OutlineColor;
		OutlineWidth = snapshot.OutlineWidth;
		ShadowColor = snapshot.ShadowColor;
		ShadowBlur = snapshot.ShadowBlur;
		ShadowOffset = snapshot.ShadowOffset;
	}

	private sealed record LabelScaleState(float Width, float FontSize, float OutlineWidth, float ShadowBlur, SKPoint ShadowOffset) : ScaleState;

	public sealed record LabelElementState(SKPoint Position, string Text, float FontSize, SKColor TextColor, SKColor OutlineColor, float OutlineWidth, SKColor ShadowColor, float ShadowBlur, SKPoint ShadowOffset) : ElementState;

	private TextOverlayOptions BuildOptions(SKPoint position) => new()
	{
		Text = Text,
		FontSize = FontSize,
		TextColor = TextColor,
		OutlineColor = OutlineColor,
		OutlineWidth = OutlineWidth,
		ShadowColor = ShadowColor,
		ShadowBlur = ShadowBlur,
		ShadowOffset = ShadowOffset,
		Position = position,
	};
}
