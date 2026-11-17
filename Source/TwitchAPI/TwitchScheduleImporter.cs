// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;

namespace WhatSchedule.TwitchAPI
{
	public class TwitchScheduleImporter
	{
		public class TwitchPaginationWrapper
		{
			public TwitchScheduleData? data { get; set; }
		}

		public class TwitchScheduleData
		{
			public ScheduleSegment[]? segments { get; set; }
			public string? data { get; set; }
			public string? broadcaster_id { get; set; }
			public string? broadcaster_name { get; set; }
			public string? broadcaster_login { get; set; }
			public bool? vacation { get; set; }

		}

		public class ScheduleSegment
		{
			public string? id { get; set; }
			public DateTime? start_time { get; set; }
			public DateTime? end_time { get; set; }
			public string? title { get; set; }
			public string? canceled_until { get; set; }
			public string? category { get; set; }
			public bool? is_recurring { get; set; }
		}

		public static async Task<ScheduleSegment[]?> ImportScheduleFromTwitchAsync(MainWindow window, DateOnly weekStart)
		{
			var authy = await TwitchAuthy.GetCurrentAuthy(window);
			if (authy == null)
				return null;

			using var http = new HttpClient();
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authy.AuthyToken);
			http.DefaultRequestHeaders.Add("Client-Id", TwitchAuthy.TWITCH_CLIENT_ID);
			var url = $"https://api.twitch.tv/helix/schedule?broadcaster_id={authy.user_id}";
			using var response = await http.GetAsync(url);
			response.EnsureSuccessStatusCode();
			var body = await response.Content.ReadAsStringAsync();

			var obj = JsonConvert.DeserializeObject<TwitchPaginationWrapper>(body);

			return obj?.data?.segments;
		}
	}
}
