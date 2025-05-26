// TextUtilities.cs (in RedditVideoMaker.Core project)
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions; // Required for Regex

namespace RedditVideoMaker.Core
{
    public static class TextUtilities
    {
        /// <summary>
        /// Cleans text for Text-to-Speech by removing/replacing certain Markdown elements
        /// and normalizing whitespace for better pronunciation.
        /// </summary>
        /// <param name="inputText">The text to clean.</param>
        /// <returns>Cleaned text suitable for TTS.</returns>
        public static string CleanTextForTts(string inputText)
        {
            if (string.IsNullOrWhiteSpace(inputText))
            {
                return string.Empty;
            }

            string text = inputText;

            // Remove or replace common Markdown:
            // 1. Links: [link text](url) -> link text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");

            // 2. Emphasis: *italic* or _italic_ -> italic, **bold** or __bold__ -> bold, ***bold italic*** -> bold italic
            //    We'll remove the asterisks/underscores. More complex emphasis might need more rules.
            text = Regex.Replace(text, @"(?<=[^\\])\*\*\*(.*?)\*\*\*", "$1"); // ***bold italic***
            text = Regex.Replace(text, @"(?<=[^\\])\*\*(.*?)\*\*", "$1");   // **bold**
            text = Regex.Replace(text, @"(?<=[^\\])\*(.*?)\*", "$1");     // *italic*
            text = Regex.Replace(text, @"(?<=[^\\])__(.*?)__", "$1");   // __bold__ (like Markdown)
            text = Regex.Replace(text, @"(?<=[^\\])_(.*?)_", "$1");     // _italic_ (like Markdown)

            // 3. Strikethrough: ~~text~~ -> text
            text = Regex.Replace(text, @"~~(.*?)~~", "$1");

            // 4. Code blocks (inline `` and fenced ```) - remove backticks, keep content for now
            //    For fenced code blocks, we might just take the content and replace newlines.
            //    This is a simple removal of backticks. Multi-line code blocks are harder.
            text = text.Replace("`", "");

            // 5. Blockquotes: > text -> text (remove leading '>')
            text = Regex.Replace(text, @"^\s*>\s*", "", RegexOptions.Multiline);

            // 6. Headers: # text, ## text etc. -> text (remove leading '#')
            text = Regex.Replace(text, @"^\s*#+\s*", "", RegexOptions.Multiline);

            // 7. Horizontal rules: ---, ***, ___ -> replace with a pause (e.g., period and space)
            text = Regex.Replace(text, @"^\s*(\*\s*){3,}\s*$", ". ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*(-\s*){3,}\s*$", ". ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*(_\s*){3,}\s*$", ". ", RegexOptions.Multiline);

            // Normalize line breaks and whitespace:
            // Replace multiple line breaks with a single period and space (for a pause)
            // This helps TTS engines create a more natural pause than just spaces.
            text = Regex.Replace(text, @"(\r\n|\r|\n){2,}", ". ");
            // Replace single line breaks (that aren't already part of a sentence end) with a space
            text = Regex.Replace(text, @"(?<!\.)(\r\n|\r|\n)(?!\s*\.)", " ");

            // Replace multiple spaces with a single space
            text = Regex.Replace(text, @"\s{2,}", " ");

            // Remove leading/trailing whitespace
            text = text.Trim();

            // Optional: Add a period if the text doesn't end with punctuation, for a clean TTS stop.
            if (!string.IsNullOrEmpty(text) && !(".?!".Contains(text.Last())))
            {
                text += ".";
            }

            return text;
        }

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
            float currentY = 0f;

            var textOptions = new RichTextOptions(font)
            {
                LineSpacing = lineSpacing,
                Dpi = 72f
            };

            FontRectangle singleLineBounds = TextMeasurer.MeasureBounds("Xg", textOptions); // Use "Xg" for better height estimate
            float estimatedLineHeight = singleLineBounds.Height > 0 ? singleLineBounds.Height : font.Size;
            if (estimatedLineHeight <= 0) estimatedLineHeight = font.Size > 0 ? font.Size : 12f;

            foreach (var word in words)
            {
                string testLine = currentLineText.Length > 0 ? currentLineText.ToString() + " " + word : word;
                FontRectangle wordBoundsOnLine = TextMeasurer.MeasureBounds(testLine, new RichTextOptions(font) { Dpi = 72f });

                if (wordBoundsOnLine.Width > maxWidth && currentLineText.Length > 0)
                {
                    if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0)
                    {
                        pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                        currentPageText.Clear();
                        currentY = 0;
                    }
                    currentPageText.Append(currentLineText.ToString().Trim() + "\n");
                    currentY += estimatedLineHeight;
                    currentLineText.Clear();

                    currentLineText.Append(word);
                }
                else if (wordBoundsOnLine.Width > maxWidth && currentLineText.Length == 0)
                {
                    // This word itself is too long for a line (even after pre-splitting via SplitWordIfTooLong)
                    // This indicates SplitWordIfTooLong might need refinement or this word is extremely long.
                    // For now, add it and let it overflow or be handled by image rendering if possible.
                    if (currentY + estimatedLineHeight > maxHeight && currentPageText.Length > 0)
                    {
                        pages.Add(currentPageText.ToString().TrimEnd('\n', ' '));
                        currentPageText.Clear();
                        currentY = 0;
                    }
                    currentPageText.Append(word + "\n");
                    currentY += estimatedLineHeight;
                }
                else
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

            return pages.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
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
            // Try to split character by character if a single word is too long
            for (int i = 0; i < word.Length; i++)
            {
                char currentChar = word[i];
                FontRectangle currentChunkWithCharBounds = TextMeasurer.MeasureBounds(currentChunk.ToString() + currentChar, textOptions);

                if (currentChunk.Length > 0 && currentChunkWithCharBounds.Width > maxWidth)
                {
                    yield return currentChunk.ToString(); // Return the part that fit
                    currentChunk.Clear(); // Start a new chunk
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
