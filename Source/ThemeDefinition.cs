using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WhatSchedule;

public static class ThemeDefinition
{
	public static readonly ISolidColorBrush Background = new ImmutableSolidColorBrush(0xFFF0F0F0);
	public static readonly ISolidColorBrush Border = new ImmutableSolidColorBrush(Colors.Gray);
	public static readonly ISolidColorBrush RulerBackground = new ImmutableSolidColorBrush(0xFFE4E4E4);
	public static readonly ISolidColorBrush RulerTick = new ImmutableSolidColorBrush(0xFF666666);
	public static readonly ISolidColorBrush RulerLabel = new ImmutableSolidColorBrush(0xFF333333);

	public static readonly IPen RulerBorderPen = new Pen(ThemeDefinition.Border, 1);
	public static readonly IPen TickPen = new Pen(ThemeDefinition.RulerTick, 1);
	public static readonly Typeface LabelTypeface = Typeface.Default;

	public const double RulerThickness = 22;
	public const double MinZoom = 0.02;
	public const double MaxZoom = 32;
	public const double WheelFactor = 1.1;
	public const double MinVisibleEdge = 40;
	public const double MinMajorTickSpacing = 60;
	public const double LabelFontSize = 10;
}
