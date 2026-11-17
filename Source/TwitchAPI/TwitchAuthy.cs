// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using WhatSchedule.Controls.Dialogs;

namespace WhatSchedule.TwitchAPI
{
	public class TwitchAuthy
	{
		public class AuthyUserData
		{
			public string? user_id { get; set; }
			public string? login { get; set; }
			public string? client_id { get; set; }
			public long? expires_in { get; set; }
			public string[]? scopes { get; set; }
			[JsonIgnore] public string AuthyToken { get; set; } = string.Empty;
		}

		private static string AUTHY_PATH => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WHAT SCHEDULE", "twitch_authy.dat");
		private static byte[] ENTROPY => Encoding.UTF8.GetBytes("SuiVT.WhatSchedule");
		public const string TWITCH_CLIENT_ID = "z4s5upzay7mzanjlzzs9hfe9bc730d";

		public static async Task<AuthyUserData?> GetCurrentAuthy(MainWindow window, bool createNewIfNoneExists = true)
		{
			string currentAuthyToken = string.Empty;
			if (File.Exists(AUTHY_PATH))
			{
				var fileContent = File.ReadAllBytes(AUTHY_PATH);

				byte[] decryptedBytes;
				if (OperatingSystem.IsWindows())
					decryptedBytes = ProtectedData.Unprotect(fileContent, ENTROPY, DataProtectionScope.CurrentUser);
				else
					decryptedBytes = fileContent; //Probably could use some encryption - not a a fan of running a process
				currentAuthyToken = Encoding.UTF8.GetString(decryptedBytes);
			}

			if (string.IsNullOrWhiteSpace(currentAuthyToken))
			{
				if (createNewIfNoneExists)
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
					{
						FileName = $"https://id.twitch.tv/oauth2/authorize?response_type=token&client_id={TWITCH_CLIENT_ID}&redirect_uri=https://suimachine.github.io/twitchauthy/",
						UseShellExecute = true
					});

					var dialogResult = await TextPromptDialog.ShowDialog(window, "Twitch authorization", "Please provide authorization token");
					if (!string.IsNullOrEmpty(dialogResult))
					{
						currentAuthyToken = dialogResult;
						byte[] bytesToStore;
						if (OperatingSystem.IsWindows())
							bytesToStore = ProtectedData.Protect(Encoding.UTF8.GetBytes(currentAuthyToken), ENTROPY, DataProtectionScope.CurrentUser);
						else
							bytesToStore = Encoding.UTF8.GetBytes(currentAuthyToken);

						var dir = Directory.GetParent(AUTHY_PATH)!.FullName;
						Directory.CreateDirectory(dir);
						File.WriteAllBytes(AUTHY_PATH, bytesToStore);
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(currentAuthyToken))
			{
				using var http = new HttpClient();
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", currentAuthyToken);
				using var response = await http.GetAsync("https://id.twitch.tv/oauth2/validate");
				response.EnsureSuccessStatusCode();

				string body = await response.Content.ReadAsStringAsync();
				var obj = JsonConvert.DeserializeObject<AuthyUserData>(body);
				obj?.AuthyToken = currentAuthyToken;
				return obj;
			}
			else
				return null;
		}
	}
}
