// TextUtilities.cs (in RedditVideoMaker.Core project)
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing; // Used for RichTextOptions which is part of text measuring
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Provides utility methods for text manipulation, such as cleaning text for Text-to-Speech (TTS)
    /// and splitting text into pages for display on image cards.
    /// </summary>
    public static class TextUtilities
    {
        // A small, somewhat arbitrary fallback for estimated line height if other measures fail.
        // It's better if font.Size is always reliable and positive.
        private const float DefaultFallbackLineHeight = 12f;

        /// <summary>
        /// Cleans a given input string to make it more suitable for Text-to-Speech (TTS) processing.
        /// This involves removing or replacing Markdown elements, normalizing whitespace, and ensuring
        /// the text ends with punctuation for a natural pause.
        /// </summary>
        /// <param name="inputText">The raw text string to clean.</param>
        /// <returns>A cleaned version of the input text, optimized for TTS.</returns>
        public static string CleanTextForTts(string inputText)
        {
            if (string.IsNullOrWhiteSpace(inputText))
            {
                return string.Empty;
            }

            string text = inputText;

            // --- Remove or replace common Markdown elements ---

            // 1. Links: [link text](url) -> "link text"
            // Replaces Markdown links with just the link's display text.
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");

            // 2. Emphasis (Bold/Italics):
            // These regular expressions aim to remove Markdown emphasis markers (*, _).
            // The lookbehind `(?<=[^\\])` attempts to avoid unescaping intentionally escaped characters,
            // though Markdown parsing can be more complex.

            // ***bold italic*** or ___bold italic___ (less common for underscores) -> "bold italic"
            text = Regex.Replace(text, @"(?<!\\)\*\*\*(.*?)(?<!\\)\*\*\*", "$1"); // Handles ***text***
            text = Regex.Replace(text, @"(?<!\\)___(.*?)___", "$1");             // Handles ___text___ (less common but for completeness)

            // **bold** or __bold__ -> "bold"
            text = Regex.Replace(text, @"(?<!\\)\*\*(.*?)(?<!\\)\*\*", "$1");     // Handles **text**
            text = Regex.Replace(text, @"(?<!\\)__(.*?)__", "$1");               // Handles __text__

            // *italic* or _italic_ -> "italic"
            text = Regex.Replace(text, @"(?<!\\)\*(.*?)(?<!\\)\*", "$1");         // Handles *text*
            text = Regex.Replace(text, @"(?<!\\)_(.*?)_", "$1");                 // Handles _text_

            // 3. Strikethrough: ~~text~~ -> "text"
            text = Regex.Replace(text, @"~~(.*?)~~", "$1");

            // 4. Code:
            // Inline code `code` -> "code" (removes backticks)
            text = text.Replace("`", "");
            // Fenced code blocks (``` ... ```) are more complex. This simple replacement
            // removes backticks but doesn't specifically handle language identifiers or preserve block structure well for TTS.
            // For TTS, the content of code blocks might be read out, or one might choose to replace it with "code block".
            // The current approach just removes the backticks.

            // 5. Blockquotes: > quote -> "quote" (removes leading '>' and optional space)
            text = Regex.Replace(text, @"^\s*>\s*", "", RegexOptions.Multiline);

            // 6. Headers: # H1, ## H2 etc. -> "H1", "H2" (removes leading '#'s and optional space)
            text = Regex.Replace(text, @"^\s*#+\s*", "", RegexOptions.Multiline);

            // 7. Horizontal rules: ---, ***, ___ -> replace with a period and space (simulates a pause)
            // Matches lines that consist only of 3 or more hyphens, asterisks, or underscores, possibly separated by spaces.
            text = Regex.Replace(text, @"^\s*(\*\s*){3,}\s*$", ". ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*(-\s*){3,}\s*$", ". ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*(_\s*){3,}\s*$", ". ", RegexOptions.Multiline);

            // --- Normalize line breaks and whitespace ---

            // Replace multiple consecutive line breaks (2 or more) with a single period and space.
            // This helps TTS engines create a more natural pause than just spaces.
            text = Regex.Replace(text, @"(\r\n|\r|\n){2,}", ". ");

            // Replace single line breaks (that are not already sentence endings) with a single space.
            // This joins paragraphs or lines that should flow together in speech.
            // The lookbehind `(?<!\.)` ensures we don't add a space after a period that was just inserted.
            // The lookahead `(?!\s*\.)` prevents replacing newlines that are followed by a period (e.g., from multi-newline replacement).
            text = Regex.Replace(text, @"(?<!\.)(\r\n|\r|\n)(?!\s*\.)", " ");

            // Replace multiple spaces with a single space.
            text = Regex.Replace(text, @"\s{2,}", " ");

            // Remove leading/trailing whitespace from the entire text.
            text = text.Trim();

            // Ensure the text ends with a punctuation mark for a clean TTS stop.
            // This gives a sense of finality to the spoken text.
            if (!string.IsNullOrEmpty(text) && !".?!".Contains(text.Last()))
            {
                text += ".";
            }

            return text;
        }

        /// <summary>
        /// Splits a long string of text into multiple smaller strings (pages),
        /// each fitting within specified width and height constraints when rendered with a given font.
        /// </summary>
        /// <param name="text">The text to be split into pages.</param>
        /// <param name="font">The <see cref="Font"/> used for rendering and measuring the text.</param>
        /// <param name="maxWidth">The maximum width (in pixels) available for each line of text on a page.</param>
        /// <param name="maxHeight">The maximum height (in pixels) available for text on a single page.</param>
        /// <param name="lineSpacing">The line spacing factor (e.g., 1.2f for 120% line height). Default is 1.2f.</param>
        /// <returns>A list of strings, where each string represents a page of text.
        /// Returns an empty list if input text is null/whitespace or dimensions are invalid,
        /// unless the text is non-empty but other conditions fail, then it might return the original text as one page.</returns>
        public static List<string> SplitTextIntoPages(
            string text,
            Font font,
            float maxWidth,
            float maxHeight,
            float lineSpacing = 1.2f)
        {
            var pages = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0 || maxHeight <= 0 || font == null)
            {
                // If text is valid but other params are not, add the original text as a single page,
                // or let the caller handle this. For now, if text exists, we add it.
                if (!string.IsNullOrWhiteSpace(text))
                {
                    pages.Add(text);
                }
                return pages;
            }

            // First, split the input text into words. Also, pre-split very long words that might exceed maxWidth on their own.
            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .SelectMany(word => SplitWordIfTooLong(word, font, maxWidth))
                            .ToList();

            if (!words.Any())
            {
                // If there are no words (e.g. input was only whitespace), add an empty page or the original text.
                pages.Add(string.IsNullOrWhiteSpace(text) ? string.Empty : text);
                return pages;
            }

            var currentPageText = new StringBuilder();
            var currentLineText = new StringBuilder();
            float currentY = 0f;

            var textOptionsForMeasurement = new RichTextOptions(font)
            {
                LineSpacing = lineSpacing,
                Dpi = 72f // Assuming 72 DPI for consistency with point sizes if font.Size is used directly.
                          // ImageSharp's default DPI for measurement might vary, so explicit is good.
            };

            // Estimate line height. Using "Xg" can give a reasonable estimate for typical character height including ascenders/descenders.
            // Using a string with both uppercase and descender characters.
            FontRectangle singleLineBounds = TextMeasurer.MeasureBounds("Xg", textOptionsForMeasurement);
            float estimatedLineHeight = singleLineBounds.Height;

            // Fallback if measured height is zero or font size is more reliable
            if (estimatedLineHeight <= 0)
            {
                estimatedLineHeight = font.Size; // font.Size is in points.
            }
            if (estimatedLineHeight <= 0) // Further fallback if font.Size was also zero/negative (unlikely for valid font).
            {
                estimatedLineHeight = DefaultFallbackLineHeight;
            }


            foreach (var word in words)
            {
                string testLine = currentLineText.Length > 0 ? currentLineText.ToString() + " " + word : word;
                // Measure the width of the current line if the new word were added.
                // Note: For optimal performance, avoid frequent re-measurement of the same growing string.
                // However, for clarity, this direct approach is used.
                FontRectangle wordBoundsOnLine = TextMeasurer.MeasureBounds(testLine, new RichTextOptions(font) { Dpi = 72f });

                // If the new word makes the line too wide:
                if (wordBoundsOnLine.Width > maxWidth && currentLineText.Length > 0)
                {
                    // The current line (without the new word) is complete.
                    // Check if adding this line would exceed the page's max height.
                    if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0)
                    {
                        // Current page is full. Add it to the list of pages.
                        pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                        currentPageText.Clear();
                        currentY = 0; // Reset Y position for the new page.
                    }
                    // Add the completed line to the current page.
                    currentPageText.Append(currentLineText.ToString().Trim() + "\n");
                    currentY += estimatedLineHeight; // Increment Y position.
                    currentLineText.Clear(); // Start a new line.
                }

                // If the word itself (even on a new line) is too wide (this should be rare if SplitWordIfTooLong works well)
                // or if the line became too wide and we just started a new line for the current word.
                if (currentLineText.Length == 0) // We are at the beginning of a new line
                {
                    FontRectangle singleWordBounds = TextMeasurer.MeasureBounds(word, new RichTextOptions(font) { Dpi = 72f });
                    if (singleWordBounds.Width > maxWidth)
                    {
                        // This specific word is too long for a line by itself.
                        // This might happen if SplitWordIfTooLong couldn't break it further or if it's a single character sequence wider than maxWidth.
                        // We'll add it to its own line and let it overflow, or rely on image rendering to clip/handle it.
                        // A more sophisticated approach might involve hyphenation or forced breaks within the rendering itself.

                        // If adding this very long word (on its new line) would start a new page:
                        if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0)
                        {
                            pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                            currentPageText.Clear();
                            currentY = 0;
                        }
                        currentPageText.Append(word + "\n"); // Add the long word, followed by a newline.
                        currentY += estimatedLineHeight;
                        // currentLineText remains empty, word is consumed.
                        continue; // Move to the next word.
                    }
                }

                // Append the word to the current line (with a leading space if it's not the first word on the line).
                currentLineText.Append((currentLineText.Length > 0 ? " " : "") + word);
            }

            // After the loop, add any remaining text in currentLineText to the page.
            if (currentLineText.Length > 0)
            {
                if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0)
                {
                    pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                    currentPageText.Clear();
                    // currentY = 0; // Not strictly needed as we are about to add the last bit.
                }
                currentPageText.Append(currentLineText.ToString().Trim());
            }

            // Add the final page content if any exists.
            if (currentPageText.Length > 0)
            {
                pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
            }

            // If no pages were created but the original text was non-empty (e.g., due to very small maxHeight),
            // add the original text as a single page.
            if (!pages.Any() && !string.IsNullOrWhiteSpace(text))
            {
                pages.Add(text);
            }

            // Filter out any pages that might have ended up empty or whitespace only.
            return pages.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        }

        /// <summary>
        /// Splits a single word into multiple chunks if its rendered width exceeds the specified maximum width.
        /// This is a helper method for <see cref="SplitTextIntoPages"/> to handle exceptionally long words
        /// that don't contain spaces but are too wide for a line.
        /// </summary>
        /// <param name="word">The word to split.</param>
        /// <param name="font">The <see cref="Font"/> used for measuring the word.</param>
        /// <param name="maxWidth">The maximum width (in pixels) allowed for a chunk of the word.</param>
        /// <returns>An enumerable of strings, where each string is a chunk of the original word
        /// that fits within the <paramref name="maxWidth"/>. If the word fits, it's returned as a single chunk.</returns>
        private static IEnumerable<string> SplitWordIfTooLong(string word, Font font, float maxWidth)
        {
            var textOptions = new RichTextOptions(font) { Dpi = 72f };
            FontRectangle wordBounds = TextMeasurer.MeasureBounds(word, textOptions);

            if (wordBounds.Width <= maxWidth)
            {
                yield return word; // Word fits, return as is.
                yield break;
            }

            // Word is too long, try to split it character by character.
            var currentChunk = new StringBuilder();
            for (int i = 0; i < word.Length; i++)
            {
                char currentChar = word[i];
                // Check width if current char is added to the chunk.
                FontRectangle currentChunkWithCharBounds = TextMeasurer.MeasureBounds(currentChunk.ToString() + currentChar, textOptions);

                if (currentChunk.Length > 0 && currentChunkWithCharBounds.Width > maxWidth)
                {
                    // The chunk (without currentChar) was the largest piece that fit.
                    yield return currentChunk.ToString();
                    currentChunk.Clear(); // Start a new chunk.
                }
                currentChunk.Append(currentChar); // Add current char to the new or growing chunk.
            }

            // Yield any remaining part of the word in the current chunk.
            if (currentChunk.Length > 0)
            {
                yield return currentChunk.ToString();
            }
        }
    }
}