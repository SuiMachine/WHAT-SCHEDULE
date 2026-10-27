namespace WhatSchedule.Documents;

/// <summary>The streamer's plan for one day of the week: the stream's title, whether it happens and when it starts.</summary>
public sealed class ScheduleDayEntry
{
	/// <summary>The title of that day's stream, as schedule labels show it.</summary>
	public string StreamTitle { get; set; } = "";

	/// <summary>Whether the streamer streams that day; a day without a stream has no title and no time.</summary>
	public bool IsStreaming { get; set; }

	/// <summary>When the stream starts that day, or null when no time is set.</summary>
	public TimeOnly? StreamTime { get; set; }
}

/// <summary>The stream plan of the week the schedule covers: one entry per day, Monday first.</summary>
public sealed class ScheduleWeek
{
	/// <summary>The seven day entries, indexed by <see cref="WhatSchedule.Elements.ScheduleDay"/>, Monday first.</summary>
	public ScheduleDayEntry[] Days { get; set; } = [new(), new(), new(), new(), new(), new(), new()];

	/// <summary>A deep copy of the week, so an editor can modify one without touching the other.</summary>
	public ScheduleWeek Copy() => new()
	{
		Days = [.. Days.Select(day => new ScheduleDayEntry { StreamTitle = day.StreamTitle, IsStreaming = day.IsStreaming, StreamTime = day.StreamTime })],
	};
}
