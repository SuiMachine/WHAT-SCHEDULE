using Avalonia.Input;

namespace WhatSchedule.Controls;

/// <summary>The cursors used by the preview canvas interactions.</summary>
internal static class CanvasCursors
{
	private static readonly Cursor TopLeft = new(StandardCursorType.TopLeftCorner);
	private static readonly Cursor TopRight = new(StandardCursorType.TopRightCorner);
	private static readonly Cursor BottomRight = new(StandardCursorType.BottomRightCorner);
	private static readonly Cursor BottomLeft = new(StandardCursorType.BottomLeftCorner);

	public static readonly Cursor Move = new(StandardCursorType.SizeAll);
	public static readonly Cursor Rotate = new(StandardCursorType.Cross);
	public static readonly Cursor HorizontalGuide = new(StandardCursorType.SizeNorthSouth);
	public static readonly Cursor VerticalGuide = new(StandardCursorType.SizeWestEast);

	public static Cursor ForCorner(Corner corner) => corner switch
	{
		Corner.TopLeft => TopLeft,
		Corner.TopRight => TopRight,
		Corner.BottomRight => BottomRight,
		_ => BottomLeft,
	};
}
