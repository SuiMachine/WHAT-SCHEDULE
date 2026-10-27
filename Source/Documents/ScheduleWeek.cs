using WhatSchedule.Elements;

namespace WhatSchedule.Documents;

/// <summary>The streamer's plan for one day of the week: the stream's title, whether it happens and when it starts.</summary>
public sealed class ScheduleDayEntry
{
	/// <summary>The start time a day's time is initialized with and falls back to when none is set.</summary>
	public static readonly TimeOnly DefaultStreamTime = new(17, 30);

	/// <summary>The title of that day's stream, as schedule labels show it.</summary>
	public string StreamTitle { get; set; } = "";

	/// <summary>Whether the streamer streams that day; a day without a stream has no title and no time.</summary>
	public bool IsStreaming { get; set; }

	/// <summary>When the stream starts that day; days are initialized with <see cref="DefaultStreamTime"/> until another time is set.</summary>
	public TimeOnly? StreamTime { get; set; } = DefaultStreamTime;
}

/// <summary>The stream plan of the week the schedule covers: one entry per day, Monday first.</summary>
public sealed class ScheduleWeek
{
	/// <summary>The seven day entries, indexed by <see cref="WhatSchedule.Elements.ScheduleDay"/>, Monday first; each day's title starts as a placeholder named after the day.</summary>
	public ScheduleDayEntry[] Days { get; set; } = [.. Enum.GetValues<ScheduleDay>().Select(day => new ScheduleDayEntry { StreamTitle = day + " title" })];

	/// <summary>How the week's stream start times are displayed as text, e.g. "17:30" or "5:30 PM".</summary>
	public ScheduleTimeFormat TimeFormat { get; set; } = ScheduleTimeFormat.Hour24;

	/// <summary>A deep copy of the week, so an editor can modify one without touching the other.</summary>
	public ScheduleWeek Copy() => new()
	{
		TimeFormat = TimeFormat,
		Days = [.. Days.Select(day => new ScheduleDayEntry { StreamTitle = day.StreamTitle, IsStreaming = day.IsStreaming, StreamTime = day.StreamTime })],
	};

	/// <summary>All variables the week defines, one title and one start time per day, each with the value it produces.</summary>
	public IReadOnlyList<ScheduleVariable> Variables => [.. Enum.GetValues<ScheduleDay>().SelectMany(EnumerateDay)];

	/// <summary>The two variables one day of the week defines: its stream title and its formatted start time.</summary>
	private IEnumerable<ScheduleVariable> EnumerateDay(ScheduleDay day)
	{
		var entry = Days[(int)day];
		yield return new ScheduleVariable(ScheduleVariables.TitleName(day), entry.StreamTitle);
		yield return new ScheduleVariable(ScheduleVariables.TimeName(day), ScheduleTimeFormatting.Format(entry.StreamTime ?? ScheduleDayEntry.DefaultStreamTime, TimeFormat));
	}
}
