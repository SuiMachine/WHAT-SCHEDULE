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
	private string overlayText = "WHAT SCHEDULE";

	public string OverlayText
	{
		get => overlayText;
		set => SetProperty(ref overlayText, value);
	}

	/// <summary>The document being edited; the canvas renders and modifies this.</summary>
	public Document Document { get; }

	/// <summary>Set by the main window so commands can open file pickers.</summary>
	public IStorageProvider? StorageProvider { get; set; }

	public MainWindowViewModel()
	{
		Document = CreateSampleDocument();
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
