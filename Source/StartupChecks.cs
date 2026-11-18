// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Microsoft.Win32;
using System.Diagnostics;
namespace WhatSchedule;

internal static class StartupChecks
{
	private const string RegistrySubKey = @"Software\WHAT SCHEDULE";
	private const string RegistryValueLegalName = "LegalNoticeShown";
	private const string RegistryValueLastProjectName = "LastProject";

	private static string FallbackLegalShownFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WHAT SCHEDULE", "legal_notice_shown");
	private static string FallbackLastProjectFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WHAT SCHEDULE", "last_project");

	public static bool WasGPLShown()
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				var val = (int?)Registry.GetValue("HKEY_CURRENT_USER\\" + RegistrySubKey, RegistryValueLegalName, null);
				if (val == null || val.Value == 1)
					return true;
			}
			catch (Exception error)
			{
				Debug.WriteLine(error);
			}
		}

		return File.Exists(FallbackLegalShownFilePath);
	}

	public static void MarkGPLAsShown()
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				Registry.SetValue("HKEY_CURRENT_USER\\" + RegistrySubKey, RegistryValueLegalName, 1, RegistryValueKind.DWord);
			}
			catch (Exception error)
			{
				Debug.WriteLine(error);
			}
		}
		else
		{
			var directory = Directory.GetParent(FallbackLegalShownFilePath)!.FullName;
			Directory.CreateDirectory(directory);
			File.WriteAllText(FallbackLegalShownFilePath, "1");
		}
	}

	public static string GetLastProjectFile()
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				var val = (string?)Registry.GetValue("HKEY_CURRENT_USER\\" + RegistrySubKey, RegistryValueLastProjectName, "");
				if (val == null)
					return "";
				else
					return val;
			}
			catch (Exception error)
			{
				Debug.WriteLine(error);
			}
		}

		if(File.Exists(FallbackLastProjectFilePath))
			return File.ReadAllLines(FallbackLastProjectFilePath).FirstOrDefault("") ?? "";
		else
			return "";
	}

	public static string SetLastProjectFile(string filePath)
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				Registry.SetValue("HKEY_CURRENT_USER\\" + RegistrySubKey, RegistryValueLastProjectName, filePath, RegistryValueKind.String);
			}
			catch (Exception error)
			{
				Debug.WriteLine(error);
			}
		}
		else
		{
			var directory = Directory.GetParent(FallbackLastProjectFilePath)!.FullName;
			Directory.CreateDirectory(directory);
			File.WriteAllText(FallbackLastProjectFilePath, filePath);
		}
		return filePath;
	}
}
