// ImageService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using Microsoft.Extensions.Options; // Required for IOptions

namespace RedditVideoMaker.Core
{
    public class ImageService
    {
        private readonly FontCollection _fontCollection;
        private readonly FontFamily? _defaultFontFamily;
        private readonly VideoOptions _videoOptions; // To store video options including font sizes
        private const string BundledFontFileName = "DejaVuSans.ttf";
        private const string ExpectedBundledFontFamilyName = "DejaVu Sans";

        public ImageService(IOptions<VideoOptions> videoOptions) // Inject VideoOptions
        {
            _videoOptions = videoOptions.Value; // Store the options

            _fontCollection = new FontCollection();
            FontFamily? loadedFont = null;
            try
            {
                string executableLocation = AppContext.BaseDirectory;
                string fontPath = Path.Combine(executableLocation, "Fonts", BundledFontFileName);
                if (File.Exists(fontPath))
                {
                    _fontCollection.Add(fontPath);
                    if (_fontCollection.TryGet(ExpectedBundledFontFamilyName, out FontFamily foundByName))
                    {
                        loadedFont = foundByName;
                        Console.WriteLine($"ImageService: Successfully loaded bundled font '{ExpectedBundledFontFamilyName}' from {fontPath}");
                    }
                    else if (_fontCollection.Families.Any())
                    {
                        FontFamily firstFamilyInCollection = _fontCollection.Families.First();
                        loadedFont = firstFamilyInCollection;
                        Console.WriteLine($"ImageService: Loaded bundled font from {fontPath}, using actual family name '{firstFamilyInCollection.Name}'. (Expected name '{ExpectedBundledFontFamilyName}' might differ or wasn't found).");
                    }
                    else { Console.Error.WriteLine($"ImageService Warning: Added font file {fontPath} but could not retrieve any font family from it."); }
                }
                else { Console.Error.WriteLine($"ImageService Warning: Bundled font file not found at {fontPath}. Will attempt to use system fonts."); }
            }
            catch (Exception ex) { Console.Error.WriteLine($"ImageService Error: Failed to load bundled font. {ex.Message}. Will attempt to use system fonts."); }

            _defaultFontFamily = loadedFont;
            if (_defaultFontFamily == null)
            {
                Console.WriteLine("ImageService: Bundled font not loaded. Attempting to use system fonts as fallback.");
                FontFamily systemFont;
                if (SystemFonts.TryGet("Arial", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet("Verdana", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet(ExpectedBundledFontFamilyName, out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet("Segoe UI", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet("Calibri", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.Families.Any())
                {
                    FontFamily firstSystemFont = SystemFonts.Families.First();
                    _defaultFontFamily = firstSystemFont;
                    Console.WriteLine($"ImageService Warning: Using first available system font: {firstSystemFont.Name}.");
                }
            }

            if (_defaultFontFamily != null)
            {
                FontFamily confirmedFontFamily = (FontFamily)_defaultFontFamily;
                Console.WriteLine($"ImageService: Initialized. Using font: {confirmedFontFamily.Name}");
            }
            else { Console.Error.WriteLine("ImageService Critical: No font loaded. Text rendering will likely fail."); }
        }

        public async Task<bool> CreateRedditContentCardAsync(
            string mainText,
            string? author,
            int? score,
            string outputImagePath,
            int cardWidth,
            int cardHeight,
            string backgroundColorString = "DarkSlateGray",
            string fontColorString = "White",
            string metadataFontColorString = "LightGray")
        {
            if (string.IsNullOrWhiteSpace(mainText))
            { Console.Error.WriteLine("ImageService Error: Main text for card cannot be empty."); return false; }
            if (string.IsNullOrWhiteSpace(outputImagePath))
            { Console.Error.WriteLine("ImageService Error: Output image path for card cannot be empty."); return false; }
            if (_defaultFontFamily == null)
            {
                Console.Error.WriteLine("ImageService Error: Default font family is not initialized. Cannot render text card.");
                return false;
            }

            FontFamily activeFontFamily = (FontFamily)_defaultFontFamily;

            try
            {
                string? directory = Path.GetDirectoryName(outputImagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Color backgroundColor = Color.TryParse(backgroundColorString, out Color bgCol) ? bgCol : Color.DarkSlateGray;
                Color mainFontColor = Color.TryParse(fontColorString, out Color textCol) ? textCol : Color.White;
                Color metadataFontColor = Color.TryParse(metadataFontColorString, out Color metaCol) ? metaCol : Color.LightGray;

                using (Image<Rgba32> image = new Image<Rgba32>(cardWidth, cardHeight))
                {
                    image.Mutate(ctx => ctx.BackgroundColor(backgroundColor));

                    float basePadding = Math.Max(15f, Math.Min(cardWidth * 0.05f, cardHeight * 0.05f));
                    float currentY = basePadding;

                    // --- Draw Author and Score ---
                    if (!string.IsNullOrWhiteSpace(author))
                    {
                        float metadataFontSize = CalculateFontSizeForLine(
                            activeFontFamily,
                            cardWidth - (2 * basePadding),
                            text: $"{author}{score}", // Text used for sizing estimation
                            _videoOptions.MetadataTargetFontSize,
                            _videoOptions.MetadataMinFontSize,
                            _videoOptions.MetadataMaxFontSize,
                            maxLines: 2); // Allow metadata to wrap to 2 lines if necessary
                        Font metadataFont = activeFontFamily.CreateFont(metadataFontSize, FontStyle.Italic);

                        string authorScoreText = $"u/{author}";
                        if (score.HasValue)
                        {
                            authorScoreText += $" • {score.Value} points";
                        }

                        var authorTextOptions = new RichTextOptions(metadataFont)
                        {
                            Origin = new PointF(basePadding, currentY),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            WrappingLength = cardWidth - (2 * basePadding),
                            Dpi = 72f
                        };
                        image.Mutate(ctx => ctx.DrawText(authorTextOptions, authorScoreText, metadataFontColor));

                        FontRectangle authorBounds = TextMeasurer.MeasureBounds(authorScoreText, authorTextOptions);
                        currentY += authorBounds.Height + basePadding * 0.75f;
                    }

                    // --- Draw Main Text ---
                    float mainTextRegionWidth = cardWidth - (2 * basePadding);
                    float mainTextRegionHeight = cardHeight - currentY - basePadding;

                    if (mainTextRegionHeight > 20)
                    {
                        float mainTextFontSize = CalculateFontSizeToFit(
                            mainText,
                            activeFontFamily,
                            mainTextRegionWidth,
                            mainTextRegionHeight,
                            _videoOptions.ContentTargetFontSize,
                            _videoOptions.ContentMinFontSize,
                            _videoOptions.ContentMaxFontSize);
                        Font mainFont = activeFontFamily.CreateFont(mainTextFontSize, FontStyle.Regular);

                        var mainTextGraphicsOptions = new RichTextOptions(mainFont)
                        {
                            Origin = new PointF(basePadding, currentY),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            WrappingLength = mainTextRegionWidth,
                            LineSpacing = 1.2f,
                            Dpi = 72f
                        };

                        Console.WriteLine($"ImageService: Drawing text card ({cardWidth}x{cardHeight}), Main Font: {activeFontFamily.Name}, Size: {mainTextFontSize}pt (Target: {_videoOptions.ContentTargetFontSize}pt)");
                        image.Mutate(ctx => ctx.DrawText(mainTextGraphicsOptions, mainText, mainFontColor));
                    }
                    else
                    {
                        Console.Error.WriteLine("ImageService Warning: Not enough space to render main text after metadata.");
                    }

                    await image.SaveAsPngAsync(outputImagePath);
                    Console.WriteLine($"ImageService: Text card saved to {outputImagePath}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ImageService Error: Failed to create text card. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        private float CalculateFontSizeForLine(FontFamily fontFamily, float regionWidth, string text,
            float targetSize, float minSize, float maxSize, int maxLines = 1, float lineSpacing = 1.2f)
        {
            if (string.IsNullOrEmpty(text) || regionWidth <= 0 || targetSize <= 0) return Math.Clamp(minSize, 8f, maxSize);

            float currentFontSize = Math.Clamp(targetSize, minSize, maxSize);

            for (int i = 0; i < 10; i++) // Limit iterations
            {
                Font testFont = fontFamily.CreateFont(currentFontSize);
                var textOptions = new RichTextOptions(testFont)
                {
                    WrappingLength = regionWidth,
                    LineSpacing = lineSpacing, // Consider line spacing if maxLines > 1
                    Dpi = 72f
                };
                FontRectangle textSize = TextMeasurer.MeasureBounds(text, textOptions);

                // Calculate number of lines based on wrapped height
                float actualLines = (textSize.Height / (currentFontSize * lineSpacing));

                if (textSize.Width <= regionWidth && actualLines <= maxLines)
                {
                    // Fits, try to optimize towards target or max if current is smaller
                    if (currentFontSize < targetSize && currentFontSize < maxSize)
                    {
                        float potentialSize = currentFontSize + 1f;
                        testFont = fontFamily.CreateFont(potentialSize);
                        textOptions.Font = testFont; // Update font in options for next measurement
                        textSize = TextMeasurer.MeasureBounds(text, textOptions);
                        actualLines = (textSize.Height / (potentialSize * lineSpacing));
                        if (textSize.Width <= regionWidth && actualLines <= maxLines)
                        {
                            currentFontSize = potentialSize; // It still fits, so use larger
                            continue; // Try to grow more if possible
                        }
                        // else, previous size was better
                    }
                    break; // Found a good fit or best possible enlargement
                }

                // If it doesn't fit, reduce size
                if (currentFontSize <= minSize) break; // Already at min
                currentFontSize = Math.Max(minSize, currentFontSize - 1f);
            }
            return Math.Clamp(currentFontSize, minSize, maxSize);
        }

        private float CalculateFontSizeToFit(string text, FontFamily fontFamily, float regionWidth, float regionHeight,
            float targetSize, float minSize, float maxSize, float lineSpacing = 1.2f)
        {
            if (string.IsNullOrEmpty(text) || regionWidth <= 10 || regionHeight <= 10) return Math.Clamp(minSize, 8f, maxSize);

            float currentFontSize = Math.Clamp(targetSize, minSize, maxSize); // Start with target, clamped by min/max

            // Iteratively adjust font size
            for (int i = 0; i < 20; i++) // Limit iterations
            {
                Font testFont = fontFamily.CreateFont(currentFontSize);
                var textOptions = new RichTextOptions(testFont)
                {
                    WrappingLength = regionWidth,
                    LineSpacing = lineSpacing,
                    Dpi = 72f
                };
                FontRectangle bounds = TextMeasurer.MeasureBounds(text, textOptions);

                if (bounds.Height <= regionHeight && bounds.Width <= regionWidth) // It fits
                {
                    // If we started at target and it fits, this is good.
                    // If we want to try to grow it towards max, we could add a small loop here.
                    // For simplicity, if it fits when starting at target (or shrunk to fit), we accept.
                    // If current size is below target, and target itself would fit, prefer target.
                    if (currentFontSize < targetSize)
                    {
                        Font targetFont = fontFamily.CreateFont(targetSize);
                        var targetOptions = new RichTextOptions(targetFont) { WrappingLength = regionWidth, LineSpacing = lineSpacing, Dpi = 72f };
                        FontRectangle targetBounds = TextMeasurer.MeasureBounds(text, targetOptions);
                        if (targetBounds.Height <= regionHeight && targetBounds.Width <= regionWidth)
                        {
                            currentFontSize = targetSize; // Prefer target if it fits
                        }
                    }
                    break;
                }

                if (currentFontSize <= minSize) break; // Already at min and doesn't fit

                // Reduce font size: more aggressively if far from fitting
                float heightRatio = bounds.Height / regionHeight;
                float reductionStep = 1f;
                if (heightRatio > 1.5) reductionStep = currentFontSize * 0.2f; // Reduce by 20% if way too tall
                else if (heightRatio > 1.1) reductionStep = currentFontSize * 0.1f; // Reduce by 10%

                currentFontSize = Math.Max(minSize, currentFontSize - reductionStep);
                if (currentFontSize < minSize) currentFontSize = minSize; // Ensure it doesn't go below min
            }
            return Math.Clamp(currentFontSize, minSize, maxSize);
        }
    }
}
