using WhatSchedule.Elements;

namespace WhatSchedule.Documents;

/// <summary>The variable names the schedule's week data always reserves; custom variables cannot take them.</summary>
public static class ScheduleVariables
{
	/// <summary>The comparer variable names are matched with, so "monday_title" is the reserved "Monday_Title".</summary>
	public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

	/// <summary>The variable name a day's stream title is referenced by, e.g. "Monday_Title".</summary>
	public static string TitleName(ScheduleDay day) => day + "_Title";

	/// <summary>The names the week data reserves: one stream title variable per day.</summary>
	public static readonly IReadOnlyList<string> ReservedNames =
		Enum.GetValues<ScheduleDay>().Select(TitleName).ToArray();

	/// <summary>Whether a variable name is one the week data reserves.</summary>
	public static bool IsReserved(string name) => ReservedNames.Contains(name, NameComparer);
}
