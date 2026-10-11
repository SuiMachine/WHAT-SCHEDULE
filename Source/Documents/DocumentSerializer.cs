using System.Globalization;
using Newtonsoft.Json;
using SkiaSharp;
using WhatSchedule.Elements;

namespace WhatSchedule.Documents
{
	/// <summary>
	/// Saves and loads a document as JSON: the background image, the guide lines
	/// and every element, with bitmaps embedded as base64-encoded PNG.
	/// </summary>
	public static class DocumentSerializer
	{
		private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			Culture = CultureInfo.InvariantCulture,
			Formatting = Formatting.Indented
		});

		public static Document Load(Stream stream)
		{
			ArgumentNullException.ThrowIfNull(stream);

			using var reader = new StreamReader(stream, leaveOpen: true);
			using var jsonReader = new JsonTextReader(reader);
			var data = Serializer.Deserialize<DocumentData>(jsonReader)
				?? throw new JsonSerializationException("The file does not contain a schedule document.");
			return FromData(data);
		}

		public static void Save(Document document, Stream stream)
		{
			ArgumentNullException.ThrowIfNull(document);
			ArgumentNullException.ThrowIfNull(stream);

			DocumentData data = ToData(document);
			using StreamWriter writer = new(stream, leaveOpen: true);
			Serializer.Serialize(writer, data);
		}

		private static DocumentData ToData(Document document) => new()
		{
			Background = EncodePng(document.Background),
			Guides = new List<Guide>(document.Guides),
			Elements = document.Elements.Select(ToData).ToList(),
		};

		private static Document FromData(DocumentData data)
		{
			var background = data.Background is null ? null : SKBitmap.Decode(Convert.FromBase64String(data.Background));
			if (background is null)
				throw new JsonSerializationException("The document background could not be decoded.");

			var document = new Document { Background = background };
			foreach (var guide in data.Guides)
				document.AddGuide(guide);
			foreach (var element in data.Elements)
				document.Add(FromData(element));
			return document;
		}

		private static ElementData ToData(CanvasElement element) => element switch
		{
			LabelElement label => new ElementData
			{
				Kind = ElementData.LabelKind,
				Position = ToData(label.Position),
				Text = label.Text,
				FontFamily = label.FontFamily,
				FontSize = label.FontSize,
				TextColor = ToData(label.TextColor),
				OutlineColor = ToData(label.OutlineColor),
				OutlineWidth = label.OutlineWidth,
				ShadowColor = ToData(label.ShadowColor),
				ShadowBlur = label.ShadowBlur,
				ShadowOffset = ToData(label.ShadowOffset),
			},
			ImageElement image => new ElementData
			{
				Kind = ElementData.ImageKind,
				Position = ToData(image.Position),
				Png = EncodePng(image.Bitmap),
				Width = image.Width,
				Height = image.Height,
			},
			_ => throw new NotSupportedException($"Cannot save {element.GetType().Name} elements."),
		};

		private static CanvasElement FromData(ElementData data)
		{
			if (data.Position is not { } position)
				throw new JsonSerializationException("An element is missing its position.");

			switch (data.Kind)
			{
				case ElementData.LabelKind:
					return new LabelElement
					{
						Position = ToSKPoint(position),
						FontFamily = data.FontFamily ?? "Arial",
						Text = data.Text ?? "Label",
						FontSize = data.FontSize ?? 48f,
						TextColor = ToSKColor(data.TextColor, SKColors.White),
						OutlineColor = ToSKColor(data.OutlineColor, SKColors.Black),
						OutlineWidth = data.OutlineWidth ?? 2f,
						ShadowColor = ToSKColor(data.ShadowColor, SKColors.Black.WithAlpha(140)),
						ShadowBlur = data.ShadowBlur ?? 6f,
						ShadowOffset = data.ShadowOffset is { } shadowOffset ? ToSKPoint(shadowOffset) : new SKPoint(3f, 3f)
					};
				case ElementData.ImageKind:
					{
						var bitmap = data.Png is null ? null : SKBitmap.Decode(Convert.FromBase64String(data.Png));
						if (bitmap is null)
							throw new JsonSerializationException("An image element could not be decoded.");

						return new ImageElement(bitmap, ToSKPoint(position))
						{
							Width = data.Width ?? bitmap.Width,
							Height = data.Height ?? bitmap.Height,
						};
					}
				default:
					throw new JsonSerializationException($"Unknown element kind '{data.Kind}'.");
			}
		}

		private static string EncodePng(SKBitmap? bitmap)
		{
			if (bitmap is null)
				return string.Empty;

			using var image = SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);
			return Convert.ToBase64String(data.ToArray());
		}

		private static PointData ToData(SKPoint point) => new() { X = point.X, Y = point.Y };

		private static SKPoint ToSKPoint(PointData point) => new(point.X, point.Y);

		private static string ToData(SKColor color)
		{
			var packed = (uint)(color.Red << 24 | color.Green << 16 | color.Blue << 8 | color.Alpha);
			return "#" + packed.ToString("X8", CultureInfo.InvariantCulture);
		}

		private static SKColor ToSKColor(string? value, SKColor defaultValue)
		{
			if (value is null)
				return defaultValue;

			if (value.Length != 9 || value[0] != '#' || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
				throw new JsonSerializationException($"Invalid color value '{value}'.");

			return new SKColor(
				(byte)(packed >> 24 & 0xFF),
				(byte)(packed >> 16 & 0xFF),
				(byte)(packed >> 8 & 0xFF),
				(byte)(packed & 0xFF));
		}
	}

	public sealed class DocumentData
	{
		public int Version { get; set; } = 1;

		public string? Background { get; set; } //Saved as base64, but we'll add an option to load a new background

		public List<Guide> Guides { get; set; } = new();

		public List<ElementData> Elements { get; set; } = new();
	}

	/// <summary>
	/// JSON shape of a canvas element; <see cref="Kind"/> picks which of the
	/// label or image fields apply.
	/// </summary>
	public sealed class ElementData
	{
		public const string LabelKind = "Label";
		public const string ImageKind = "Image";

		public string Kind { get; set; } = "";

		public PointData? Position { get; set; }

		public string? FontFamily { get; set; }
		// Label
		public string? Text { get; set; }

		public float? FontSize { get; set; }

		public string? TextColor { get; set; }

		public string? OutlineColor { get; set; }

		public float? OutlineWidth { get; set; }

		public string? ShadowColor { get; set; }

		public float? ShadowBlur { get; set; }

		public PointData? ShadowOffset { get; set; }

		// Image
		/// <summary>The element's image as a base64-encoded PNG.</summary>
		public string? Png { get; set; }

		public float? Width { get; set; }

		public float? Height { get; set; }
	}

	/// <summary>JSON shape of a document-pixel coordinate pair.</summary>
	public sealed class PointData
	{
		public float X { get; set; }

		public float Y { get; set; }
	}
}
