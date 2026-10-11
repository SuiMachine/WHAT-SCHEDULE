using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
			Formatting = Formatting.Indented,
			Converters = { new ElementDataConverter() }
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

		private static CanvasElement.ElementData ToData(CanvasElement element) => element switch
		{
			LabelElement label => new LabelElement.LabelElementData
			{
				Position = ToData(label.Position),
				Text = label.Text,
				FontFamily = label.FontFamily,
				FontSize = label.FontSize,
				TextColor = ToData(label.TextColor),
				OutlineColor = ToData(label.OutlineColor),
				OutlineWidth = label.OutlineWidth,
				ShadowColor = ToData(label.ShadowColor),
				ShadowBlur = label.ShadowBlur,
				ShadowOffset = ToData(label.ShadowOffset)
			},
			ImageElement image => new ImageElement.ImageElementData
			{
				Position = ToData(image.Position),
				Png = EncodePng(image.Bitmap),
				Width = image.Width,
				Height = image.Height
			},
			_ => throw new NotSupportedException($"Cannot save {element.GetType().Name} elements.")
		};

		private static CanvasElement FromData(CanvasElement.ElementData data)
		{
			if (data.Position is not { } position)
				throw new JsonSerializationException("An element is missing its position.");

			switch (data)
			{
				case LabelElement.LabelElementData label:
					return new LabelElement
					{
						Position = ToSKPoint(position),
						Text = label.Text,
						FontFamily = label.FontFamily,
						FontSize = label.FontSize,
						TextColor = ToSKColor(label.TextColor, SKColors.White),
						OutlineColor = ToSKColor(label.OutlineColor, SKColors.Black),
						OutlineWidth = label.OutlineWidth,
						ShadowColor = ToSKColor(label.ShadowColor, SKColors.Black.WithAlpha(140)),
						ShadowBlur = label.ShadowBlur,
						ShadowOffset = label.ShadowOffset is { } shadowOffset ? ToSKPoint(shadowOffset) : new SKPoint(3f, 3f)
					};
				case ImageElement.ImageElementData image:
					{
						var bitmap = image.Png is null ? null : SKBitmap.Decode(Convert.FromBase64String(image.Png));
						if (bitmap is null)
							throw new JsonSerializationException("An image element could not be decoded.");

						return new ImageElement(bitmap, ToSKPoint(position))
						{
							Width = image.Width ?? bitmap.Width,
							Height = image.Height ?? bitmap.Height
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

		private static CanvasElement.PointData ToData(SKPoint point) => new() { X = point.X, Y = point.Y };

		private static SKPoint ToSKPoint(CanvasElement.PointData point) => new(point.X, point.Y);

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

		/// <summary>
		/// Reads an element's JSON as the storage class its kind names; writing needs no custom
		/// logic, because elements are serialized by their runtime type, kind included.
		/// </summary>
		private sealed class ElementDataConverter : JsonConverter
		{
			public override bool CanConvert(Type objectType) => objectType == typeof(CanvasElement.ElementData);

			public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
			{
				var element = JObject.Load(reader);
				var kind = element[nameof(CanvasElement.ElementData.Kind)]?.ToString();

				CanvasElement.ElementData data = kind switch
				{
					LabelElement.LabelElementData.KindName => new LabelElement.LabelElementData(),
					ImageElement.ImageElementData.KindName => new ImageElement.ImageElementData(),
					_ => throw new JsonSerializationException($"Unknown element kind '{kind}'.")
				};

				using var elementReader = element.CreateReader();
				serializer.Populate(elementReader, data);
				return data;
			}

			public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
				throw new NotSupportedException("The converter only reads; elements are written by their runtime type.");
		}
	}

	public sealed class DocumentData
	{
		public int Version { get; set; } = 1;

		public string? Background { get; set; } //Saved as base64, but we'll add an option to load a new background

		public List<Guide> Guides { get; set; } = new();

		public List<CanvasElement.ElementData> Elements { get; set; } = new();
	}
}
