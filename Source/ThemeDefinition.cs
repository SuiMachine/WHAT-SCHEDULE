using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WhatSchedule;

public static class ThemeDefinition
{
	public static readonly ISolidColorBrush Background = new ImmutableSolidColorBrush(0xFF202020);
	public static readonly ISolidColorBrush Border = new ImmutableSolidColorBrush(0xFF3F3F46);
	public static readonly ISolidColorBrush RulerBackground = new ImmutableSolidColorBrush(0xFF272727);
	public static readonly ISolidColorBrush RulerTick = new ImmutableSolidColorBrush(0xFF999999);
	public static readonly ISolidColorBrush RulerLabel = new ImmutableSolidColorBrush(0xFFD4D4D4);
	public static readonly ISolidColorBrush Selection = new ImmutableSolidColorBrush(0xFF4CC2FF);
	public static readonly ISolidColorBrush HandleFill = new ImmutableSolidColorBrush(Colors.White);
	public static readonly ISolidColorBrush Guide = new ImmutableSolidColorBrush(0xFF00A2ED);

	public static readonly IPen RulerBorderPen = new Pen(Border, 1);
	public static readonly IPen TickPen = new Pen(RulerTick, 1);
	public static readonly IPen SelectionPen = new Pen(Selection, 1);
	public static readonly IPen GuidePen = new Pen(Guide, 1) { DashStyle = DashStyle.Dash };
	public static readonly Typeface LabelTypeface = Typeface.Default;

	public const double RulerThickness = 22;
	public const double MinZoom = 0.02;
	public const double MaxZoom = 32;
	public const double WheelFactor = 1.1;
	public const double MinVisibleEdge = 40;
	public const double MinMajorTickSpacing = 60;
	public const double LabelFontSize = 10;
	public const double HandleSize = 8;
	public const double HandleHitRadius = 6;
	public const double RotateHandleOffset = 20;
	public const double RotationSnap = 15;
	public const double GuideHitTolerance = 2;
	public const double MinElementSize = 8;
	public const float MinFontSize = 4;
}
