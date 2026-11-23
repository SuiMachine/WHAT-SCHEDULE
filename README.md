# WHAT SCHEDULE ?!
A free multi-platform program for handling stream schedule and generating PNGs for for them written in C#. This application uses [Avalonia UI](https://avaloniaui.net/), with image processing handled by [SkiaSharp](https://github.com/mono/SkiaSharp).

This application targets Windows and Linux.

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

## Libraries used
- [Avalonia (UI)](https://github.com/avaloniaui/avalonia)
- [Avalonia Spell Checker](https://github.com/GustavoHennig/Avalonia.SpellChecker)
- [SkiaSharp](https://github.com/mono/skiasharp)

## References used
Simplified PSD importer was written based on:
- [Adobe Photoshop File Formats Specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) - the format the importer reads.
- [ag-psd](https://github.com/Agamnentzar/ag-psd)
No direct code was used from either, but both helped to build a simplified importer.
