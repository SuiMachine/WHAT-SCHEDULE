using SkiaSharp;

namespace WhatSchedule.Extensions
{
	public static class SKPointsExtensions
	{
		public static SKPoint Snap(this SKPoint point)
		{
			return new SKPoint(MathF.Round(point.X), MathF.Round(point.Y));
		}
	}
}
