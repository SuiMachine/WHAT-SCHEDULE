using SkiaSharp;

namespace WhatSchedule.Rendering.Definitions;

/// <summary>Casing applied to the label text before rendering; the stored text is unchanged.</summary>
public enum TextTransform
{
	None,
	Upper,
	Lower,
}

/// <summary>Where the outline stroke sits relative to the glyph edge.</summary>
public enum OutlinePosition
{
	/// <summary>The stroke straddles the glyph edge; the fill covers its inner half.</summary>
	Center,

	/// <summary>The stroke is widened so the whole visible thickness sits outside the glyph edge.</summary>
	Outside,
}

/// <summary>How the lines of a multi-line text block are placed within its width.</summary>
public enum TextAlignment
{
	Left,
	Center,
	Right,
}

/// <summary>How the text block is placed vertically within the box it is drawn in.</summary>
public enum TextVerticalPlacement
{
	Top,
	Middle,
	Bottom,
}

public sealed record TextOverlayOptions
{
	public required string Text { get; init; }

	public string FontFamily { get; init; } = "Arial";

	/// <summary>The style (weight and slant) requested from the font family.</summary>
	public SKFontStyle FontStyle { get; init; } = SKFontStyle.Normal;

	public float FontSize { get; init; } = 48f;

	/// <summary>Extra space inserted between glyphs, in pixels.</summary>
	public float LetterSpacing { get; init; }

	/// <summary>Horizontal stretch in percent; 100 is the typeface's natural width.</summary>
	public float Stretch { get; init; } = 100f;

	/// <summary>Italic slant in degrees; positive leans right, negative leans left, 0 is upright.</summary>
	public float Skew { get; init; }

	/// <summary>Line height as a multiple of the font's height; 1 packs the lines tightly.</summary>
	public float LineHeight { get; init; } = 1f;

	/// <summary>How the lines are placed within the text block's width.</summary>
	public TextAlignment Alignment { get; init; } = TextAlignment.Left;

	/// <summary>The width the lines are aligned within, e.g. the width of the text box the block is drawn in; null aligns within the measured text width.</summary>
	public float? AlignmentWidth { get; init; }

	/// <summary>How the text block is placed vertically within the box it is drawn in.</summary>
	public TextVerticalPlacement VerticalPlacement { get; init; } = TextVerticalPlacement.Top;

	/// <summary>The height the text block is placed within per <see cref="VerticalPlacement"/>; null draws the block at <see cref="Position"/>.</summary>
	public float? BoxHeight { get; init; }

	public TextTransform Transform { get; init; } = TextTransform.None;

	public SKColor TextColor { get; init; } = SKColors.White;

	/// <summary>Whole-label opacity from 0 (transparent) to 1 (opaque).</summary>
	public float Opacity { get; init; } = 1f;

	/// <summary>Whether the fill is a gradient from <see cref="TextColor"/> to <see cref="GradientColor"/>.</summary>
	public bool GradientFill { get; init; }

	public SKColor GradientColor { get; init; } = SKColors.White;

	/// <summary>The gradient's direction in degrees, 0 left to right, 90 top to bottom.</summary>
	public float GradientAngle { get; init; }

	public SKColor OutlineColor { get; init; } = SKColors.Black;

	public float OutlineWidth { get; init; } = 2f;

	public OutlinePosition OutlinePosition { get; init; } = OutlinePosition.Center;

	/// <summary>The color of the outline drawn outside the main outline.</summary>
	public SKColor SecondOutlineColor { get; init; } = SKColors.White;

	/// <summary>The visible thickness of the outer outline in pixels; 0 hides it.</summary>
	public float SecondOutlineWidth { get; init; }

	public SKColor ShadowColor { get; init; } = SKColors.Black.WithAlpha(140);

	public float ShadowBlur { get; init; } = 6f;

	public SKPoint ShadowOffset { get; init; } = new(3, 3);

	/// <summary>How the shadow composites onto what is behind it.</summary>
	public SKBlendMode ShadowBlendMode { get; init; } = SKBlendMode.SrcOver;

	/// <summary>The color of the blurred glow drawn behind the label; transparent alpha hides it.</summary>
	public SKColor GlowColor { get; init; } = SKColors.Transparent;

	/// <summary>The glow's blur radius in pixels.</summary>
	public float GlowBlur { get; init; }

	/// <summary>Gaussian blur applied to the whole label; 0 is sharp.</summary>
	public float Blur { get; init; }

	/// <summary>How the whole text block composites onto what is already on the canvas.</summary>
	public SKBlendMode BlendMode { get; init; } = SKBlendMode.SrcOver;

	/// <summary>Top-left corner of the text block, in image/document pixels.</summary>
	public SKPoint Position { get; init; } = new(20, 20);
}
