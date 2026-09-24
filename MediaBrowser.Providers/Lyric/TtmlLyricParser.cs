using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Lyrics;

namespace MediaBrowser.Providers.Lyric;

/// <summary>
/// TTML Lyric Parser.
/// </summary>
public class TtmlLyricParser : ILyricParser
{
    private static readonly string[] _supportedMediaTypes = [".ttml"];

    private static readonly XmlReaderSettings _xmlReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true
    };

    /// <inheritdoc />
    public string Name => "TtmlLyricProvider";

    /// <summary>
    /// Gets the priority.
    /// </summary>
    /// <value>The priority.</value>
    public ResolverPriority Priority => ResolverPriority.Fourth;

    /// <inheritdoc />
    public LyricDto? ParseLyrics(LyricFile lyrics)
    {
        if (!_supportedMediaTypes.Contains(Path.GetExtension(lyrics.Name.AsSpan()), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        XDocument document;

        try
        {
            using var stringReader = new StringReader(lyrics.Content);
            using var xmlReader = XmlReader.Create(stringReader, _xmlReaderSettings);
            document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // Failed to parse, return null so the next parser will be tried
            return null;
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "tt")
        {
            return null;
        }

        // Use the namespace of the root element so both TTML and legacy DFXP namespaces are supported.
        var ns = root.Name.Namespace;
        var body = root.Element(ns + "body");
        if (body is null)
        {
            return null;
        }

        List<LyricLine> lyricList = [];
        foreach (var paragraph in body.Descendants(ns + "p"))
        {
            var text = new StringBuilder();
            var cues = new List<LyricLineCue>();
            AppendContent(paragraph, ns, text, cues);

            // Drop the trailing space left by whitespace collapsing, cue positions never include it
            if (text.Length > 0 && text[^1] == ' ')
            {
                text.Length--;
            }

            if (text.Length == 0)
            {
                continue;
            }

            var start = ParseTime(paragraph.Attribute("begin")?.Value);
            if (start is null && cues.Count > 0)
            {
                start = cues[0].Start;
            }

            lyricList.Add(new LyricLine(text.ToString(), start, cues));
        }

        if (lyricList.Count == 0)
        {
            return null;
        }

        return new LyricDto { Lyrics = lyricList };
    }

    /// <summary>
    /// Appends the text of an element to the line and creates cues for its timed spans.
    /// Nested spans, such as background vocals, are flattened into the line.
    /// </summary>
    private static void AppendContent(XElement element, XNamespace ns, StringBuilder text, List<LyricLineCue> cues)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
            {
                AppendText(text, textNode.Value);
                continue;
            }

            if (node is not XElement child)
            {
                continue;
            }

            if (child.Name == ns + "br")
            {
                AppendText(text, " ");
            }
            else if (child.Name == ns + "span")
            {
                if (child.HasElements)
                {
                    AppendContent(child, ns, text, cues);
                    continue;
                }

                var position = text.Length;
                AppendText(text, child.Value);
                var endPosition = text.Length;

                // Exclude the collapsed whitespace surrounding the span's text from the cue
                if (position < endPosition && text[position] == ' ')
                {
                    position++;
                }

                if (position < endPosition && text[endPosition - 1] == ' ')
                {
                    endPosition--;
                }

                var start = ParseTime(child.Attribute("begin")?.Value);
                if (start is null || position >= endPosition)
                {
                    continue;
                }

                var end = ParseTime(child.Attribute("end")?.Value);
                if (end is null && ParseTime(child.Attribute("dur")?.Value) is { } duration)
                {
                    end = start + duration;
                }

                cues.Add(new LyricLineCue(
                    position: position,
                    endPosition: endPosition,
                    start: start.Value,
                    end: end));
            }
        }
    }

    /// <summary>
    /// Appends text, collapsing whitespace to single spaces and skipping leading whitespace.
    /// </summary>
    private static void AppendText(StringBuilder text, string value)
    {
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                text.Append(c);
            }
            else if (text.Length > 0 && text[^1] != ' ')
            {
                text.Append(' ');
            }
        }
    }

    /// <summary>
    /// Parses a TTML time expression into ticks.
    /// </summary>
    /// <param name="value">The time expression.</param>
    /// <returns>The time in ticks, or null if the expression is missing or unsupported.</returns>
    private static long? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        decimal seconds;

        if (value.Contains(':', StringComparison.Ordinal))
        {
            // Clock time, e.g. "01:02:03.456".
            var parts = value.Split(':');
            if (parts.Length > 3)
            {
                return null;
            }

            seconds = 0;
            foreach (var part in parts)
            {
                if (!decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var partValue))
                {
                    return null;
                }

                seconds = (seconds * 60) + partValue;
            }
        }
        else
        {
            // Offset time, e.g. "12.3s", "100ms", "1.5m" or "0.5h".
            var (number, multiplier) = value switch
            {
                _ when value.EndsWith("ms", StringComparison.Ordinal) => (value[..^2], 0.001m),
                _ when value.EndsWith('s') => (value[..^1], 1m),
                _ when value.EndsWith('m') => (value[..^1], 60m),
                _ when value.EndsWith('h') => (value[..^1], 3600m),
                _ => (value, 1m)
            };

            if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numberValue))
            {
                return null;
            }

            seconds = numberValue * multiplier;
        }

        return (long)(seconds * TimeSpan.TicksPerSecond);
    }
}
