using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using WhatSchedule.Rendering.Definitions;

namespace WhatSchedule.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
	private string overlayText = "WHAT SCHEDULE";

	[ObservableProperty]
	private IImage? preview;

	public string OverlayText
	{
		get => overlayText;
		set => SetProperty(ref overlayText, value);
	}

	[RelayCommand]
	public void RenderPreview()
	{
		using var source = CreateSampleImage();
		using var rendered = ImageTextRenderer.Apply(source, new TextOverlayOptions
		{
			Text = OverlayText,
			FontSize = 56,
			TextColor = SKColors.White,
			OutlineColor = SKColors.Black,
			OutlineWidth = 3,
			ShadowColor = SKColors.Black.WithAlpha(160),
			ShadowBlur = 8,
			ShadowOffset = new SKPoint(4, 4),
			Position = new SKPoint(24, 72),
		});

		Preview = ToAvaloniaBitmap(rendered);
	}

	private static SKBitmap CreateSampleImage()
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

	private static Bitmap ToAvaloniaBitmap(SKBitmap source)
	{
		using var image = SKImage.FromBitmap(source);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		var stream = new MemoryStream(data.ToArray());
		return new Bitmap(stream);
	}
}
