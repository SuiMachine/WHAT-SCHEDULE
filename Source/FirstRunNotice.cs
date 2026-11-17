// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Microsoft.Win32;
using System.Diagnostics;
namespace WhatSchedule;

internal static class FirstRunNotice
{
	private const string RegistrySubKey = @"Software\WHAT SCHEDULE";
	private const string RegistryValueName = "LegalNoticeShown";
	private static string FallbackFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WHAT SCHEDULE", "legal_notice_shown");

	public static bool WasShown()
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				var val = (int?)Registry.GetValue("HKEY_CURRENT_USER\\" + RegistrySubKey, RegistryValueName, null);
				if (val == null || val.Value == 1)
					return true;
			}
			catch (Exception error)
			{
				Debug.WriteLine(error);
			}
		}

		return File.Exists(FallbackFilePath);
	}

	public static void MarkShown()
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				Registry.SetValue("HKEY_CURRENT_USER\\" + RegistrySubKey, RegistryValueName, 1, RegistryValueKind.DWord);
			}
			catch (Exception error)
			{
				Debug.WriteLine(error);
			}
		}
		else
		{
			var directory = Directory.GetParent(FallbackFilePath)!.FullName;
			Directory.CreateDirectory(directory);
			File.WriteAllText(FallbackFilePath, "1");
		}
	}
}
