using Avalonia.Input;
using SkiaSharp;
using WhatSchedule.Rendering.Definitions;

namespace WhatSchedule.Elements;

/// <summary>
/// A text element rendered with fill, outline, drop shadow and glow.
/// </summary>
public sealed class LabelElement : CanvasElement
{
	/// <summary>The named cuts of a font family the style row offers.</summary>
	private static readonly (string Name, SKFontStyle Style)[] FontStyles =
	{
		("Regular", SKFontStyle.Normal),
		("Bold", SKFontStyle.Bold),
		("Italic", SKFontStyle.Italic),
		("Bold Italic", SKFontStyle.BoldItalic),
	};

	/// <summary>The blend modes offered for the label and for its shadow.</summary>
	private static readonly string[] BlendModes =
	{
		SKBlendMode.SrcOver.ToString(), SKBlendMode.Multiply.ToString(), SKBlendMode.Screen.ToString(), SKBlendMode.Overlay.ToString(),
		SKBlendMode.Darken.ToString(), SKBlendMode.Lighten.ToString(), SKBlendMode.HardLight.ToString(), SKBlendMode.SoftLight.ToString(),
		SKBlendMode.Difference.ToString(), SKBlendMode.Exclusion.ToString(), SKBlendMode.Hue.ToString(), SKBlendMode.Saturation.ToString(),
		SKBlendMode.Color.ToString(), SKBlendMode.Luminosity.ToString(), SKBlendMode.Plus.ToString(),
	};

	public string Text { get; set; } = "Label";

	/// <summary>The font family the text is rendered with; falls back to the default font when unavailable.</summary>
	public string FontFamily { get; set; } = "Arial";

	/// <summary>The style (weight and slant) requested from the font family.</summary>
	public SKFontStyle FontStyle { get; set; } = SKFontStyle.Normal;

	public float FontSize { get; set; } = 48f;

	/// <summary>Extra space inserted between glyphs, in pixels.</summary>
	public float LetterSpacing { get; set; }

	/// <summary>Horizontal stretch in percent; 100 is the typeface's natural width.</summary>
	public float Stretch { get; set; } = 100f;

	/// <summary>Italic slant in degrees; 0 is upright.</summary>
	public float Skew { get; set; }

	/// <summary>Line height as a multiple of the font's height; 1 packs the lines tightly.</summary>
	public float LineHeight { get; set; } = 1f;

	/// <summary>How the lines are placed within the text block's width.</summary>
	public TextAlignment Alignment { get; set; } = TextAlignment.Left;

	/// <summary>Casing applied to the text before rendering; the stored text is unchanged.</summary>
	public TextTransform Transform { get; set; } = TextTransform.None;

	public SKColor TextColor { get; set; } = SKColors.White;

	/// <summary>Whole-label opacity from 0 (transparent) to 1 (opaque).</summary>
	public float Opacity { get; set; } = 1f;

	/// <summary>Whether the fill is a gradient from <see cref="TextColor"/> to <see cref="GradientColor"/>.</summary>
	public bool GradientFill { get; set; }

	public SKColor GradientColor { get; set; } = SKColors.White;

	/// <summary>The gradient's direction in degrees, 0 left to right, 90 top to bottom.</summary>
	public float GradientAngle { get; set; }

	public SKColor OutlineColor { get; set; } = SKColors.Black;

	public float OutlineWidth { get; set; } = 2f;

	/// <summary>Where the outline stroke sits relative to the glyph edge.</summary>
	public OutlinePosition OutlinePosition { get; set; } = OutlinePosition.Center;

	/// <summary>The color of the outline drawn outside the main outline.</summary>
	public SKColor SecondOutlineColor { get; set; } = SKColors.White;

	/// <summary>The visible thickness of the outer outline in pixels; 0 hides it.</summary>
	public float SecondOutlineWidth { get; set; }

	public SKColor ShadowColor { get; set; } = SKColors.Black.WithAlpha(140);

	public float ShadowBlur { get; set; } = 6f;

	public SKPoint ShadowOffset { get; set; } = new(3f, 3f);

	/// <summary>How the shadow composites onto the background.</summary>
	public SKBlendMode ShadowBlendMode { get; set; } = SKBlendMode.SrcOver;

	/// <summary>The color of the blurred glow drawn behind the label; transparent alpha hides it.</summary>
	public SKColor GlowColor { get; set; } = SKColors.Transparent;

	/// <summary>The glow's blur radius in pixels.</summary>
	public float GlowBlur { get; set; }

	/// <summary>Gaussian blur applied to the whole label; 0 is sharp.</summary>
	public float Blur { get; set; }

	/// <summary>How the whole label composites onto the background.</summary>
	public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;

	public bool FlipX { get; set; }

	public bool FlipY { get; set; }

	public override SKSize Size => ImageTextRenderer.Measure(BuildOptions(SKPoint.Empty));

	public override void Draw(SKCanvas canvas)
	{
		var bounds = Bounds;
		var count = canvas.Save();
		canvas.RotateDegrees(Rotation, bounds.MidX, bounds.MidY);
		if (FlipX || FlipY)
			canvas.Scale(FlipX ? -1 : 1, FlipY ? -1 : 1, bounds.MidX, bounds.MidY);
		ImageTextRenderer.Draw(canvas, BuildOptions(Position));
		canvas.RestoreToCount(count);
	}

	public override ScaleState BeginScale() => new LabelScaleState(Size.Width, FontSize, OutlineWidth, SecondOutlineWidth, LetterSpacing, ShadowBlur, GlowBlur, Blur, ShadowOffset);

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
		SecondOutlineWidth = start.SecondOutlineWidth * factor;
		LetterSpacing = start.LetterSpacing * factor;
		ShadowBlur = start.ShadowBlur * factor;
		GlowBlur = start.GlowBlur * factor;
		Blur = start.Blur * factor;
		ShadowOffset = new SKPoint(start.ShadowOffset.X * factor, start.ShadowOffset.Y * factor);

		var size = Size;
		Position = new SKPoint(
			anchor.X <= cursor.X ? anchor.X : anchor.X - size.Width,
			anchor.Y <= cursor.Y ? anchor.Y : anchor.Y - size.Height);
	}

	public override ElementState CaptureState() => new LabelElementState(Position, Rotation, Text, FontFamily, FontStyle, FontSize, LetterSpacing, Stretch, Skew, LineHeight, Alignment, Transform, TextColor, Opacity, GradientFill, GradientColor, GradientAngle, OutlineColor, OutlineWidth, OutlinePosition, SecondOutlineColor, SecondOutlineWidth, ShadowColor, ShadowBlur, ShadowOffset, ShadowBlendMode, GlowColor, GlowBlur, Blur, BlendMode, FlipX, FlipY);

	public override void BuildInspectorRows(IInspectorBuilder builder)
	{
		builder.AddSection("Text");
		builder.AddTextArea("Content", () => Text, value => Text = value);
		builder.AddFontBox("Font", () => FontFamily, value => FontFamily = value);
		builder.AddComboBox("Style", FontStyles.Select(style => style.Name).ToArray(), GetStyleName, SetStyle);
		builder.AddNumber("Font Size", () => FontSize,
			value => FontSize = Math.Max(ThemeDefinition.MinFontSize, value));
		builder.AddComboBox("Alignment", Enum.GetNames<TextAlignment>(), () => Alignment.ToString(), value => Alignment = Enum.Parse<TextAlignment>(value));
		builder.AddSlider("Letter Spacing", 0, 40, () => LetterSpacing, value => LetterSpacing = Math.Max(0, value));
		builder.AddSlider("Stretch %", 50, 200, () => Stretch, value => Stretch = Math.Clamp(value, 50, 200));
		builder.AddSlider("Skew", -30, 30, () => Skew, value => Skew = Math.Clamp(value, -30, 30));
		builder.AddSlider("Line Height %", 50, 300, () => LineHeight * 100f, value => LineHeight = Math.Clamp(value, 50, 300) / 100f);
		builder.AddComboBox("Transform", Enum.GetNames<TextTransform>(), () => Transform.ToString(), value => Transform = Enum.Parse<TextTransform>(value));

		builder.AddSection("Fill");
		builder.AddColor("Color", () => TextColor, value => TextColor = value);
		builder.AddToggle("Gradient", () => GradientFill, value => GradientFill = value);
		builder.AddColor("Gradient To", () => GradientColor, value => GradientColor = value);
		builder.AddSlider("Gradient Angle", 0, 360, () => GradientAngle, value => GradientAngle = Math.Clamp(value, 0, 360));

		builder.AddSection("Outline");
		builder.AddColor("Color", () => OutlineColor, value => OutlineColor = value);
		builder.AddNumber("Width", () => OutlineWidth, value => OutlineWidth = Math.Max(0, value));
		builder.AddComboBox("Position", Enum.GetNames<OutlinePosition>(), () => OutlinePosition.ToString(), value => OutlinePosition = Enum.Parse<OutlinePosition>(value));
		builder.AddColor("Second Color", () => SecondOutlineColor, value => SecondOutlineColor = value);
		builder.AddNumber("Second Width", () => SecondOutlineWidth, value => SecondOutlineWidth = Math.Max(0, value));

		builder.AddSection("Shadow");
		builder.AddComboBox("Blend Mode", BlendModes, () => ShadowBlendMode.ToString(), value => ShadowBlendMode = Enum.Parse<SKBlendMode>(value));
		builder.AddColor("Color", () => ShadowColor, value => ShadowColor = value);
		builder.AddNumber("Blur", () => ShadowBlur, value => ShadowBlur = Math.Max(0, value));
		builder.AddNumber("Offset X", () => ShadowOffset.X, value => ShadowOffset = new SKPoint(value, ShadowOffset.Y));
		builder.AddNumber("Offset Y", () => ShadowOffset.Y, value => ShadowOffset = new SKPoint(ShadowOffset.X, value));

		builder.AddSection("Glow");
		builder.AddColor("Color", () => GlowColor, value => GlowColor = value);
		builder.AddNumber("Blur", () => GlowBlur, value => GlowBlur = Math.Max(0, value));

		builder.AddSection("Appearance");
		builder.AddComboBox("Blend Mode", BlendModes, () => BlendMode.ToString(), value => BlendMode = Enum.Parse<SKBlendMode>(value));
		builder.AddSlider("Opacity %", 0, 100, () => Opacity * 100f, value => Opacity = Math.Clamp(value, 0, 100) / 100f);
		builder.AddSlider("Blur", 0, 50, () => Blur, value => Blur = Math.Max(0, value));
		builder.AddToggle("Flip Horizontal", () => FlipX, value => FlipX = value);
		builder.AddToggle("Flip Vertical", () => FlipY, value => FlipY = value);
	}

	public override void RestoreState(ElementState state)
	{
		if (state is not LabelElementState snapshot)
			return;

		Position = snapshot.Position;
		Rotation = snapshot.Rotation;
		Text = snapshot.Text;
		FontFamily = snapshot.FontFamily;
		FontStyle = snapshot.FontStyle;
		FontSize = snapshot.FontSize;
		LetterSpacing = snapshot.LetterSpacing;
		Stretch = snapshot.Stretch;
		Skew = snapshot.Skew;
		LineHeight = snapshot.LineHeight;
		Alignment = snapshot.Alignment;
		Transform = snapshot.Transform;
		TextColor = snapshot.TextColor;
		Opacity = snapshot.Opacity;
		GradientFill = snapshot.GradientFill;
		GradientColor = snapshot.GradientColor;
		GradientAngle = snapshot.GradientAngle;
		OutlineColor = snapshot.OutlineColor;
		OutlineWidth = snapshot.OutlineWidth;
		OutlinePosition = snapshot.OutlinePosition;
		SecondOutlineColor = snapshot.SecondOutlineColor;
		SecondOutlineWidth = snapshot.SecondOutlineWidth;
		ShadowColor = snapshot.ShadowColor;
		ShadowBlur = snapshot.ShadowBlur;
		ShadowOffset = snapshot.ShadowOffset;
		ShadowBlendMode = snapshot.ShadowBlendMode;
		GlowColor = snapshot.GlowColor;
		GlowBlur = snapshot.GlowBlur;
		Blur = snapshot.Blur;
		BlendMode = snapshot.BlendMode;
		FlipX = snapshot.FlipX;
		FlipY = snapshot.FlipY;
	}

	/// <summary>The name of the offered font style matching the element's weight and slant,
	/// or the first style when the element carries a cut the row does not offer.</summary>
	private string GetStyleName() => (FontStyles.FirstOrDefault(style => style.Style.Weight == FontStyle.Weight && style.Style.Slant == FontStyle.Slant).Name ?? FontStyles[0].Name)!;

	/// <summary>Sets the element's style from the name of one of the offered font styles.</summary>
	private void SetStyle(string name) => FontStyle = FontStyles.First(style => style.Name == name).Style;

	private sealed record LabelScaleState(float Width, float FontSize, float OutlineWidth, float SecondOutlineWidth, float LetterSpacing, float ShadowBlur, float GlowBlur, float Blur, SKPoint ShadowOffset) : ScaleState;

	public sealed record LabelElementState(SKPoint Position, float Rotation, string Text, string FontFamily, SKFontStyle FontStyle, float FontSize, float LetterSpacing, float Stretch, float Skew, float LineHeight, TextAlignment Alignment, TextTransform Transform, SKColor TextColor, float Opacity, bool GradientFill, SKColor GradientColor, float GradientAngle, SKColor OutlineColor, float OutlineWidth, OutlinePosition OutlinePosition, SKColor SecondOutlineColor, float SecondOutlineWidth, SKColor ShadowColor, float ShadowBlur, SKPoint ShadowOffset, SKBlendMode ShadowBlendMode, SKColor GlowColor, float GlowBlur, float Blur, SKBlendMode BlendMode, bool FlipX, bool FlipY) : ElementState;

	private TextOverlayOptions BuildOptions(SKPoint position) => new()
	{
		Text = Text,
		FontFamily = FontFamily,
		FontStyle = FontStyle,
		FontSize = FontSize,
		LetterSpacing = LetterSpacing,
		Stretch = Stretch,
		Skew = Skew,
		LineHeight = LineHeight,
		Alignment = Alignment,
		Transform = Transform,
		TextColor = TextColor,
		Opacity = Opacity,
		GradientFill = GradientFill,
		GradientColor = GradientColor,
		GradientAngle = GradientAngle,
		OutlineColor = OutlineColor,
		OutlineWidth = OutlineWidth,
		OutlinePosition = OutlinePosition,
		SecondOutlineColor = SecondOutlineColor,
		SecondOutlineWidth = SecondOutlineWidth,
		ShadowColor = ShadowColor,
		ShadowBlur = ShadowBlur,
		ShadowOffset = ShadowOffset,
		ShadowBlendMode = ShadowBlendMode,
		GlowColor = GlowColor,
		GlowBlur = GlowBlur,
		Blur = Blur,
		BlendMode = BlendMode,
		Position = position,
	};

	public sealed class LabelElementData : ElementData
	{
		public const string KindName = "Label";

		public override string Kind => KindName;

		public string Text { get; set; } = "Label";

		public string FontFamily { get; set; } = "Arial";

		/// <summary>The requested font weight as a Skia weight value (400 normal, 700 bold); null in documents saved before font styles existed.</summary>
		public int? FontWeight { get; set; }

		/// <summary>The requested font slant as a Skia slant value (0 upright, 1 italic); null in documents saved before font styles existed.</summary>
		public int? FontSlant { get; set; }

		public float FontSize { get; set; } = 48f;

		/// <summary>Null in documents saved before letter spacing existed.</summary>
		public float? LetterSpacing { get; set; }

		/// <summary>Null in documents saved before stretching existed.</summary>
		public float? Stretch { get; set; }

		/// <summary>Null in documents saved before skewing existed.</summary>
		public float? Skew { get; set; }

		/// <summary>Null in documents saved before line height existed.</summary>
		public float? LineHeight { get; set; }

		/// <summary>The text alignment as its name; null in documents saved before alignment existed.</summary>
		public string? Alignment { get; set; }

		/// <summary>The text transform as its name; null in documents saved before transforms existed.</summary>
		public string? Transform { get; set; }

		public string? TextColor { get; set; }

		/// <summary>Null in documents saved before whole-label opacity existed.</summary>
		public float? Opacity { get; set; }

		/// <summary>Null in documents saved before gradient fill existed.</summary>
		public bool? GradientFill { get; set; }

		public string? GradientColor { get; set; }

		/// <summary>Null in documents saved before gradient fill existed.</summary>
		public float? GradientAngle { get; set; }

		public string? OutlineColor { get; set; }

		public float OutlineWidth { get; set; } = 2f;

		/// <summary>The outline position as its name; null in documents saved before outline positions existed.</summary>
		public string? OutlinePosition { get; set; }

		/// <summary>Null in documents saved before the second outline existed.</summary>
		public string? SecondOutlineColor { get; set; }

		/// <summary>Null in documents saved before the second outline existed.</summary>
		public float? SecondOutlineWidth { get; set; }

		public string? ShadowColor { get; set; }

		public float ShadowBlur { get; set; } = 6f;

		public PointData? ShadowOffset { get; set; }

		/// <summary>The shadow's blend mode as its name; null in documents saved before shadow blend modes existed.</summary>
		public string? ShadowBlendMode { get; set; }

		/// <summary>Null in documents saved before glow existed.</summary>
		public string? GlowColor { get; set; }

		/// <summary>Null in documents saved before glow existed.</summary>
		public float? GlowBlur { get; set; }

		/// <summary>Null in documents saved before whole-label blur existed.</summary>
		public float? Blur { get; set; }

		/// <summary>The label's blend mode as its name; null in documents saved before blend modes existed.</summary>
		public string? BlendMode { get; set; }

		/// <summary>Null in documents saved before flipping existed.</summary>
		public bool? FlipX { get; set; }

		/// <summary>Null in documents saved before flipping existed.</summary>
		public bool? FlipY { get; set; }
	}
}
