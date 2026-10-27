using WhatSchedule.Elements;

namespace WhatSchedule.Documents;

/// <summary>A variable a schedule defines: its name and the value it currently produces.</summary>
public readonly record struct ScheduleVariable(string Name, string? Value);

/// <summary>The variable names the schedule's week data always reserves; custom variables cannot take them.</summary>
public static class ScheduleVariables
{
	/// <summary>The comparer variable names are matched with, so "monday_title" is the reserved "Monday_Title".</summary>
	public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

	/// <summary>The variable name a day's stream title is referenced by, e.g. "Monday_Title".</summary>
	public static string TitleName(ScheduleDay day) => day + "_Title";

	/// <summary>The variable name a day's stream start time is referenced by, e.g. "Monday_Time".</summary>
	public static string TimeName(ScheduleDay day) => day + "_Time";

	/// <summary>The variable name a day's date with the abbreviated month is referenced by, e.g. "Monday_Date" for "Feb 3".</summary>
	public static string DateName(ScheduleDay day) => day + "_Date";

	/// <summary>The variable name a day's date with the full month name is referenced by, e.g. "Monday_DateLong" for "February 3".</summary>
	public static string DateLongName(ScheduleDay day) => day + "_DateLong";

	/// <summary>The variable name a day's day of the month is referenced by, e.g. "Monday_Day" for "3".</summary>
	public static string DayName(ScheduleDay day) => day + "_Day";

	/// <summary>The names the week data reserves: one stream title, one start time and three date variables per day.</summary>
	public static readonly IReadOnlyList<string> ReservedNames =
		Enum.GetValues<ScheduleDay>().SelectMany(day => new[] { TitleName(day), TimeName(day), DateName(day), DateLongName(day), DayName(day) }).ToArray();

	/// <summary>Whether a variable name is one the week data reserves.</summary>
	public static bool IsReserved(string name) => ReservedNames.Contains(name, NameComparer);

	/// <summary>All variables the document defines: the week's day data, then the custom variables, each with the value it currently produces.</summary>
	public static ScheduleVariable[] Collect(Document document) =>
		[.. document.Week.Variables, .. document.Variables.Select(pair => new ScheduleVariable(pair.Key, pair.Value))];
}
