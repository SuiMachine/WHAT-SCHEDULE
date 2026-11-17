// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Globalization;

namespace WhatSchedule.Documents;

/// <summary>The culture the schedule's dates are displayed as variables with.</summary>
public enum ScheduleDateCulture
{
	/// <summary>The operating system's culture, e.g. "lut 3" on a Polish system.</summary>
	Local,

	/// <summary>US English, e.g. "Feb 3", whatever the system's culture is.</summary>
	EnUs,
}

/// <summary>Resolves the culture a schedule displays its dates with.</summary>
public static class ScheduleDates
{
	/// <summary>The culture the given setting displays dates with.</summary>
	public static CultureInfo Resolve(ScheduleDateCulture culture) =>
		culture == ScheduleDateCulture.EnUs ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.CurrentCulture;
}
