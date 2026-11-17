// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Globalization;

namespace WhatSchedule.Documents;

/// <summary>How a schedule's stream start times are displayed as text, e.g. as variables in labels.</summary>
public enum ScheduleTimeFormat
{
	/// <summary>24-hour clock with a leading zero, e.g. "17:30".</summary>
	Hour24,

	/// <summary>12-hour clock with an AM/PM suffix, e.g. "5:30 PM".</summary>
	Hour12,
}

/// <summary>Formats stream start times after a schedule's time format.</summary>
public static class ScheduleTimeFormatting
{
	/// <summary>The time formatted for display, e.g. "17:30" or "5:30 PM".</summary>
	public static string Format(TimeOnly time, ScheduleTimeFormat format)
	{
		if (format == ScheduleTimeFormat.Hour12)
		{
			//Cultures without a designator of their own (e.g. Polish) fall back to the English one.
			var info = CultureInfo.CurrentCulture.DateTimeFormat;
			var designator = time.Hour < 12 ? info.AMDesignator : info.PMDesignator;
			if (string.IsNullOrEmpty(designator))
				designator = time.Hour < 12 ? "AM" : "PM";

			return $"{time.ToString("h:mm", CultureInfo.CurrentCulture)} {designator}";
		}

		return time.ToString("HH:mm", CultureInfo.CurrentCulture);
	}
}
