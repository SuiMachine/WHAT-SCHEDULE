// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

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
