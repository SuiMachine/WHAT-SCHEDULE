using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using WhatSchedule.Documents;
using WhatSchedule.Elements;
using WhatSchedule.Rendering;

namespace WhatSchedule.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
	private const string EXTENSION = "*.wschedule";
	private string overlayText = "WHAT SCHEDULE";
	private Document document = null!;

	public string OverlayText
	{
		get => overlayText;
		set => SetProperty(ref overlayText, value);
	}

	/// <summary>The document being edited; the canvas renders and modifies this.</summary>
	public Document Document
	{
		get => document;
		private set
		{
			if (document is not null)
				document.History.Changed -= RefreshHistoryCommands;
			SetProperty(ref document, value);
			document.History.Changed += RefreshHistoryCommands;
			RefreshHistoryCommands();
		}
	}

	/// <summary>Whether any changes are recorded in the history and would be lost by closing or replacing the document.</summary>
	public bool HasHistoryChanges => Document.History.CanUndo || Document.History.CanRedo;

	/// <summary>Set by the main window so commands can open file pickers.</summary>
	public IStorageProvider? StorageProvider { get; set; }

	public MainWindowViewModel()
	{
		Document = CreateSampleDocument();
	}

	[RelayCommand]
	private async Task NewScheduleAsync()
	{
		var provider = StorageProvider;
		if (provider is null)
			return;

		IReadOnlyList<IStorageFile>? file = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open background image",
			AllowMultiple = false,
			FileTypeFilter = [new FilePickerFileType("Image files") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }],
		});

		if (file is null || file.Count == 0 || file[0] is null)
			return;

		var bitmap = SKBitmap.Decode(await file[0].OpenReadAsync());
		if (bitmap is null)
			return;

		Document.PrepareNewWithBackground(bitmap);
	}

	[RelayCommand]
	private async Task LoadAsync()
	{
		var provider = StorageProvider;
		if (provider is null)
			return;

		IReadOnlyList<IStorageFile>? file = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open schedule document",
			AllowMultiple = false,
			FileTypeFilter = [new FilePickerFileType("WHAT Schedule document") { Patterns = [EXTENSION] }],
		});

		if (file is null || file.Count == 0 || file[0] is null)
			return;

		try
		{
			await using var stream = await file[0].OpenReadAsync();
			Document = DocumentSerializer.Load(stream);
		}
		catch
		{
			//An invalid or foreign file leaves the current document untouched.
		}
	}

	[RelayCommand]
	private async Task SaveAsync()
	{
		var provider = StorageProvider;
		if (provider is null)
			return;

		var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Save schedule document",
			SuggestedFileName = "schedule",
			DefaultExtension = "json",
			FileTypeChoices = [new FilePickerFileType("WHAT Schedule document") { Patterns = [EXTENSION] }],
		});
		if (file is null)
			return;

		await using var stream = await file.OpenWriteAsync();
		DocumentSerializer.Save(Document, stream);
	}

	[RelayCommand]
	private async Task ExportAsync()
	{
		var provider = StorageProvider;
		if (provider is null)
			return;

		var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Export PNG",
			SuggestedFileName = "schedule",
			DefaultExtension = "png",
			FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
		});
		if (file is null)
			return;

		var png = DocumentRenderer.EncodePng(Document);
		await using var stream = await file.OpenWriteAsync();
		await stream.WriteAsync(png);
	}

	private bool CanUndo() => Document.History.CanUndo;

	[RelayCommand]
	private void Undo() => Document.History.Undo(Document);

	private bool CanRedo() => Document.History.CanRedo;

	[RelayCommand]
	private void Redo() => Document.History.Redo(Document);

	/// <summary>The undo menu item's text, naming the change it will revert.</summary>
	public string UndoHeader
	{
		get => undoHeader;
		private set => SetProperty(ref undoHeader, value);
	}

	private string undoHeader = "Undo";

	/// <summary>The redo menu item's text, naming the change it will re-apply.</summary>
	public string RedoHeader
	{
		get => redoHeader;
		private set => SetProperty(ref redoHeader, value);
	}

	private string redoHeader = "Redo";

	/// <summary>Re-evaluates whether undo and redo can run after the history or the document itself changed,
	/// and re-names both commands after the change they would act on.</summary>
	private void RefreshHistoryCommands()
	{
		UndoCommand.NotifyCanExecuteChanged();
		RedoCommand.NotifyCanExecuteChanged();
		UndoHeader = Document.History.UndoDescription is { } undoDescription ? $"Undo {undoDescription}" : "Undo";
		RedoHeader = Document.History.RedoDescription is { } redoDescription ? $"Redo {redoDescription}" : "Redo";
	}

	private static Document CreateSampleDocument()
	{
		//I have created this by hand from Photoshop project
		//because I wrote the rules that no assets can be read directly by AI
		//- Sui
		var document = new Document { Background = LoadExampleInternalAsset("background.png") };
		document.Week.DateCulture = ScheduleDateCulture.EnUs;

		//Might not work on Linux? We'll see.
		(string Suffix, string Zone)[] defaultConversions =
		[
			("UTC", "UTC"),
			("KST", "Asia/Seoul"),
			("JST", "Asia/Tokyo"),
			("WIB", "Asia/Jakarta"),
			("MSK", "Europe/Moscow"),
			("CET", "Europe/Berlin"),
			("GMT", "Europe/London"),
			("ET", "America/New_York"),
			("CT", "America/Chicago"),
			("PT", "America/Los_Angeles"),
		];
		foreach (var (suffix, zone) in defaultConversions)
			document.Week.Conversions.Add(new ScheduleZoneConversion { Suffix = suffix, TimeZoneId = zone });

		document.Add(new ImageElement(LoadExampleInternalAsset("background_outline.png"), new SKPoint() { X = 0, Y = 0 })
		{
			Name = "Background outline",
			BlendMode = SKBlendMode.Difference,
			IsLocked = true
		});
		document.AddGuide(new Guide() { IsHorizontal = true, Position = 145 });
		document.AddGuide(new Guide() { IsHorizontal = true, Position = 540 });
		document.AddGuide(new Guide() { IsHorizontal = true, Position = 998 });
		document.AddGuide(new Guide() { IsHorizontal = false, Position = 730 });
		document.AddGuide(new Guide() { IsHorizontal = false, Position = 960 });
		document.AddGuide(new Guide() { IsHorizontal = false, Position = 1678 });
		document.AddGuide(new Guide() { IsHorizontal = false, Position = 1836 });

		var artSection = new GroupElement()
		{
			Document = document,
			Name = "Art section",
			IsLocked = true
		};
		artSection.InsertChild(new ImageElement(LoadExampleInternalAsset("art_outline.png"), new SKPoint() { X = -278, Y = -423 }) { IsLocked = true }, 0);
		artSection.Position = new SKPoint(350, 501);

		var userArtCrop = new GroupElement()
		{
			Document = document,
			Name = "User art crop",
			IsLocked = true,
			Mask = new List<SKPoint>()
			{
				new SKPoint(-297, -431), //left upper corner
				new SKPoint(-216, -431),
				new SKPoint(-194, -454),
				new SKPoint(194, -454),
				new SKPoint(216, -431),
				new SKPoint(297, -431),

				//Mirror through Y
				new SKPoint(297, 431),
				new SKPoint(216, 431),
				new SKPoint(194, 454),
				new SKPoint(-194, 454),
				new SKPoint(-216, 431),
				new SKPoint(-297, 431)
			}
		};
		userArtCrop.InsertChild(new ImageElement(LoadExampleInternalAsset("art_temporary.png"), new SKPoint() { X = -288, Y = -445 }) { Name = "User art" }, 0);
		userArtCrop.Position = new SKPoint(36, 36);
		artSection.InsertChild(userArtCrop, 0);
		document.Add(artSection);

		//But I got a bit clever here
		int startPositionX = 1270;
		int startPositionY = 148;

		int shuffleX = 28;
		int stepY = 131;
		int counter = 0;

		var dayOfWeekBackground = LoadExampleInternalAsset("element_bg.png");
		var dayOfWeekBackgroundOutline = LoadExampleInternalAsset("outline.png");
		var dayOfWeekDetailLeft = LoadExampleInternalAsset("active_day_left.png");
		var dayOfWeekDetailRight = LoadExampleInternalAsset("active_day_right.png");

		foreach (ScheduleDay dayOfTheWeek in Enum.GetValues<ScheduleDay>())
		{
			var dayGroup = new GroupElement()
			{
				Document = document,
				Name = dayOfTheWeek.ToString(),
			};

			dayGroup.InsertChild(new ImageElement(dayOfWeekBackgroundOutline, new SKPoint(-542, -59)) { Name = "Outline" }, 0);
			dayGroup.InsertChild(new ImageElement(dayOfWeekBackground, new SKPoint(-543, -59)) { BlendMode = SKBlendMode.Hue, Name = "Background" }, 0);
			dayGroup.InsertChild(new ImageElement(dayOfWeekDetailLeft, new SKPoint(-540, -55)) { Name = "DetailLeft" }, 2);
			dayGroup.InsertChild(new ImageElement(dayOfWeekDetailRight, new SKPoint(416, -55)) { Name = "DetailRight" }, 3);
			dayGroup.InsertChild(new LabelElement()
			{
				Text = $"{{{ScheduleVariables.DateName(dayOfTheWeek)}}}",
				Position = new SKPoint(-537, -54),
				Width = 124,
				Height = 112,
				Alignment = Rendering.Definitions.TextAlignment.Center,
				VerticalPlacement = Rendering.Definitions.TextVerticalPlacement.Middle,
				FontFamily = "Impact",
				FontSize = 38
			}, 4);

			dayGroup.InsertChild(new LabelElement()
			{
				Text = $"CET:",
				Position = new SKPoint(403, -60),
				Width = 124,
				Height = 34,
				Alignment = Rendering.Definitions.TextAlignment.Center,
				VerticalPlacement = Rendering.Definitions.TextVerticalPlacement.Middle,
				FontFamily = "Impact",
				FontSize = 24
			}, 4);
			dayGroup.InsertChild(new LabelElement()
			{
				Text = $"{{{ScheduleVariables.TimeName(dayOfTheWeek)}}}",
				Position = new SKPoint(423, -32),
				Width = 124,
				Height = 34,
				Alignment = Rendering.Definitions.TextAlignment.Center,
				VerticalPlacement = Rendering.Definitions.TextVerticalPlacement.Middle,
				FontFamily = "Impact",
				FontSize = 24
			}, 4);

			dayGroup.InsertChild(new LabelElement()
			{
				Text = $"WIB:",
				Position = new SKPoint(423, 0),
				Width = 124,
				Height = 34,
				Alignment = Rendering.Definitions.TextAlignment.Center,
				VerticalPlacement = Rendering.Definitions.TextVerticalPlacement.Middle,
				FontFamily = "Impact",
				FontSize = 24
			}, 4);
			dayGroup.InsertChild(new LabelElement()
			{
				Text = $"{{{ScheduleVariables.ConvertedTimeName(dayOfTheWeek, "WIB")}}}",
				Position = new SKPoint(403, 26),
				Width = 124,
				Height = 34,
				Alignment = Rendering.Definitions.TextAlignment.Center,
				VerticalPlacement = Rendering.Definitions.TextVerticalPlacement.Middle,
				FontFamily = "Impact",
				FontSize = 24
			}, 4);

			int dayOfWeekPosition = counter % 2 == 0 ? startPositionX : startPositionX + shuffleX;
			dayGroup.Position = new SKPoint(dayOfWeekPosition, startPositionY);
			document.Add(dayGroup);

			startPositionY += stepY;
			counter++;
		}

		return document;
	}

	private static SKBitmap LoadExampleInternalAsset(string asset)
	{
		using var stream = AssetLoader.Open(new Uri($"avares://WhatSchedule/Assets/Example/{asset}"));
		return SKBitmap.Decode(stream);
	}
}
