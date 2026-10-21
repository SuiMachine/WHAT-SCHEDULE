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
		if(bitmap is null)
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

	private Document CreateSampleDocument()
	{
		var document = new Document { Background = CreateSampleBackground() };
		document.Add(new LabelElement
		{
			Text = OverlayText,
			FontSize = 56f,
			OutlineWidth = 3f,
			ShadowColor = SKColors.Black.WithAlpha(160),
			ShadowBlur = 8f,
			ShadowOffset = new SKPoint(4f, 4f),
			Position = new SKPoint(24f, 24f),
		});
		return document;
	}

	private static SKBitmap CreateSampleBackground()
	{
		const int width = 1920;
		const int height = 1080;

		var bitmap = new SKBitmap(width, height);
		using var canvas = new SKCanvas(bitmap);
		using var paint = new SKPaint
		{
			Shader = SKShader.CreateLinearGradient(
				new SKPoint(0, 0),
				new SKPoint(width, height),
				new[] { new SKColor(0x1E, 0x3A, 0x8A), new SKColor(0x9A, 0x3D, 0x8F) },
				null,
				SKShaderTileMode.Clamp),
		};
		canvas.DrawRect(0, 0, width, height, paint);
		return bitmap;
	}
}
