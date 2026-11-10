using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System.Runtime.InteropServices;
using WhatSchedule.Documents;
using WhatSchedule.Elements;
using WhatSchedule.Rendering;

namespace WhatSchedule.Controls;

/// <summary>
/// The preview bitmap the rendered document is composited into, kept in sync
/// with the document through a dirty flag so renders only happen when needed.
/// </summary>
internal sealed class PreviewSurface : IDisposable
{
	private WriteableBitmap? bitmap;
	private bool dirty = true;

	/// <summary>The preview bitmap; recreated by <see cref="EnsureSize"/> after being disposed.</summary>
	public WriteableBitmap? Bitmap => bitmap;

	public void MarkDirty() => dirty = true;

	/// <summary>Recreates the bitmap when the document size changed. Returns true when a new bitmap was created.</summary>
	public bool EnsureSize(Document document)
	{
		if (bitmap is not null && bitmap.PixelSize.Width == document.Width && bitmap.PixelSize.Height == document.Height)
			return false;

		bitmap?.Dispose();
		bitmap = new WriteableBitmap(
			new PixelSize(document.Width, document.Height),
			new Vector(96, 96),
			PixelFormat.Bgra8888,
			AlphaFormat.Premul);
		dirty = true;
		return true;
	}

	/// <summary>Re-renders the document into the bitmap if anything changed since the last render. The display filter keeps the tagged elements it hides out of the bitmap.</summary>
	public void Render(Document document, TagFilter? filter)
	{
		if (!dirty || bitmap is null)
			return;

		using var framebuffer = bitmap.Lock();
		var info = new SKImageInfo(document.Width, document.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
		DocumentRenderer.Render(document, surface.Canvas, filter);
		dirty = false;
	}

	/// <summary>The color of the rendered pixel at <paramref name="documentPoint"/>, rounded and clamped
	/// into the bitmap, in straight alpha; null when there is no bitmap to sample.</summary>
	public SKColor? SamplePixel(SKPoint documentPoint)
	{
		if (bitmap is null)
			return null;

		var x = Math.Clamp((int)Math.Round(documentPoint.X), 0, bitmap.PixelSize.Width - 1);
		var y = Math.Clamp((int)Math.Round(documentPoint.Y), 0, bitmap.PixelSize.Height - 1);

		//The render that filled the bitmap ran on this same thread, before the click that samples.
		using var framebuffer = bitmap.Lock();
		var pixel = new byte[4];
		Marshal.Copy(framebuffer.Address + y * framebuffer.RowBytes + x * 4, pixel, 0, 4);

		//The buffer holds premultiplied BGRA; the color to edit wants a straight-alpha color.
		var alpha = pixel[3];
		if (alpha == 0)
			return SKColor.Empty;

		return new SKColor(
			(byte)Math.Min(byte.MaxValue, pixel[2] * 255 / alpha),
			(byte)Math.Min(byte.MaxValue, pixel[1] * 255 / alpha),
			(byte)Math.Min(byte.MaxValue, pixel[0] * 255 / alpha),
			byte.MaxValue);
	}

	/// <summary>A flat copy of the rendered pixels in the surface's BGRA-premultiplied layout, rows tight;
	/// null when there is no bitmap. Sampling freezes the copy, which the magnifier then reads.</summary>
	public (byte[] Pixels, int Width, int Height)? SnapshotPixels()
	{
		if (bitmap is null)
			return null;

		using var framebuffer = bitmap.Lock();
		var width = bitmap.PixelSize.Width;
		var height = bitmap.PixelSize.Height;
		var pixels = new byte[width * height * 4];
		var stride = framebuffer.RowBytes;
		if (stride == width * 4)
		{
			Marshal.Copy(framebuffer.Address, pixels, 0, pixels.Length);
		}
		else
		{
			//Row padding never belongs to the image, so the copy drops it row by row.
			for (var y = 0; y < height; y++)
				Marshal.Copy(framebuffer.Address + y * stride, pixels, y * width * 4, width * 4);
		}

		return (pixels, width, height);
	}

	public void Dispose()
	{
		bitmap?.Dispose();
		bitmap = null;
		dirty = true;
		GC.SuppressFinalize(this);
	}
}
