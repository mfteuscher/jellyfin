using System.IO;
using MediaBrowser.Model.Lyrics;
using MediaBrowser.Providers.Lyric;
using Xunit;

namespace Jellyfin.Providers.Tests.Lyrics;

public static class TtmlLyricParserTests
{
    [Fact]
    public static void ParseTtmlCues()
    {
        var parser = new TtmlLyricParser();
        var fileContents = File.ReadAllText(Path.Combine("Test Data", "Lyrics", "fleetwood-mac-storms.ttml"));
        var parsed = parser.ParseLyrics(new LyricFile("fleetwood-mac-storms.ttml", fileContents));

        Assert.NotNull(parsed);
        Assert.Equal(40, parsed.Lyrics.Count);

        var line1 = parsed.Lyrics[0];
        Assert.Equal("Every night that goes between", line1.Text);
        Assert.NotNull(line1.Cues);
        Assert.Equal(5, line1.Cues.Count);
        Assert.Equal(130220000, line1.Cues[0].Start);
        Assert.Equal(138380000, line1.Cues[0].End);
        Assert.Equal(0, line1.Cues[0].Position);
        Assert.Equal(5, line1.Cues[0].EndPosition);
        Assert.Equal(6, line1.Cues[1].Position);
        Assert.Equal(11, line1.Cues[1].EndPosition);
        Assert.Equal(12, line1.Cues[2].Position);

        var line5 = parsed.Lyrics[4];
        Assert.Equal("Every night you do not come", line5.Text);
        Assert.NotNull(line5.Cues);
        Assert.Equal(6, line5.Cues.Count);
        Assert.Equal(434680000, line5.Cues[2].Start);
        Assert.Equal(441000000, line5.Cues[2].End);

        var lastLine = parsed.Lyrics[^1];
        Assert.Equal("Could save us", lastLine.Text);
        Assert.NotNull(lastLine.Cues);
        Assert.Equal(3, lastLine.Cues.Count);
        Assert.Equal(3175520000, lastLine.Cues[^1].Start);
        Assert.Equal(13, lastLine.Cues[^1].EndPosition);
        Assert.Equal(3185120000, lastLine.Cues[^1].End);
    }
}
