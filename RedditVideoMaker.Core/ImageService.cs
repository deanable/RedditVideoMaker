// ImageService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization; // For CultureInfo in GetAdjustedFont if needed

namespace RedditVideoMaker.Core
{
    public class ImageService
    {
        private readonly VideoOptions _videoOptions;
        private readonly FontCollection _fontCollection;

        /// <summary>
        /// Gets the FontFamily that was successfully loaded by the ImageService.
        /// This can be used by other parts of the application (like Program.cs for TextUtilities)
        /// to ensure consistent font metrics for text measurement.
        /// Will be null if no font could be loaded.
        /// </summary>
        public FontFamily? LoadedFontFamily { get; private set; }

        public ImageService(IOptions<VideoOptions> videoOptions)
        {
            _videoOptions = videoOptions.Value;
            _fontCollection = new FontCollection();
            LoadConfiguredFont();
        }

        private void LoadConfiguredFont()
        {
            // Attempt 1: Load primary font from configured file path
            if (!string.IsNullOrWhiteSpace(_videoOptions.PrimaryFontFilePath))
            {
                string fontFilePath = Path.Combine(AppContext.BaseDirectory, _videoOptions.PrimaryFontFilePath);

                if (File.Exists(fontFilePath))
                {
                    try
                    {
                        var tempCollection = new FontCollection();
                        FontFamily loadedFromFile = tempCollection.Add(fontFilePath);
                        if (!string.IsNullOrEmpty(loadedFromFile.Name)) // Ensure it's a valid font family
                        {
                            LoadedFontFamily = loadedFromFile;
                            _fontCollection.Add(fontFilePath); // Add to the service's main collection
                            // Corrected: Access .Name via .Value after confirming LoadedFontFamily is not null
                            Console.WriteLine($"ImageService: Successfully loaded primary font '{LoadedFontFamily.Value.Name}' from '{fontFilePath}'.");
                            return;
                        }
                        else
                        {
                            Console.Error.WriteLine($"ImageService Warning: Loaded font from '{fontFilePath}' but it has no name (invalid font family).");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"ImageService Warning: Failed to load primary font from '{fontFilePath}'. {ex.Message}");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"ImageService Warning: Primary font file not found at '{fontFilePath}'. Path configured: '{_videoOptions.PrimaryFontFilePath}'");
                }
            }
            else
            {
                Console.WriteLine("ImageService: No PrimaryFontFilePath configured. Attempting fallbacks.");
            }

            // Attempt 2: Load fallback font name from system fonts
            if (!string.IsNullOrWhiteSpace(_videoOptions.FallbackFontName))
            {
                if (SystemFonts.TryGet(_videoOptions.FallbackFontName, out FontFamily fallbackFontFamily) && !string.IsNullOrEmpty(fallbackFontFamily.Name))
                {
                    LoadedFontFamily = fallbackFontFamily;
                    Console.WriteLine($"ImageService: Using configured fallback system font '{LoadedFontFamily.Value.Name}'.");
                    return;
                }
                else
                {
                    Console.Error.WriteLine($"ImageService Warning: Configured fallback font '{_videoOptions.FallbackFontName}' not found on system or is invalid.");
                }
            }

            // Attempt 3: Default to first available system font
            if (SystemFonts.Families.Any())
            {
                FontFamily firstSystemFont = SystemFonts.Families.First();
                if (!string.IsNullOrEmpty(firstSystemFont.Name))
                {
                    LoadedFontFamily = firstSystemFont;
                    Console.WriteLine($"ImageService: Using first available system font '{LoadedFontFamily.Value.Name}' as a last resort.");
                    return;
                }
            }

            Console.Error.WriteLine("ImageService CRITICAL: No fonts could be loaded (primary, fallback, or system default). Text rendering will likely fail.");
            LoadedFontFamily = null;
        }

        public async Task<bool> CreateRedditContentCardAsync(
            string mainText,
            string? author,
            int? score,
            string outputImagePath,
            int cardWidth,
            int cardHeight,
            string backgroundColorHex,
            string fontColorHex,
            string metadataFontColorHex)
        {
            if (LoadedFontFamily == null || !LoadedFontFamily.HasValue) // Check HasValue for nullable struct
            {
                Console.Error.WriteLine("ImageService Error: Cannot create card because no valid font is loaded.");
                return false;
            }
            FontFamily fontFamily = LoadedFontFamily.Value;

            try
            {
                string? directory = Path.GetDirectoryName(outputImagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Color bgColor = Color.ParseHex(backgroundColorHex);
                Color textColor = Color.ParseHex(fontColorHex);
                Color metaColor = Color.ParseHex(metadataFontColorHex);

                float padding = Math.Max(15f, Math.Min(cardWidth * 0.05f, cardHeight * 0.05f));
                float contentWidth = cardWidth - (2 * padding);
                float currentY = padding;

                using (var image = new Image<Rgba32>(cardWidth, cardHeight))
                {
                    image.Mutate(ctx => ctx.BackgroundColor(bgColor));

                    if (!string.IsNullOrWhiteSpace(author))
                    {
                        Font metadataFont = fontFamily.CreateFont(_videoOptions.MetadataTargetFontSize, FontStyle.Regular);
                        string metadataText = $"u/{author}";
                        if (score.HasValue)
                        {
                            metadataText += $" • {score.Value} points";
                        }

                        var metadataTextOptions = new RichTextOptions(metadataFont)
                        {
                            Origin = new PointF(padding, currentY),
                            WrappingLength = contentWidth,
                            HorizontalAlignment = HorizontalAlignment.Left
                        };
                        image.Mutate(ctx => ctx.DrawText(metadataTextOptions, metadataText, metaColor));
                        FontRectangle metadataBounds = TextMeasurer.MeasureBounds(metadataText, metadataTextOptions);
                        currentY += metadataBounds.Height + (padding / 2);
                    }

                    Font mainFont = GetAdjustedFont(
                        fontFamily, mainText,
                        _videoOptions.ContentTargetFontSize, _videoOptions.ContentMinFontSize, _videoOptions.ContentMaxFontSize,
                        contentWidth, cardHeight - currentY - padding
                    );

                    Console.WriteLine($"ImageService: Drawing text card ({cardWidth}x{cardHeight}), Main Font: {fontFamily.Name}, Size: {mainFont.Size}pt (Target: {_videoOptions.ContentTargetFontSize}pt)");

                    var mainTextOptions = new RichTextOptions(mainFont)
                    {
                        Origin = new PointF(padding, currentY),
                        WrappingLength = contentWidth,
                        LineSpacing = 1.2f,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    image.Mutate(ctx => ctx.DrawText(mainTextOptions, mainText, textColor));

                    await image.SaveAsync(outputImagePath, new PngEncoder());
                    Console.WriteLine($"ImageService: Text card saved to {outputImagePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ImageService Error: Failed to create text card. {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return false;
            }
        }

        private Font GetAdjustedFont(FontFamily fontFamily, string text, float targetSize, float minSize, float maxSize, float maxWidth, float maxHeight)
        {
            float initialSize = Math.Clamp(targetSize, minSize, maxSize);
            Font font = fontFamily.CreateFont(initialSize, FontStyle.Regular);
            var textOptions = new RichTextOptions(font) { WrappingLength = maxWidth, Dpi = 72f, LineSpacing = 1.2f };
            FontRectangle size = TextMeasurer.MeasureBounds(text, textOptions);

            while ((size.Height > maxHeight || size.Width > maxWidth) && font.Size > minSize)
            {
                font = fontFamily.CreateFont(Math.Max(minSize, font.Size - 1), FontStyle.Regular);
                textOptions.Font = font;
                size = TextMeasurer.MeasureBounds(text, textOptions);
            }

            while (size.Height <= maxHeight && size.Width <= maxWidth && font.Size < maxSize && font.Size < targetSize)
            {
                float nextPotentialSize = Math.Min(maxSize, font.Size + 1);
                if (nextPotentialSize <= font.Size) break;

                Font nextFont = fontFamily.CreateFont(nextPotentialSize, FontStyle.Regular);
                var nextTextOptions = new RichTextOptions(nextFont) { WrappingLength = maxWidth, Dpi = 72f, LineSpacing = 1.2f };
                FontRectangle nextSize = TextMeasurer.MeasureBounds(text, nextTextOptions);

                if (nextSize.Height > maxHeight || nextSize.Width > maxWidth) break;

                font = nextFont;
                size = nextSize;
                if (font.Size >= targetSize) break;
            }

            if (size.Height > maxHeight && font.Size == minSize)
            {
                Console.WriteLine($"ImageService Warning: Text '{text.Substring(0, Math.Min(30, text.Length))}...' might be truncated vertically even at min font size {minSize}pt. MeasuredHeight: {size.Height}, MaxHeight: {maxHeight}");
            }
            return font;
        }
    }
}
