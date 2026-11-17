// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Avalonia.SpellChecker;

namespace WhatSchedule
{
	public static class SpellCheckerUniversalInstance
	{
		private static TextBoxSpellChecker? m_Instance;
		public static TextBoxSpellChecker Instance
		{
			get
			{
				if (m_Instance == null)
				{
					var directory = Path.Combine(AppContext.BaseDirectory, "Dictionaries");
					if (Directory.Exists(directory))
					{
						var dicFiles = Directory.GetFiles(directory, "*.dic", SearchOption.AllDirectories); //unfortunate extension pick... at least it's not ass, I guess?
						var affFiles = Directory.GetFiles(directory, "*.aff", SearchOption.AllDirectories);

						var trimmedDics = dicFiles.Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).Distinct().ToArray();
						var trimmedAff = affFiles.Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).Distinct().ToArray();

						//Note - this might be an issue on Linux? Linux paths are case-sensitive?
						var affFilesOverlapping = trimmedAff.Where(x => trimmedDics.Any(y => y == x)).ToArray();
						m_Instance = new TextBoxSpellChecker(SpellCheckerConfig.Create(affFilesOverlapping));
					}
					else
						m_Instance = new TextBoxSpellChecker(SpellCheckerConfig.Create());
				}

				return m_Instance;
			}
		}
	}
}
