// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using SkiaSharp;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Rendering;

/// <summary>
/// Composites a document (background plus elements) with SkiaSharp,
/// so the on-screen preview and the exported PNG are rendered identically.
/// </summary>
public static class DocumentRenderer
{
	/// <summary>Renders the document into a new BGRA bitmap.</summary>
	public static SKBitmap Render(Document document)
	{
		ArgumentNullException.ThrowIfNull(document);
		if (document.Width <= 0 || document.Height <= 0)
			throw new InvalidOperationException("The document has no background image.");

		var bitmap = new SKBitmap(document.Width, document.Height, false);
		using (var canvas = new SKCanvas(bitmap))
		{
			Render(document, canvas);
		}

		return bitmap;
	}

	/// <summary>Renders the document onto an existing canvas, clearing it first. The display <paramref name="filter"/> is a concern of the canvas preview; exports pass null to render everything.</summary>
	public static void Render(Document document, SKCanvas canvas, TagFilter? filter = null)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(canvas);

		canvas.Clear(SKColors.Transparent);

		if (document.Background is not { } background)
			return;

		canvas.DrawBitmap(background, 0, 0);
		//Groups draw their own children inside their transform, so only the roots are drawn here.
		foreach (var element in document.RootElements)
		{
			if (element.IsDisplayed(filter))
				element.Draw(canvas, filter);
		}
	}

	/// <summary>Encodes the rendered document as a PNG image.</summary>
	public static byte[] EncodePng(Document document)
	{
		using var rendered = Render(document);
		using var image = SKImage.FromBitmap(rendered);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}
}
