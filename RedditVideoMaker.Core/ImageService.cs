// ImageService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq; // Required for .Any() and .First()
using System.Threading.Tasks;
using SixLabors.ImageSharp; // Core ImageSharp namespace
using SixLabors.ImageSharp.Processing; // For operations like Mutate
using SixLabors.ImageSharp.PixelFormats; // For Rgba32, Color
using SixLabors.ImageSharp.Drawing.Processing; // For drawing operations
using SixLabors.Fonts; // For Font, FontCollection, SystemFonts

namespace RedditVideoMaker.Core
{
    public class ImageService
    {
        private readonly FontCollection _fontCollection;
        private readonly FontFamily? _defaultFontFamily;
        private const string BundledFontFileName = "DejaVuSans.ttf"; // Or your exact font filename
        private const string ExpectedBundledFontFamilyName = "DejaVu Sans"; // The family name embedded in the font

        public ImageService()
        {
            _fontCollection = new FontCollection();
            FontFamily? loadedFont = null;

            // --- Attempt to load bundled font ---
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
                        // _fontCollection.Families.First() returns a non-nullable FontFamily
                        FontFamily firstFamilyInCollection = _fontCollection.Families.First();
                        loadedFont = firstFamilyInCollection; // Assign non-nullable to nullable
                        // Now firstFamilyInCollection is non-nullable, so .Name is safe to access.
                        Console.WriteLine($"ImageService: Loaded bundled font from {fontPath}, using actual family name '{firstFamilyInCollection.Name}'. (Expected name '{ExpectedBundledFontFamilyName}' might differ or wasn't found).");
                    }
                    else
                    {
                        Console.Error.WriteLine($"ImageService Warning: Added font file {fontPath} but could not retrieve any font family from it.");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"ImageService Warning: Bundled font file not found at {fontPath}. Will attempt to use system fonts.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ImageService Error: Failed to load bundled font. {ex.Message}. Will attempt to use system fonts.");
            }

            _defaultFontFamily = loadedFont;

            // --- Fallback to system fonts if bundled font loading failed ---
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

            // --- Final check and logging ---
            // Assign to a local variable within the scope of the null check for clarity
            if (_defaultFontFamily != null)
            {
                // At this point, _defaultFontFamily is confirmed not null.
                // Use an explicit cast to satisfy the compiler for assignment to a non-nullable type.
                FontFamily confirmedFont = (FontFamily)_defaultFontFamily;
                Console.WriteLine($"ImageService: Initialized. Using font: {confirmedFont.Name}");
            }
            else
            {
                Console.Error.WriteLine("ImageService Critical: No font loaded (neither bundled nor system). Text rendering will likely fail.");
            }
        }

        public async Task<bool> CreateImageFromTextAsync(string text, string outputImagePath, int imageWidth = 1920, int imageHeight = 1080)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.Error.WriteLine("ImageService Error: Input text cannot be empty.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputImagePath))
            {
                Console.Error.WriteLine("ImageService Error: Output image path cannot be empty.");
                return false;
            }

            if (_defaultFontFamily == null)
            {
                Console.Error.WriteLine("ImageService Error: Default font family is not initialized (no suitable font could be loaded). Cannot render text.");
                return false;
            }

            // After the null check, _defaultFontFamily is known to be non-null.
            // Use an explicit cast to satisfy the compiler for assignment to a non-nullable type.
            FontFamily activeFontFamily = (FontFamily)_defaultFontFamily;

            try
            {
                string? directory = Path.GetDirectoryName(outputImagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (Image<Rgba32> image = new Image<Rgba32>(imageWidth, imageHeight))
                {
                    image.Mutate(ctx => ctx.BackgroundColor(Color.DarkSlateGray));

                    float fontSize = CalculateFontSize(imageWidth, imageHeight, text.Length);
                    Font font = activeFontFamily.CreateFont(fontSize, FontStyle.Regular);

                    var textColor = Color.White;
                    var textPadding = 30f;

                    var textGraphicsOptions = new RichTextOptions(font)
                    {
                        Origin = new PointF(textPadding, textPadding),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        WrappingLength = imageWidth - (2 * textPadding),
                        LineSpacing = 1.2f,
                        Dpi = 72f
                    };

                    Console.WriteLine($"ImageService: Drawing text onto image ({imageWidth}x{imageHeight}), Font: {activeFontFamily.Name}, Size: {fontSize}pt");

                    image.Mutate(ctx => ctx.DrawText(textGraphicsOptions, text, textColor));

                    await image.SaveAsync(outputImagePath);
                    Console.WriteLine($"ImageService: Image saved to {outputImagePath}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ImageService Error: Failed to create image from text. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        private float CalculateFontSize(int imageWidth, int imageHeight, int textLength)
        {
            float baseSize = Math.Min(imageWidth, imageHeight);
            if (textLength < 50) return baseSize / 12f;
            if (textLength < 150) return baseSize / 18f;
            if (textLength < 300) return baseSize / 24f;
            if (textLength < 600) return baseSize / 30f;
            return baseSize / 36f;
        }
    }
}
