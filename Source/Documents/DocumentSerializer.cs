using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using WhatSchedule.Elements;
using WhatSchedule.Rendering.Definitions;

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
			Elements = document.RootElements.Select(ToData).ToList(),

			//The week and the custom variables complete the picture: without them every label referencing
			//the schedule's variables would render empty once the file loads back.
			Week = ToData(document.Week),
			//Nothing is written when the document defines none, so old files keep their small shape.
			Variables = document.Variables.Count > 0 ? new Dictionary<string, string>(document.Variables) : null,
		};

		private static Document FromData(DocumentData data)
		{
			var background = data.Background is null ? null : SKBitmap.Decode(Convert.FromBase64String(data.Background));
			if (background is null)
				throw new JsonSerializationException("The document background could not be decoded.");

			var document = new Document { Background = background };
			foreach (var guide in data.Guides)
				document.AddGuide(guide);

			//Files saved before the week was stored keep their freshly initialized week, untouched.
			FromData(document.Week, data.Week);
			if (data.Variables is { } variables)
			{
				foreach (var pair in variables)
					document.Variables[pair.Key] = pair.Value;
			}

			foreach (var element in data.Elements)
				document.Add(FromData(element));
			return document;
		}

		/// <summary>The week's JSON shape: fields absent (saved by an older version) keep the week's in-memory default.</summary>
		private static WeekData ToData(ScheduleWeek week) => new()
		{
			WeekStart = week.WeekStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			TimeFormat = week.TimeFormat.ToString(),
			DateCulture = week.DateCulture.ToString(),
			TimeZoneId = week.TimeZoneId,
			Conversions = [.. week.Conversions.Select(ToData)],
			Days = [.. week.Days.Select(ToData)],
		};

		private static ConversionData ToData(ScheduleZoneConversion conversion) => new()
		{
			Suffix = conversion.Suffix,
			TimeZoneId = conversion.TimeZoneId,
		};

		private static DayEntryData ToData(ScheduleDayEntry entry) => new()
		{
			StreamTitle = entry.StreamTitle,
			//Streaming is the exception, not the rule: days that do not stream stay unwritten.
			IsStreaming = entry.IsStreaming ? true : null,
			StreamTime = entry.StreamTime is { } time ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : null,
		};

		/// <summary>Restores whatever of the week the file carries onto <paramref name="week"/>; the file's nulls
		/// (fields saved by an older version omitted) leave the corresponding week setting at its default.</summary>
		private static void FromData(ScheduleWeek week, WeekData? data)
		{
			if (data is null)
				return;

			if (data.WeekStart is { } startText && DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
				week.WeekStart = start;
			if (Enum.TryParse(data.TimeFormat, out ScheduleTimeFormat timeFormat))
				week.TimeFormat = timeFormat;
			if (Enum.TryParse(data.DateCulture, out ScheduleDateCulture dateCulture))
				week.DateCulture = dateCulture;
			if (data.TimeZoneId is { } zone)
				week.TimeZoneId = zone;

			week.Conversions = [.. data.Conversions?.Select(FromData) ?? []];
			if (data.Days is { } days)
			{
				//The week holds one entry per day; a file holding fewer or more leaves the rest at their defaults.
				for (var i = 0; i < week.Days.Length && i < days.Count; i++)
				{
					if (days[i].StreamTitle is { } title)
						week.Days[i].StreamTitle = title;
					week.Days[i].IsStreaming = days[i].IsStreaming == true;
					if (days[i].StreamTime is { } timeText && TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
						week.Days[i].StreamTime = time;
				}
			}
		}

		private static ScheduleZoneConversion FromData(ConversionData data) => new()
		{
			Suffix = data.Suffix ?? string.Empty,
			TimeZoneId = data.TimeZoneId,
		};

		private static CanvasElement.ElementData ToData(CanvasElement element)
		{
			CanvasElement.ElementData data = element switch
			{
				GroupElement group => new GroupElement.GroupElementData
				{
					Name = element.Name,
					Position = ToData(group.Position),
					Rotation = group.Rotation,
					ScaleX = group.ScaleX,
					ScaleY = group.ScaleY,
					//The mask is only written when the group carries one, so files saved by older versions load and save unchanged.
					Mask = group.Mask?.Select(vertex => ToData(vertex)).ToList(),
					Children = group.Children.Select(ToData).ToList()
				},
				LabelElement label => new LabelElement.LabelElementData
				{
					Name = element.Name,
					Position = ToData(label.Position),
					Rotation = label.Rotation,
					Text = label.Text,
					FontFamily = label.FontFamily,
					FontWeight = label.FontStyle.Weight,
					FontSlant = (int)label.FontStyle.Slant,
					FontSize = label.FontSize,
					LetterSpacing = label.LetterSpacing,
					Stretch = label.Stretch,
					Skew = label.Skew,
					LineHeight = label.LineHeight,
					Alignment = label.Alignment.ToString(),
					VerticalPlacement = label.VerticalPlacement.ToString(),
					Width = label.Width,
					Height = label.Height,
					Transform = label.Transform.ToString(),
					TextColor = ToData(label.TextColor),
					Opacity = label.Opacity,
					GradientFill = label.GradientFill,
					GradientColor = ToData(label.GradientColor),
					GradientAngle = label.GradientAngle,
					OutlineColor = ToData(label.OutlineColor),
					OutlineWidth = label.OutlineWidth,
					OutlinePosition = label.OutlinePosition.ToString(),
					SecondOutlineColor = ToData(label.SecondOutlineColor),
					SecondOutlineWidth = label.SecondOutlineWidth,
					ShadowColor = ToData(label.ShadowColor),
					ShadowBlur = label.ShadowBlur,
					ShadowOffset = ToData(label.ShadowOffset),
					ShadowBlendMode = label.ShadowBlendMode.ToString(),
					GlowColor = ToData(label.GlowColor),
					GlowBlur = label.GlowBlur,
					Blur = label.Blur,
					BlendMode = label.BlendMode.ToString(),
					FlipX = label.FlipX,
					FlipY = label.FlipY
				},
				ImageElement image => new ImageElement.ImageElementData
				{
					Name = element.Name,
					Position = ToData(image.Position),
					Rotation = image.Rotation,
					Png = EncodePng(image.Bitmap),
					Width = image.Width,
					Height = image.Height,
					CropLeft = image.Crop.Left,
					CropTop = image.Crop.Top,
					CropRight = image.Bitmap.Width - image.Crop.Right,
					CropBottom = image.Bitmap.Height - image.Crop.Bottom,
					Opacity = image.Opacity,
					CornerRadius = image.CornerRadius,
					FlipX = image.FlipX,
					FlipY = image.FlipY,
					ShadowColor = ToData(image.ShadowColor),
					ShadowBlur = image.ShadowBlur,
					ShadowOffset = ToData(image.ShadowOffset),
					TintColor = ToData(image.TintColor),
					TintMode = image.TintMode.ToString(),
					BlendMode = image.BlendMode.ToString()
				},
				_ => throw new NotSupportedException($"Cannot save {element.GetType().Name} elements.")
			};

			//The tag is only written when the element carries one, so files saved before tags existed load and save unchanged.
			if (element.Tag is { } tag)
				data.Tags = [tag.SaveName];

			//The lock is only written when the element carries it, so files saved before locks existed load and save unchanged.
			if (element.IsLocked)
				data.IsLocked = true;

			return data;
		}

		private static CanvasElement FromData(CanvasElement.ElementData data)
		{
			var position = data.Position;
			CanvasElement element;
			switch (data)
			{
				case GroupElement.GroupElementData group:
					{
						var restored = new GroupElement
						{
							Position = ToSKPoint(position),
							ScaleX = group.ScaleX,
							ScaleY = group.ScaleY
						};
						//An empty vertex list would clip nothing forever and save forever, so it loads as no mask at all.
						restored.Mask = group.Mask is { Count: > 0 } mask ? mask.Select(vertex => ToSKPoint(vertex)).ToList() : null;
						foreach (var child in group.Children)
							restored.InsertChild(FromData(child), restored.Children.Count);
						element = restored;
						break;
					}
				case LabelElement.LabelElementData label:
					{
						element = new LabelElement
						{
							Position = ToSKPoint(position),
							Text = label.Text,
							FontFamily = label.FontFamily,
							//Skia's width has no named constant for normal, and the renderer only ever needs the weight and slant.
							FontStyle = new SKFontStyle(label.FontWeight ?? 400, 5, (SKFontStyleSlant)(label.FontSlant ?? 0)),
							FontSize = label.FontSize,
							LetterSpacing = label.LetterSpacing ?? 0f,
							Stretch = label.Stretch ?? 100f,
							Skew = label.Skew ?? 0f,
							LineHeight = label.LineHeight ?? 1f,
							Alignment = Enum.TryParse(label.Alignment, out TextAlignment alignment) ? alignment : TextAlignment.Left,
							VerticalPlacement = Enum.TryParse(label.VerticalPlacement, out TextVerticalPlacement verticalPlacement) ? verticalPlacement : TextVerticalPlacement.Top,
							Width = label.Width,
							Height = label.Height,
							Transform = Enum.TryParse(label.Transform, out TextTransform transform) ? transform : TextTransform.None,
							TextColor = ToSKColor(label.TextColor, SKColors.White),
							Opacity = label.Opacity ?? 1f,
							GradientFill = label.GradientFill ?? false,
							GradientColor = ToSKColor(label.GradientColor, SKColors.White),
							GradientAngle = label.GradientAngle ?? 0f,
							OutlineColor = ToSKColor(label.OutlineColor, SKColors.Black),
							OutlineWidth = label.OutlineWidth,
							OutlinePosition = Enum.TryParse(label.OutlinePosition, out OutlinePosition outlinePosition) ? outlinePosition : OutlinePosition.Center,
							SecondOutlineColor = ToSKColor(label.SecondOutlineColor, SKColors.White),
							SecondOutlineWidth = label.SecondOutlineWidth ?? 0f,
							ShadowColor = ToSKColor(label.ShadowColor, SKColors.Black.WithAlpha(140)),
							ShadowBlur = label.ShadowBlur,
							ShadowOffset = label.ShadowOffset is { } shadowOffset ? ToSKPoint(shadowOffset) : new SKPoint(3f, 3f),
							ShadowBlendMode = Enum.TryParse(label.ShadowBlendMode, out SKBlendMode shadowBlendMode) ? shadowBlendMode : SKBlendMode.SrcOver,
							GlowColor = ToSKColor(label.GlowColor, SKColors.Transparent),
							GlowBlur = label.GlowBlur ?? 0f,
							Blur = label.Blur ?? 0f,
							BlendMode = Enum.TryParse(label.BlendMode, out SKBlendMode blendMode) ? blendMode : SKBlendMode.SrcOver,
							FlipX = label.FlipX ?? false,
							FlipY = label.FlipY ?? false
						};
						break;
					}
				case ImageElement.ImageElementData image:
					{
						var bitmap = image.Png is null ? null : SKBitmap.Decode(Convert.FromBase64String(image.Png));
						if (bitmap is null)
							throw new JsonSerializationException("An image element could not be decoded.");

						var restored = new ImageElement(bitmap, ToSKPoint(position))
						{
							Width = image.Width ?? bitmap.Width,
							Height = image.Height ?? bitmap.Height,
							Opacity = image.Opacity ?? 1f,
							CornerRadius = image.CornerRadius ?? 0f,
							FlipX = image.FlipX ?? false,
							FlipY = image.FlipY ?? false,
							ShadowColor = ToSKColor(image.ShadowColor, SKColor.Empty),
							ShadowBlur = image.ShadowBlur ?? 6f,
							ShadowOffset = image.ShadowOffset is { } imageShadowOffset ? ToSKPoint(imageShadowOffset) : new SKPoint(3f, 3f),
							TintColor = ToSKColor(image.TintColor, SKColor.Empty),
							TintMode = Enum.TryParse(image.TintMode, out SKBlendMode tintMode) ? tintMode : SKBlendMode.SrcIn,
							BlendMode = Enum.TryParse(image.BlendMode, out SKBlendMode imageBlendMode) ? imageBlendMode : SKBlendMode.SrcOver
						};
						restored.Crop = SKRect.Create(image.CropLeft, image.CropTop,
							bitmap.Width - image.CropLeft - image.CropRight, bitmap.Height - image.CropTop - image.CropBottom);
						element = restored;
						break;
					}
				default:
					throw new JsonSerializationException($"Unknown element kind '{data.Kind}'.");
			}

			element.Rotation = data.Rotation;
			element.Name = data.Name;
			//Absence means unlocked, the state every element had before locks existed.
			element.IsLocked = data.IsLocked is true;

			//Files saved by older versions could carry several tags; the first valid one becomes the element's single tag.
			//Unknown tag names (saved by a newer version) are skipped instead of failing the whole document.
			if (data.Tags is { } saved)
			{
				foreach (var name in saved)
				{
					if (ScheduleTag.TryParse(name) is { } tag)
					{
						element.Tag = tag;
						break;
					}
				}
			}

			return element;
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
					GroupElement.GroupElementData.KindName => new GroupElement.GroupElementData(),
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

		/// <summary>The week the schedule covers and displays its dates and times by; null in files saved before the week was stored.</summary>
		public WeekData? Week { get; set; }

		/// <summary>The document's custom variables by the names the labels reference them with; null when the document defines none.</summary>
		public Dictionary<string, string>? Variables { get; set; }
	}

	/// <summary>JSON shape of the schedule's week: the settings its variables display by, the zone conversions
	/// it offers and its seven day entries. Fields are nullable so files saved by older versions load unchanged.</summary>
	public sealed class WeekData
	{
		/// <summary>The Monday the week starts on, as yyyy-MM-dd; null keeps the current week the fresh week starts with.</summary>
		public string? WeekStart { get; set; }

		/// <summary>The format the stream start times display by, "Hour24" or "Hour12"; null keeps the default 24-hour one.</summary>
		public string? TimeFormat { get; set; }

		/// <summary>The culture the dates display by, "Local" or "EnUs"; null keeps the system's culture.</summary>
		public string? DateCulture { get; set; }

		/// <summary>The zone the stream start times belong to, as a portable (IANA) ID; null means the system's local zone.</summary>
		public string? TimeZoneId { get; set; }

		/// <summary>The named conversions of the stream start times into other time zones.</summary>
		public List<ConversionData>? Conversions { get; set; }

		/// <summary>The week's day entries, Monday first.</summary>
		public List<DayEntryData>? Days { get; set; }
	}

	/// <summary>JSON shape of one of the week's named zone conversions.</summary>
	public sealed class ConversionData
	{
		/// <summary>The suffix the conversion's variable names carry, e.g. "ET" in "Monday_Time_ET".</summary>
		public string? Suffix { get; set; }

		/// <summary>The zone the times convert into, as a portable (IANA) ID; null converts into the system's local zone.</summary>
		public string? TimeZoneId { get; set; }
	}

	/// <summary>JSON shape of the stream plan for one day of the week.</summary>
	public sealed class DayEntryData
	{
		/// <summary>The day's stream title; null keeps the day's default placeholder title.</summary>
		public string? StreamTitle { get; set; }

		/// <summary>Whether the streamer streams that day; null (or false) means the day has no stream.</summary>
		public bool? IsStreaming { get; set; }

		/// <summary>The day's stream start time as HH:mm; null keeps the default of 17:30.</summary>
		public string? StreamTime { get; set; }
	}
}
