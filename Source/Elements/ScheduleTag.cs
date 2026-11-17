// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

namespace WhatSchedule.Elements;

/// <summary>A day of the week a schedule tag can be assigned to.</summary>
public enum ScheduleDay
{
	Monday,
	Tuesday,
	Wednesday,
	Thursday,
	Friday,
	Saturday,
	Sunday
}

/// <summary>What the streamer does on the day a schedule tag names.</summary>
public enum ScheduleActivity
{
	Streaming,
	TakingABreak
}

/// <summary>One of the fourteen schedule tags: what the streamer does on one day of the week, e.g. "Monday: Streaming".</summary>
public readonly record struct ScheduleTag(ScheduleDay Day, ScheduleActivity Activity)
{
	/// <summary>The tag as shown in menus, e.g. "Monday: Taking a break".</summary>
	public string DisplayName => Day + ": " + ActivityName(Activity);

	/// <summary>The tag's serialized form, the day and the activity joined by a colon, e.g. "Monday:Streaming".</summary>
	public string SaveName => Day + ":" + Activity;

	/// <summary>The activity as shown in menus, e.g. "Taking a break" instead of the enum's own name.</summary>
	public static string ActivityName(ScheduleActivity activity) => activity == ScheduleActivity.TakingABreak ? "Taking a break" : "Streaming";

	/// <summary>Parses a tag's serialized form; returns null when the name names neither a day nor an activity.</summary>
	public static ScheduleTag? TryParse(string? name)
	{
		if (name is null)
			return null;

		var parts = name.Split(':');
		if (parts.Length != 2 || !Enum.TryParse(parts[0], out ScheduleDay day) || !Enum.TryParse(parts[1], out ScheduleActivity activity))
			return null;

		return new ScheduleTag(day, activity);
	}
}
