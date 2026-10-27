namespace WhatSchedule.Documents;

/// <summary>A named conversion of the week's stream start times into another time zone, e.g. suffix "ET"
/// for the US Eastern zone, producing variables like "Monday_Time_ET" and "Monday_Weekday_ET".</summary>
public sealed class ScheduleZoneConversion
{
	/// <summary>The suffix the conversion's variable names carry, e.g. "ET" in "Monday_Time_ET"; letters, digits and underscores only.</summary>
	public string Suffix { get; set; } = "";

	/// <summary>The zone the times are converted into, as a portable (IANA) ID; null converts into the system's local zone.</summary>
	public string? TimeZoneId { get; set; }
}

/// <summary>Resolves the schedule's time zones and converts stream start times between them.</summary>
public static class ScheduleZones
{
	/// <summary>The zone the given portable ID names: the system's local zone when the ID is null or empty,
	/// UTC when the ID is unknown to this system, so a document saved anywhere always renders.</summary>
	public static TimeZoneInfo Resolve(string? id)
	{
		if (string.IsNullOrEmpty(id))
			return TimeZoneInfo.Local;

		if (FindZone(id) is { } zone)
			return zone;

		//An ID from the other system (an IANA ID on Windows, a Windows ID elsewhere) converts to the one this system knows.
		if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && FindZone(windows) is { } byWindows)
			return byWindows;
		if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && FindZone(iana) is { } byIana)
			return byIana;

		return TimeZoneInfo.Utc;
	}

	/// <summary>The zone's portable ID for storing in a document: its IANA ID when the system has one, its system ID otherwise.</summary>
	public static string PortableId(TimeZoneInfo zone) => TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;

	/// <summary>Converts a wall time belonging to <paramref name="from"/> into <paramref name="to"/>. The conversion applies
	/// both zones' daylight saving rules to that concrete date, so a time that crosses a transition converts correctly;
	/// a wall time that does not exist (a spring-forward gap) shifts past the gap, an ambiguous one resolves to standard time.</summary>
	public static DateTime Convert(DateOnly date, TimeOnly time, TimeZoneInfo from, TimeZoneInfo to)
	{
		//Unspecified kind: the wall time is local in the streamer's zone, so it must not carry a UTC marker.
		var wall = date.ToDateTime(time);
		return TimeZoneInfo.ConvertTime(TimeZoneInfo.ConvertTimeToUtc(wall, from), to);
	}

	/// <summary>The zone the ID names, or null when this system knows no such zone.</summary>
	private static TimeZoneInfo? FindZone(string id)
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(id);
		}
		catch (TimeZoneNotFoundException)
		{
			return null;
		}
		catch (InvalidTimeZoneException)
		{
			return null;
		}
	}
}
