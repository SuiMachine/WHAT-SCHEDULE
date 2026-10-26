using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
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

	/// <summary>Re-renders the document into the bitmap if anything changed since the last render. The activity filter hides elements whose tag names another activity.</summary>
	public void Render(Document document, ScheduleActivity? filter)
	{
		if (!dirty || bitmap is null)
			return;

		using var framebuffer = bitmap.Lock();
		var info = new SKImageInfo(document.Width, document.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
		DocumentRenderer.Render(document, surface.Canvas, filter);
		dirty = false;
	}

	public void Dispose()
	{
		bitmap?.Dispose();
		bitmap = null;
		dirty = true;
		GC.SuppressFinalize(this);
	}
}
