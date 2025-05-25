// TextUtilities.cs (in RedditVideoMaker.Core project)
using SixLabors.Fonts; // For FontFamily, Font, TextMeasurer, FontRectangle, RichTextOptions
using SixLabors.ImageSharp.Drawing.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RedditVideoMaker.Core
{
    public static class TextUtilities
    {
        /// <summary>
        /// Splits a long string of text into multiple smaller strings (pages),
        /// each of which should fit within the given dimensions when rendered with the specified font.
        /// </summary>
        /// <param name="text">The full text to split.</param>
        /// <param name="font">The Font object to use for measurement.</param>
        /// <param name="maxWidth">The maximum width the text can occupy on a page (card width minus padding).</param>
        /// <param name="maxHeight">The maximum height the text can occupy on a page (card height minus padding and any header).</param>
        /// <param name="lineSpacing">The line spacing factor used in rendering.</param>
        /// <returns>A list of strings, where each string represents a page of text.</returns>
        public static List<string> SplitTextIntoPages(
            string text,
            Font font,
            float maxWidth,
            float maxHeight,
            float lineSpacing = 1.2f)
        {
            var pages = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0 || maxHeight <= 0)
            {
                if (!string.IsNullOrWhiteSpace(text)) pages.Add(text);
                return pages;
            }

            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .SelectMany(word => SplitWordIfTooLong(word, font, maxWidth))
                            .ToList();

            if (!words.Any())
            {
                pages.Add(string.Empty);
                return pages;
            }

            var currentPageText = new StringBuilder();
            var currentLineText = new StringBuilder();
            // float currentLineHeight = 0f; // CS0219: This variable was assigned but its value was never used. Removed.
            float currentY = 0f;

            var textOptions = new RichTextOptions(font)
            {
                // WrappingLength = maxWidth, // Not needed here as we build line by line
                LineSpacing = lineSpacing,
                Dpi = 72f
            };

            FontRectangle singleLineBounds = TextMeasurer.MeasureBounds("X", textOptions);
            float estimatedLineHeight = singleLineBounds.Height > 0 ? singleLineBounds.Height : font.Size;
            if (estimatedLineHeight <= 0) estimatedLineHeight = font.Size > 0 ? font.Size : 12f; // Further fallback for estimated line height

            foreach (var word in words)
            {
                string testLine = currentLineText.Length > 0 ? currentLineText.ToString() + " " + word : word;
                // Use textOptions without WrappingLength for accurate line width measurement before manual break
                FontRectangle wordBounds = TextMeasurer.MeasureBounds(testLine, new RichTextOptions(font) { Dpi = 72f });


                if (wordBounds.Width > maxWidth && currentLineText.Length > 0) // Current line is full (without the new word), add it to page
                {
                    if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0) // Page is full
                    {
                        pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                        currentPageText.Clear();
                        currentY = 0;
                    }
                    currentPageText.Append(currentLineText.ToString().Trim() + "\n");
                    currentY += estimatedLineHeight;
                    currentLineText.Clear();

                    // Now add the word that caused overflow to start a new line
                    currentLineText.Append(word);
                }
                else if (wordBounds.Width > maxWidth && currentLineText.Length == 0) // Word itself is too long for a line
                {
                    // This case should ideally be handled by SplitWordIfTooLong, but as a fallback:
                    if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0) // Page is full
                    {
                        pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                        currentPageText.Clear();
                        currentY = 0;
                    }
                    currentPageText.Append(word + "\n"); // Add word on a new line
                    currentY += estimatedLineHeight;
                    // currentLineText remains clear as this word took the whole line.
                }
                else // Word fits on the current line or starts a new line if currentLineText was empty
                {
                    currentLineText.Append((currentLineText.Length > 0 ? " " : "") + word);
                }
            }

            if (currentLineText.Length > 0)
            {
                if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0)
                {
                    pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                    currentPageText.Clear();
                }
                currentPageText.Append(currentLineText.ToString().Trim());
            }

            if (currentPageText.Length > 0)
            {
                pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
            }

            if (!pages.Any() && !string.IsNullOrWhiteSpace(text))
            {
                pages.Add(text);
            }

            return pages.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(); // Ensure no empty pages are returned
        }

        private static IEnumerable<string> SplitWordIfTooLong(string word, Font font, float maxWidth)
        {
            var textOptions = new RichTextOptions(font) { Dpi = 72f };
            FontRectangle wordBounds = TextMeasurer.MeasureBounds(word, textOptions);

            if (wordBounds.Width <= maxWidth)
            {
                yield return word;
                yield break;
            }

            var currentChunk = new StringBuilder();
            for (int i = 0; i < word.Length; i++)
            {
                char currentChar = word[i];
                FontRectangle charBounds = TextMeasurer.MeasureBounds(currentChar.ToString(), textOptions);
                FontRectangle currentChunkWithCharBounds = TextMeasurer.MeasureBounds(currentChunk.ToString() + currentChar, textOptions);

                if (currentChunk.Length > 0 && currentChunkWithCharBounds.Width > maxWidth)
                {
                    yield return currentChunk.ToString();
                    currentChunk.Clear();
                }
                currentChunk.Append(currentChar);
            }
            if (currentChunk.Length > 0)
            {
                yield return currentChunk.ToString();
            }
        }
    }
}
