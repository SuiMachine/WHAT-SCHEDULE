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

	/// <summary>The Monday the week starts on, which the days' dates derive from; initialized to the current week's Monday.</summary>
	public DateOnly? WeekStart { get; set; } = CurrentWeekStart();

	/// <summary>The culture the week's dates are displayed as text, e.g. "Feb 3" or "lut 3".</summary>
	public ScheduleDateCulture DateCulture { get; set; } = ScheduleDateCulture.Local;

	/// <summary>The zone the week's stream start times belong to, as a portable (IANA) ID; null means the system's local zone.</summary>
	public string? TimeZoneId { get; set; }

	/// <summary>The named conversions of the week's stream start times into other time zones, each producing its own set of variables.</summary>
	public List<ScheduleZoneConversion> Conversions { get; set; } = [];

	/// <summary>A deep copy of the week, so an editor can modify one without touching the other.</summary>
	public ScheduleWeek Copy() => new()
	{
		TimeFormat = TimeFormat,
		WeekStart = WeekStart,
		DateCulture = DateCulture,
		TimeZoneId = TimeZoneId,
		Conversions = [.. Conversions.Select(conversion => new ScheduleZoneConversion { Suffix = conversion.Suffix, TimeZoneId = conversion.TimeZoneId })],
		Days = [.. Days.Select(day => new ScheduleDayEntry { StreamTitle = day.StreamTitle, IsStreaming = day.IsStreaming, StreamTime = day.StreamTime })],
	};

	/// <summary>All variables the week defines: per day its stream title, start time and three date forms, plus the converted
	/// time, dates and weekday each of its conversions defines for that day, each with the value it produces.</summary>
	public IReadOnlyList<ScheduleVariable> Variables => [.. Enum.GetValues<ScheduleDay>().SelectMany(EnumerateDay)];

	/// <summary>The five variables one day of the week defines: its stream title, its formatted start time and its three date forms,
	/// plus the converted variables each of the week's conversions defines for that day.</summary>
	private IEnumerable<ScheduleVariable> EnumerateDay(ScheduleDay day)
	{
		var entry = Days[(int)day];
		var date = WeekStart?.AddDays((int)day);
		var culture = ScheduleDates.Resolve(DateCulture);
		yield return new ScheduleVariable(ScheduleVariables.TitleName(day), entry.StreamTitle);
		yield return new ScheduleVariable(ScheduleVariables.TimeName(day), ScheduleTimeFormatting.Format(entry.StreamTime ?? ScheduleDayEntry.DefaultStreamTime, TimeFormat));
		yield return new ScheduleVariable(ScheduleVariables.DateName(day), date?.ToString("MMM d", culture));
		yield return new ScheduleVariable(ScheduleVariables.DateLongName(day), date?.ToString("MMMM d", culture));
		yield return new ScheduleVariable(ScheduleVariables.DayName(day), date?.Day.ToString(culture));

		//The conversion needs the concrete date: the zones' rules decide that occurrence's daylight saving, so the
		//same wall time can convert differently in two different weeks. Without a week the dates stay empty like above.
		if (date is not { } occurrence || Conversions.Count == 0)
			yield break;

		var time = entry.StreamTime ?? ScheduleDayEntry.DefaultStreamTime;
		var from = ScheduleZones.Resolve(TimeZoneId);
		foreach (var conversion in Conversions)
		{
			//The converted time can land on another day, so the dates and the weekday follow it there.
			var converted = ScheduleZones.Convert(occurrence, time, from, ScheduleZones.Resolve(conversion.TimeZoneId));
			yield return new ScheduleVariable(ScheduleVariables.ConvertedTimeName(day, conversion.Suffix), ScheduleTimeFormatting.Format(TimeOnly.FromDateTime(converted), TimeFormat));
			yield return new ScheduleVariable(ScheduleVariables.ConvertedDateName(day, conversion.Suffix), converted.ToString("MMM d", culture));
			yield return new ScheduleVariable(ScheduleVariables.ConvertedDateLongName(day, conversion.Suffix), converted.ToString("MMMM d", culture));
			yield return new ScheduleVariable(ScheduleVariables.ConvertedDayName(day, conversion.Suffix), converted.Day.ToString(culture));
			yield return new ScheduleVariable(ScheduleVariables.ConvertedWeekdayName(day, conversion.Suffix), culture.DateTimeFormat.GetDayName(converted.DayOfWeek));
		}
	}

	/// <summary>The Monday of the week that contains today.</summary>
	private static DateOnly CurrentWeekStart()
	{
		var today = DateOnly.FromDateTime(DateTime.Now);
		return today.AddDays(-(((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
	}
}
