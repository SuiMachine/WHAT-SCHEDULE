using WhatSchedule.Documents;

namespace WhatSchedule.Elements;

/// <summary>The display filter chosen in the main window's menu, i.e. how the canvas decides which tagged elements stay out of the drawing.</summary>
public enum ScheduleFilter
{
	/// <summary>Displays everything.</summary>
	None,

	/// <summary>Displays everything except the elements tagged as taking a break.</summary>
	Streaming,

	/// <summary>Displays everything except the elements tagged as streaming.</summary>
	TakingABreak,

	/// <summary>Displays each day the way the week's plan says it looks: on days the streamer streams, the taking-a-break elements stay out; on days off, the streaming ones do.</summary>
	Final
}

/// <summary>
/// The canvas display filter resolved for rendering: the activity kept out per day of the week.
/// A day with no hidden activity renders whole; a null filter (and every export) renders the whole document.
/// </summary>
public sealed class TagFilter
{
	/// <summary>The activity each day's tags of are kept out of the display, e.g. TakingABreak under the "Streaming" choice.</summary>
	private readonly Func<ScheduleDay, ScheduleActivity?> hiddenActivity;

	private TagFilter(Func<ScheduleDay, ScheduleActivity?> hiddenActivity) => this.hiddenActivity = hiddenActivity;

	/// <summary>Whether an element carrying <paramref name="tag"/> is kept out of the canvas display.</summary>
	public bool IsHidden(ScheduleTag tag) => hiddenActivity(tag.Day) == tag.Activity;

	/// <summary>The "Streaming" choice: every day keeps its taking-a-break tags out of the display.</summary>
	public static readonly TagFilter Streaming = new(_ => ScheduleActivity.TakingABreak);

	/// <summary>The "Taking a break" choice: every day keeps its streaming tags out of the display.</summary>
	public static readonly TagFilter TakingABreak = new(_ => ScheduleActivity.Streaming);

	/// <summary>The "Final" choice: a day the week's plan streams keeps its taking-a-break tags out, a day without a stream keeps its streaming tags out. Built from the live week on every render, so it follows the edit schedule window.</summary>
	public static TagFilter FromWeek(ScheduleWeek week) => new(day =>
		week.Days[(int)day].IsStreaming ? ScheduleActivity.TakingABreak : ScheduleActivity.Streaming);
}
