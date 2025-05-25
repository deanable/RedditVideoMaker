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
                FontFamily systemFont; // Non-nullable for out parameter
                if (SystemFonts.TryGet("Arial", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet("Verdana", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet(ExpectedBundledFontFamilyName, out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet("Segoe UI", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.TryGet("Calibri", out systemFont)) _defaultFontFamily = systemFont;
                else if (SystemFonts.Families.Any())
                {
                    FontFamily firstSystemFont = SystemFonts.Families.First(); // This is non-nullable
                    _defaultFontFamily = firstSystemFont; // Assign non-nullable to nullable
                    Console.WriteLine($"ImageService Warning: Using first available system font: {firstSystemFont.Name}.");
                }
            }

            // Corrected access to .Name after null check
            if (_defaultFontFamily != null)
            {
                // Assign to a non-nullable local variable after the check, using an explicit cast
                FontFamily confirmedFontFamily = (FontFamily)_defaultFontFamily; // Corrected for CS0266
                Console.WriteLine($"ImageService: Initialized. Using font: {confirmedFontFamily.Name}");
            }
            else { Console.Error.WriteLine("ImageService Critical: No font loaded. Text rendering will likely fail."); }
        }

        /// <summary>
        /// Creates an image (card) with the Reddit comment text, author, and score.
        /// </summary>
        /// <param name="mainText">The main comment body or post title.</param>
        /// <param name="author">The author of the comment/post (e.g., "u/username").</param>
        /// <param name="score">The score of the comment/post.</param>
        /// <param name="outputImagePath">The path to save the generated image.</param>
        /// <param name="cardWidth">Width of the output card image.</param>
        /// <param name="cardHeight">Height of the output card image.</param>
        /// <param name="backgroundColorString">Background color of the card.</param>
        /// <param name="fontColorString">Main font color for the text.</param>
        /// <param name="metadataFontColorString">Font color for author/score metadata.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> CreateRedditContentCardAsync(
            string mainText,
            string? author, // Author can be null or empty for titles sometimes
            int? score,    // Score can be null
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

            FontFamily activeFontFamily = (FontFamily)_defaultFontFamily; // Corrected for CS0266. Explicit cast after null check.

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

                    // --- Define Fonts ---
                    float basePadding = Math.Max(10f, Math.Min(cardWidth * 0.05f, cardHeight * 0.05f));
                    float metadataFontSize = CalculateFontSizeForCard(cardWidth, cardHeight, 20, 0.1f); // Smaller font for metadata
                    Font metadataFont = activeFontFamily.CreateFont(metadataFontSize, FontStyle.Italic);

                    float currentY = basePadding; // Starting Y position

                    // --- Draw Author and Score ---
                    if (!string.IsNullOrWhiteSpace(author))
                    {
                        string authorText = $"u/{author}";
                        if (score.HasValue)
                        {
                            authorText += $" • {score.Value} points";
                        }
                        var authorTextOptions = new RichTextOptions(metadataFont)
                        {
                            Origin = new PointF(basePadding, currentY),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            WrappingLength = cardWidth - (2 * basePadding),
                            Dpi = 72f
                        };
                        image.Mutate(ctx => ctx.DrawText(authorTextOptions, authorText, metadataFontColor));
                        // Estimate height of metadata text (this is a rough estimate)
                        FontRectangle authorBounds = TextMeasurer.MeasureBounds(authorText, authorTextOptions);
                        currentY += authorBounds.Height + (basePadding / 2); // Add some spacing
                    }

                    // --- Draw Main Text ---
                    // Adjust available height for main text based on metadata drawn
                    float availableHeightForMainText = cardHeight - currentY - basePadding;
                    float mainTextFontSize = CalculateFontSizeForCard(cardWidth, (int)availableHeightForMainText, mainText.Length, 0.9f); // Use more of available height
                    Font mainFont = activeFontFamily.CreateFont(mainTextFontSize, FontStyle.Regular);

                    var mainTextGraphicsOptions = new RichTextOptions(mainFont)
                    {
                        Origin = new PointF(basePadding, currentY),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        WrappingLength = cardWidth - (2 * basePadding),
                        LineSpacing = 1.2f,
                        Dpi = 72f
                    };

                    Console.WriteLine($"ImageService: Drawing text card ({cardWidth}x{cardHeight}), Main Font: {activeFontFamily.Name}, Size: {mainTextFontSize}pt");
                    image.Mutate(ctx => ctx.DrawText(mainTextGraphicsOptions, mainText, mainFontColor));

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

        private float CalculateFontSizeForCard(int boxWidth, int boxHeight, int textLength, float targetFillFactor = 0.7f)
        {
            if (textLength == 0 || boxHeight <= 0 || boxWidth <= 0) return 10f;

            float referenceDimension = Math.Min(boxWidth, boxHeight);
            float availableHeight = boxHeight * targetFillFactor;

            // Attempt to make font size proportional to available height and inversely to sqrt of text length (more text = smaller font)
            // This is a heuristic and can be refined.
            float fontSize = (float)(availableHeight / (Math.Sqrt(textLength / 10.0) + 1.0)); // Reduce impact of length slightly
            fontSize = Math.Max(8f, fontSize); // Ensure a minimum font size

            // Clamp font size to reasonable min/max values relative to card height/width
            return Math.Clamp(fontSize, referenceDimension / 40f, referenceDimension / 5f);
        }
    }
}
