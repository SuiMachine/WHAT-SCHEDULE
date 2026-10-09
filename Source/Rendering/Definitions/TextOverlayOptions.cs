using SkiaSharp;

namespace WhatSchedule.Rendering.Definitions;

public sealed record TextOverlayOptions
{
	public required string Text { get; init; }

	public string FontFamily { get; init; } = "Arial";

	public float FontSize { get; init; } = 48f;

	public SKColor TextColor { get; init; } = SKColors.White;

	public SKColor OutlineColor { get; init; } = SKColors.Black;

	public float OutlineWidth { get; init; } = 2f;

	public SKColor ShadowColor { get; init; } = SKColors.Black.WithAlpha(140);

	public float ShadowBlur { get; init; } = 6f;

	public SKPoint ShadowOffset { get; init; } = new(3, 3);

	/// <summary>Top-left corner of the text block, in image/document pixels.</summary>
	public SKPoint Position { get; init; } = new(20, 20);
}
