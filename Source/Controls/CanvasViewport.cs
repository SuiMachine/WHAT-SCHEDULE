using Avalonia;
using SkiaSharp;

namespace WhatSchedule.Controls;

/// <summary>
/// The pan/zoom state of the document preview: where the document image lands
/// on screen and how to convert between screen and document coordinates.
/// </summary>
internal sealed class CanvasViewport
{
	public double Scale { get; private set; } = 1;
	public double OffsetX { get; private set; }
	public double OffsetY { get; private set; }

	/// <summary>True once the document has been fitted to the control for the first time.</summary>
	public bool HasInitialFit { get; private set; }

	/// <summary>Forgets the current fit so the next render re-fits the document into view.</summary>
	public void ResetFit() => HasInitialFit = false;

	/// <summary>Scales the document to fit into <paramref name="content"/>, once. Does nothing afterwards until <see cref="ResetFit"/>.</summary>
	public void TryInitialFit(Rect content, Size documentSize)
	{
		if (HasInitialFit || content.Width <= 0 || content.Height <= 0 || documentSize.Width <= 0 || documentSize.Height <= 0)
			return;

		Scale = Math.Min(Math.Min(content.Width / documentSize.Width, content.Height / documentSize.Height), 1);
		OffsetX = 0;
		OffsetY = 0;
		HasInitialFit = true;
	}

	public Rect GetImageRect(Rect content, Size documentSize)
	{
		var width = documentSize.Width * Scale;
		var height = documentSize.Height * Scale;
		return new Rect(
			content.X + content.Width / 2 + OffsetX - width / 2,
			content.Y + content.Height / 2 + OffsetY - height / 2,
			width,
			height);
	}

	/// <summary>Moves the document to absolute pan offsets, keeping it clamped over the content area.</summary>
	public void MoveTo(double offsetX, double offsetY, Rect content, Size documentSize)
	{
		OffsetX = offsetX;
		OffsetY = offsetY;
		ClampOffsets(content, documentSize);
	}

	/// <summary>Zooms by <paramref name="factor"/> while keeping the document point under <paramref name="cursor"/> fixed. Returns false when a zoom limit was reached.</summary>
	public bool Zoom(double factor, Point cursor, Rect content, Size documentSize)
	{
		var newScale = Math.Clamp(Scale * factor, ThemeDefinition.MinZoom, ThemeDefinition.MaxZoom);
		if (newScale == Scale)
			return false;

		// Keep the document point under the cursor fixed while zooming.
		var imageRect = GetImageRect(content, documentSize);
		var pixelX = (cursor.X - imageRect.X) / Scale;
		var pixelY = (cursor.Y - imageRect.Y) / Scale;

		Scale = newScale;
		SetImageRect(new Rect(
			cursor.X - pixelX * Scale,
			cursor.Y - pixelY * Scale,
			documentSize.Width * Scale,
			documentSize.Height * Scale), content, documentSize);
		return true;
	}

	public SKPoint ScreenToDocument(Point position, Rect imageRect) => new(
		(float)((position.X - imageRect.X) / Scale),
		(float)((position.Y - imageRect.Y) / Scale));

	public Point DocumentToScreen(SKPoint point, Rect imageRect) => new(imageRect.X + point.X * Scale, imageRect.Y + point.Y * Scale);

	private void SetImageRect(Rect rect, Rect content, Size documentSize)
	{
		OffsetX = rect.X - content.X - content.Width / 2 + documentSize.Width * Scale / 2;
		OffsetY = rect.Y - content.Y - content.Height / 2 + documentSize.Height * Scale / 2;
		ClampOffsets(content, documentSize);
	}

	private void ClampOffsets(Rect content, Size documentSize)
	{
		var width = documentSize.Width * Scale;
		var height = documentSize.Height * Scale;

		// The document must keep at least MinVisibleEdge of overlap with the content rect
		// on each axis, but its edges are allowed to hang off-screen.
		if (content.Width > 2 * ThemeDefinition.MinVisibleEdge)
		{
			OffsetX = Math.Clamp(OffsetX, ThemeDefinition.MinVisibleEdge - content.Width / 2 - width / 2, content.Width / 2 - ThemeDefinition.MinVisibleEdge + width / 2);
		}

		if (content.Height > 2 * ThemeDefinition.MinVisibleEdge)
		{
			OffsetY = Math.Clamp(OffsetY, ThemeDefinition.MinVisibleEdge - content.Height / 2 - height / 2, content.Height / 2 - ThemeDefinition.MinVisibleEdge + height / 2);
		}
	}
}
