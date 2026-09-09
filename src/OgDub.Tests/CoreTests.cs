using System.Buffers.Binary;
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

    [Fact]
    public void Track_jcard_includes_artist_and_minutes()
    {
        var when = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.FromHours(10));
        var text = CassetteNaming.JCardFromTrack("Nightcall", "Kavinsky", when, TimeSpan.FromMinutes(12));
        Assert.StartsWith("Nightcall · Kavinsky · ", text, StringComparison.Ordinal);
        Assert.EndsWith("12m", text);
    }

    [Fact]
    public void Track_jcard_omits_blank_artist()
    {
        var when = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.FromHours(10));
        var text = CassetteNaming.JCardFromTrack("Nightcall", "  ", when, TimeSpan.FromMinutes(3));
        Assert.DoesNotContain("Kavinsky", text, StringComparison.Ordinal);
        Assert.StartsWith("Nightcall · ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nightcall ·  ·", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapse_strips_newlines()
    {
        Assert.Equal("One Two", CassetteNaming.Collapse(" One\r\nTwo "));
        Assert.Equal("", CassetteNaming.Collapse("   "));
    }
}

public sealed class SmtcSessionMatchTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("chrome", "chrome")]
    [InlineData("MSEdge", "msedge")]
    public void Matches_aimed_process(string aumid, string processName)
    {
        Assert.True(SmtcSessionMatch.Matches(aumid, processName));
    }

    [Theory]
    [InlineData("Spotify.exe", "Discord")]
    [InlineData("", "Spotify")]
    [InlineData("Spotify.exe", "")]
    [InlineData("Spotify.exe", "   ")]
    public void Rejects_other_or_empty(string aumid, string processName)
    {
        Assert.False(SmtcSessionMatch.Matches(aumid, processName));
    }
}

public sealed class CrateStoreTests
{
    [Fact]
    public void Artwork_path_stays_under_library()
    {
        var root = Path.Combine(Path.GetTempPath(), "og-dub-crate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new CrateStore(root);
            var rec = new CassetteRecord { ArtworkFileName = @"..\..\Windows\evil.png" };
            var path = store.ArtworkPath(rec);
            Assert.Equal("evil.png", Path.GetFileName(path));
            Assert.True(LibraryPaths.IsUnderLibrary(path, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Empty_artwork_name_is_blank_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "og-dub-crate-" + Guid.NewGuid().ToString("N"));
        var store = new CrateStore(root);
        Assert.Equal("", store.ArtworkPath(new CassetteRecord()));
        Assert.Equal("", store.ArtworkPath(new CassetteRecord { ArtworkFileName = ".." }));
        Assert.Equal("", store.SideBPath(new CassetteRecord()));
        Assert.Equal("", store.SideBPath(new CassetteRecord { SideBWavFileName = ".." }));
    }

    [Fact]
    public void Side_b_path_stays_under_library()
    {
        var root = Path.Combine(Path.GetTempPath(), "og-dub-crate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new CrateStore(root);
            var rec = new CassetteRecord { SideBWavFileName = @"..\..\Windows\evil.wav" };
            var path = store.SideBPath(rec);
            Assert.Equal("evil.wav", Path.GetFileName(path));
            Assert.True(LibraryPaths.IsUnderLibrary(path, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Old_crate_json_has_empty_markers()
    {
        var root = Path.Combine(Path.GetTempPath(), "og-dub-crate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new CrateStore(root);
            File.WriteAllText(store.IndexPath, """{"cassettes":[{"Id":"a","Title":"Old","WavFileName":"a.wav"}]}""");
            var loaded = store.Load();
            Assert.Single(loaded);
            Assert.Empty(loaded[0].Markers);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

public sealed class TapeCounterTests
{
    [Fact]
    public void Zero_elapsed_is_000()
    {
        Assert.Equal(0, TapeCounter.FromElapsed(TimeSpan.Zero));
        Assert.Equal(0, TapeCounter.FromElapsed(TimeSpan.FromSeconds(-1)));
        Assert.Equal("000", TapeCounter.Lcd(0));
    }

    [Fact]
    public void Side_maps_to_999()
    {
        Assert.Equal(999, TapeCounter.FromElapsed(TapeSide.C60SideA));
        Assert.Equal(999, TapeCounter.FromElapsed(TimeSpan.FromHours(1)));
        Assert.Equal("999", TapeCounter.Lcd(1200));
    }

    [Fact]
    public void Halfway_is_500()
    {
        Assert.Equal(500, TapeCounter.FromElapsed(TimeSpan.FromMinutes(15)));
        Assert.Equal(8, TapeCounter.FromElapsed(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void Next_mark_wraps()
    {
        var marks = new List<TapeMarker>
        {
            new() { Counter = 10, OffsetSeconds = 1, Label = "010" },
            new() { Counter = 50, OffsetSeconds = 5, Label = "050" }
        };
        Assert.Null(TapeMarkers.Next([], 0));
        Assert.Equal("050", TapeMarkers.Next(marks, 1)?.Label);
        Assert.Equal("010", TapeMarkers.Next(marks, 5)?.Label);
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

public sealed class InstantReplayTests
{
    [Fact]
    public void Window_is_15_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), InstantReplay.Window);
    }

    [Fact]
    public void Capacity_aligns_to_block()
    {
        Assert.Equal(15 * 8, InstantReplay.CapacityBytes(8, 2));
        Assert.Equal(0, InstantReplay.CapacityBytes(0, 2));
        Assert.Equal(0, InstantReplay.CapacityBytes(8, 0));
    }
}

public sealed class PcmRingBufferTests
{
    [Fact]
    public void Snapshot_is_empty_before_writes()
    {
        var ring = new PcmRingBuffer(8, 1);
        Assert.Empty(ring.Snapshot());
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void Snapshot_returns_bytes_in_order_before_wrap()
    {
        var ring = new PcmRingBuffer(8, 1);
        ring.Write([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, ring.Snapshot());
    }

    [Fact]
    public void Snapshot_keeps_the_newest_bytes_after_wrap()
    {
        var ring = new PcmRingBuffer(4, 1);
        ring.Write([1, 2, 3, 4, 5]);
        Assert.Equal(new byte[] { 2, 3, 4, 5 }, ring.Snapshot());
        Assert.Equal(4, ring.Count);
    }

    [Fact]
    public void Write_drops_a_partial_block()
    {
        var ring = new PcmRingBuffer(8, 2);
        ring.Write([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2 }, ring.Snapshot());
    }

    [Fact]
    public void Filled_duration_uses_byte_rate()
    {
        var ring = new PcmRingBuffer(100, 1);
        ring.Write([1, 2, 3, 4]);
        Assert.Equal(TimeSpan.FromSeconds(2), ring.FilledDuration(2));
        Assert.Equal(TimeSpan.Zero, ring.FilledDuration(0));
    }
}

public sealed class SmtcSongChangeTests
{
    [Fact]
    public void Same_title_is_not_a_new_song()
    {
        Assert.False(SmtcSongChange.IsNewSong("Nightcall", "nightcall"));
        Assert.False(SmtcSongChange.IsNewSong("Nightcall", " Nightcall "));
    }

    [Fact]
    public void Different_title_is_a_new_song()
    {
        Assert.True(SmtcSongChange.IsNewSong("Nightcall", "Odd Look"));
    }

    [Fact]
    public void Blank_titles_do_not_split()
    {
        Assert.False(SmtcSongChange.IsNewSong(null, "Nightcall"));
        Assert.False(SmtcSongChange.IsNewSong("Nightcall", "  "));
        Assert.False(SmtcSongChange.IsNewSong("", ""));
    }
}

public sealed class SafetyPadTests
{
    [Fact]
    public void Linear_gain_is_minus_12_dB()
    {
        var expected = (float)Math.Pow(10.0, -12.0 / 20.0);
        Assert.Equal(expected, SafetyPad.Linear);
        Assert.InRange(SafetyPad.Linear, 0.2511f, 0.2513f);
    }

    [Fact]
    public void Float_sample_is_scaled()
    {
        var buf = new byte[4];
        BitConverter.TryWriteBytes(buf, 1f);
        SafetyPad.ApplyIeeeFloat32(buf);
        Assert.Equal(SafetyPad.Linear, BitConverter.ToSingle(buf), 5);
    }

    [Fact]
    public void Pcm16_sample_is_scaled()
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, 10_000);
        SafetyPad.ApplyPcm16(buf);
        var got = BinaryPrimitives.ReadInt16LittleEndian(buf);
        Assert.Equal((short)Math.Round(10_000 * (double)SafetyPad.Linear), got);
    }

    [Fact]
    public void Short_buffer_is_left_alone()
    {
        var buf = new byte[] { 1, 2 };
        SafetyPad.ApplyIeeeFloat32(buf);
        Assert.Equal(new byte[] { 1, 2 }, buf);
    }
}

public sealed class BwfFileTests
{
    [Fact]
    public void Bext_stamp_is_iso_date()
    {
        var when = new DateTimeOffset(2026, 9, 9, 12, 30, 0, TimeSpan.FromHours(10));
        var (date, time) = CassetteNaming.BextStamp(when);
        Assert.Equal(10, date.Length);
        Assert.Equal(8, time.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", date);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", time);
    }

    [Fact]
    public void Hundredths_maps_lufs_and_unknown()
    {
        Assert.Equal(-2300, BwfFile.ToHundredths(-23));
        Assert.Equal(BwfFile.UnknownLoudness, BwfFile.ToHundredths(null));
        Assert.Equal(BwfFile.UnknownLoudness, BwfFile.ToHundredths(double.NaN));
    }

    [Fact]
    public void Append_bext_once_and_write_originator()
    {
        var dir = Path.Combine(Path.GetTempPath(), "og-dub-bwf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "take.wav");
            var pcm = new WavPcm { AudioFormat = 1, Channels = 1, SampleRate = 8000, BitsPerSample = 16, Data = new byte[32] };
            using (var created = File.Create(path))
                WavPcm.Write(created, pcm, pcm.Data);

            using (var before = File.OpenRead(path))
                Assert.False(BwfFile.HasBext(before));

            var first = new FileInfo(path).Length;
            Assert.True(BwfFile.TryAppend(path, new BextFields
            {
                Description = "Nightcall",
                OriginatorReference = "abc",
                OriginatedAt = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.FromHours(10)),
                LoudnessLufs = -16.4
            }));
            var tagged = new FileInfo(path).Length;
            Assert.True(tagged > first);

            using (var after = File.OpenRead(path))
                Assert.True(BwfFile.HasBext(after));

            var bytes = File.ReadAllBytes(path);
            var ascii = System.Text.Encoding.ASCII.GetString(bytes);
            Assert.Contains("OG Digital Designs", ascii, StringComparison.Ordinal);
            Assert.Contains("Nightcall", ascii, StringComparison.Ordinal);
            Assert.Contains("bext", ascii, StringComparison.Ordinal);

            Assert.True(BwfFile.TryAppend(path, new BextFields { Description = "again", OriginatedAt = DateTimeOffset.Now }));
            Assert.Equal(tagged, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_cue_once_with_label()
    {
        var dir = Path.Combine(Path.GetTempPath(), "og-dub-cue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "take.wav");
            var pcm = new WavPcm { AudioFormat = 1, Channels = 1, SampleRate = 8000, BitsPerSample = 16, Data = new byte[32] };
            using (var created = File.Create(path))
                WavPcm.Write(created, pcm, pcm.Data);

            using (var before = File.OpenRead(path))
                Assert.False(BwfFile.HasCue(before));

            var markers = new List<TapeMarker>
            {
                new() { Counter = 8, OffsetSeconds = 1.5, Label = "008" }
            };
            var first = new FileInfo(path).Length;
            Assert.True(BwfFile.TryAppendCues(path, markers, 8000));
            var tagged = new FileInfo(path).Length;
            Assert.True(tagged > first);

            using (var after = File.OpenRead(path))
                Assert.True(BwfFile.HasCue(after));

            var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path));
            Assert.Contains("cue ", ascii, StringComparison.Ordinal);
            Assert.Contains("adtl", ascii, StringComparison.Ordinal);
            Assert.Contains("008", ascii, StringComparison.Ordinal);

            Assert.True(BwfFile.TryAppendCues(path, markers, 8000));
            Assert.Equal(tagged, new FileInfo(path).Length);
            Assert.True(BwfFile.TryAppendCues(path, [], 8000));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class Loudness1770Tests
{
    [Fact]
    public void Silence_is_at_or_below_absolute_gate()
    {
        var pcm = new WavPcm
        {
            AudioFormat = 1,
            Channels = 1,
            SampleRate = 48000,
            BitsPerSample = 16,
            Data = new byte[48000 * 2]
        };
        var result = Loudness1770.Measure(pcm);
        Assert.NotNull(result.LoudnessLufs);
        Assert.True(result.LoudnessLufs <= Loudness1770.AbsoluteGateLufs);
        Assert.True(result.PeakDbfs <= -90);
    }

    [Fact]
    public void Sine_has_finite_loudness()
    {
        var sr = 48000;
        var data = new byte[sr * 2];
        for (var i = 0; i < sr; i++)
        {
            var sample = (short)Math.Round(Math.Sin(2 * Math.PI * 1000 * i / sr) * 16000);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), sample);
        }

        var pcm = new WavPcm { AudioFormat = 1, Channels = 1, SampleRate = sr, BitsPerSample = 16, Data = data };
        var result = Loudness1770.Measure(pcm);
        Assert.NotNull(result.LoudnessLufs);
        Assert.InRange(result.LoudnessLufs.Value, -30, -5);
        Assert.False(string.IsNullOrEmpty(Loudness1770.Lcd(result.LoudnessLufs)));
        Assert.Contains("LUFS", Loudness1770.Lcd(result.LoudnessLufs), StringComparison.Ordinal);
    }
}

public sealed class TapeLeaderTests
{
    [Fact]
    public void Three_one_second_beats()
    {
        Assert.Equal(3, TapeLeader.Beats);
        Assert.Equal(TimeSpan.FromSeconds(1), TapeLeader.Beat);
        Assert.Equal("3", TapeLeader.Lcd(3));
        Assert.Equal("1", TapeLeader.Lcd(1));
        Assert.Equal("", TapeLeader.Lcd(0));
    }
}

public sealed class ClipLightTests
{
    [Fact]
    public void Lights_at_threshold()
    {
        Assert.False(ClipLight.IsOn(0));
        Assert.False(ClipLight.IsOn(0.98f));
        Assert.True(ClipLight.IsOn(ClipLight.Threshold));
        Assert.True(ClipLight.IsOn(1f));
    }
}
