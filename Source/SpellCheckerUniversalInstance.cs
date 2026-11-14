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
						var dicFiles = Directory.GetFiles(directory, "*.dic", SearchOption.AllDirectories); //unfortunance extension
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
