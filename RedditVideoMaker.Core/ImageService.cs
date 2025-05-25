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

namespace RedditVideoMaker.Core
{
    public class ImageService
    {
        private readonly FontCollection _fontCollection;
        private readonly FontFamily? _defaultFontFamily;
        private const string BundledFontFileName = "DejaVuSans.ttf";
        private const string ExpectedBundledFontFamilyName = "DejaVu Sans";

        public ImageService()
        {
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
                        // Pass activeFontFamily (which is non-nullable) to CalculateFontSizeForLine
                        float metadataFontSize = CalculateFontSizeForLine(activeFontFamily, cardWidth - (2 * basePadding), text: $"{author}{score}", maxLines: 1, cardHeight * 0.08f);
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
                        float mainTextFontSize = CalculateFontSizeToFit(mainText, activeFontFamily, mainTextRegionWidth, mainTextRegionHeight);
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

                        Console.WriteLine($"ImageService: Drawing text card ({cardWidth}x{cardHeight}), Main Font: {activeFontFamily.Name}, Size: {mainTextFontSize}pt");
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

        // Calculates font size for a single line of text to fit width, clamped by maxHeight.
        // Now takes a non-nullable FontFamily.
        private float CalculateFontSizeForLine(FontFamily fontFamily, float regionWidth, string text, int maxLines, float maxHeightAbsolute)
        {
            if (string.IsNullOrEmpty(text) || regionWidth <= 0 || maxHeightAbsolute <= 0) return 10f;

            float fontSize = maxHeightAbsolute / maxLines;
            Font testFont = fontFamily.CreateFont(fontSize);
            FontRectangle textSize = TextMeasurer.MeasureBounds(text, new RichTextOptions(testFont) { WrappingLength = regionWidth });

            while ((textSize.Width > regionWidth || textSize.Height > maxHeightAbsolute) && fontSize > 8f)
            {
                fontSize -= 1f;
                if (fontSize <= 8f) break;
                testFont = fontFamily.CreateFont(fontSize);
                textSize = TextMeasurer.MeasureBounds(text, new RichTextOptions(testFont) { WrappingLength = regionWidth });
            }
            return Math.Max(8f, fontSize);
        }

        // Tries to find a font size that fits the text within the given box dimensions.
        private float CalculateFontSizeToFit(string text, FontFamily fontFamily, float regionWidth, float regionHeight, float lineSpacing = 1.2f)
        {
            if (string.IsNullOrEmpty(text) || regionWidth <= 10 || regionHeight <= 10) return 8f;

            float maxFontSize = regionHeight / lineSpacing;
            float minFontSize = 8f;
            float currentFontSize = maxFontSize;

            for (int i = 0; i < 15; i++)
            {
                if (currentFontSize < minFontSize) currentFontSize = minFontSize;
                Font testFont = fontFamily.CreateFont(currentFontSize, FontStyle.Regular);
                var textOptions = new RichTextOptions(testFont)
                {
                    WrappingLength = regionWidth,
                    LineSpacing = lineSpacing,
                    Dpi = 72f
                };
                FontRectangle bounds = TextMeasurer.MeasureBounds(text, textOptions);

                if (bounds.Height <= regionHeight && bounds.Width <= regionWidth)
                {
                    break;
                }
                currentFontSize -= Math.Max(1f, currentFontSize * 0.1f);
                if (currentFontSize < minFontSize) break;
            }
            return Math.Max(minFontSize, currentFontSize);
        }
    }
}
