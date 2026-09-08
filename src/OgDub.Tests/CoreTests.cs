using OgDub.Core;
using Xunit;

namespace OgDub.Tests;

public sealed class LibraryPathsTests
{
    [Fact]
    public void Default_library_is_under_documents()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var lib = LibraryPaths.DefaultLibrary();
        Assert.StartsWith(docs, lib, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OG Digital Designs", lib, StringComparison.Ordinal);
        Assert.Contains("OG Dub", lib, StringComparison.Ordinal);
    }

    [Fact]
    public void File_inside_library_is_under_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "og-dub-lib-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "tape.wav");
        Assert.True(LibraryPaths.IsUnderLibrary(file, root));
        Assert.False(LibraryPaths.IsUnderLibrary(Path.GetTempPath(), root));
    }
}

public sealed class CassetteNamingTests
{
    [Fact]
    public void Sanitize_strips_invalid_name_chars()
    {
        Assert.Equal("Chrome 123", CassetteNaming.Sanitize(@"Chrome<>123"));
    }

    [Fact]
    public void Empty_name_becomes_Station()
    {
        Assert.Equal("Station", CassetteNaming.Sanitize("   "));
    }
}

public sealed class TapeSideTests
{
    [Fact]
    public void C60_side_is_30_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), TapeSide.C60SideA);
    }

    [Fact]
    public void Remaining_does_not_go_negative()
    {
        Assert.Equal(TimeSpan.Zero, TapeSide.Remaining(TimeSpan.FromHours(1), TapeSide.C60SideA));
    }

    [Fact]
    public void Lcd_pads_minutes()
    {
        Assert.Equal("05:07", TapeSide.LcdCountdown(TimeSpan.FromSeconds(5 * 60 + 7)));
    }
}

public sealed class WavConcatTests
{
    [Fact]
    public void Dub_inserts_gap_between_matching_pcm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "og-dub-wav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var format = new WavPcm
            {
                AudioFormat = 1,
                Channels = 1,
                SampleRate = 8000,
                BitsPerSample = 16,
                Data = new byte[8000]
            };
            var a = Path.Combine(dir, "a.wav");
            var b = Path.Combine(dir, "b.wav");
            var dest = Path.Combine(dir, "dub.wav");
            using (var fa = File.Create(a))
                WavPcm.Write(fa, format, format.Data);
            using (var fb = File.Create(b))
                WavPcm.Write(fb, format, format.Data);

            WavConcat.Dub([a, b], dest, TimeSpan.FromSeconds(1));
            using var stream = File.OpenRead(dest);
            var result = WavPcm.Read(stream);
            Assert.Equal(format.Data.Length * 2 + format.AverageBytesPerSecond, result.Data.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dub_refuses_mismatched_formats()
    {
        var dir = Path.Combine(Path.GetTempPath(), "og-dub-wav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var aFmt = new WavPcm { AudioFormat = 1, Channels = 1, SampleRate = 8000, BitsPerSample = 16, Data = new byte[16] };
            var bFmt = new WavPcm { AudioFormat = 1, Channels = 2, SampleRate = 8000, BitsPerSample = 16, Data = new byte[16] };
            var a = Path.Combine(dir, "a.wav");
            var b = Path.Combine(dir, "b.wav");
            using (var fa = File.Create(a))
                WavPcm.Write(fa, aFmt, aFmt.Data);
            using (var fb = File.Create(b))
                WavPcm.Write(fb, bFmt, bFmt.Data);

            var dest = Path.Combine(dir, "dub.wav");
            Assert.Throws<InvalidOperationException>(() => WavConcat.Dub([a, b], dest, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class TermsTests
{
    [Fact]
    public void Terms_file_version_matches_service_constant()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Legal", "TermsOfUse.txt");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.Contains("Version: " + ProductInfo.TermsVersion, text, StringComparison.Ordinal);
        Assert.Contains("not a stream ripper", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_is_asInvoker()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "app.manifest");
        Assert.True(File.Exists(path));
        var xml = File.ReadAllText(path);
        Assert.Contains("asInvoker", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("requireAdministrator", xml, StringComparison.Ordinal);
    }
}
