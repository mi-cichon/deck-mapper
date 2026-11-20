using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeckMapper;
using Xunit;

namespace DeckMapper.Tests;

public class LizardMapTests
{
    [Fact]
    public void BuiltIn_IsUnverified_AndHasKnownKeysAndMouse()
    {
        var map = LizardMap.BuiltIn;
        Assert.False(map.Verified);
        Assert.Equal("Enter", map.Of(DeckControl.A).Name);
        Assert.Equal("Esc", map.Of(DeckControl.B).Name);
        Assert.True(map.Of(DeckControl.RT).IsMouse);
        Assert.Equal("LMB", map.Of(DeckControl.RT).Name);
        Assert.Equal("RMB", map.Of(DeckControl.LT).Name);
        Assert.Equal(LizardOutputKind.None, map.Of(DeckControl.X).Kind);
        Assert.Equal(new[] { "Enter", "Esc", "Up", "Down", "Left", "Right" }, map.Keys.Select(k => k.Name));
    }

    [Fact]
    public void Arrows_AreExtended_EnterIsNot()
    {
        var map = LizardMap.BuiltIn;
        Assert.True(map.Of(DeckControl.DpadUp).Key.Extended);
        Assert.Equal(0x48, map.Of(DeckControl.DpadUp).Key.ScanCode);
        Assert.False(map.Of(DeckControl.A).Key.Extended);
        Assert.Equal(0x1C, map.Of(DeckControl.A).Key.ScanCode);
        // Enter z klawiatury numerycznej (E0 1C) to inny kod niż zwykły Enter
        Assert.NotEqual(LizardKeys.Code(0x1C, false), LizardKeys.Code(0x1C, true));
    }

    [Fact]
    public void SerializeAndParse_RoundTrip()
    {
        var original = new LizardMap(new Dictionary<DeckControl, LizardOutput>
        {
            [DeckControl.A] = LizardOutput.OfKey(new KeyDef(0x1C, false)),
            [DeckControl.DpadLeft] = LizardOutput.OfKey(new KeyDef(0x4B, true)),
            [DeckControl.View] = LizardOutput.OfKey(new KeyDef(0x7E, true)), // scancode bez nazwy
            [DeckControl.RT] = LizardOutput.OfMouse("LMB"),
            [DeckControl.X] = LizardOutput.None,
        }, verified: true, detectedAt: new DateTime(2026, 11, 20, 12, 0, 0));

        var errors = new List<string>();
        var parsed = LizardMap.Parse(original.Serialize(), errors);

        Assert.Empty(errors);
        Assert.NotNull(parsed);
        Assert.True(parsed!.Verified);
        Assert.Equal(original.DetectedAt, parsed.DetectedAt);
        Assert.Equal("Enter", parsed.Of(DeckControl.A).Name);
        Assert.Equal("Left", parsed.Of(DeckControl.DpadLeft).Name);
        Assert.Equal("sc:e07E", parsed.Of(DeckControl.View).Name);
        Assert.Equal(new KeyDef(0x7E, true), parsed.Of(DeckControl.View).Key);
        Assert.True(parsed.Of(DeckControl.RT).IsMouse);
        Assert.True(parsed.Outputs.ContainsKey(DeckControl.X)); // „sprawdzone, nic” zostaje zapamiętane
    }

    [Fact]
    public void Parse_SkipsBadEntries_ButKeepsGoodOnes()
    {
        var json = """
        // komentarze są dozwolone
        { "verified": true, "controls": { "A": "Enter", "Bogus": "Esc", "B": "NieMaTakiego", "RT": "mouse:XMB", "Y": "escape", } }
        """;
        var errors = new List<string>();
        var map = LizardMap.Parse(json, errors);

        Assert.NotNull(map);
        Assert.Equal(3, errors.Count);
        Assert.Equal("Enter", map!.Of(DeckControl.A).Name);
        Assert.Equal("Esc", map.Of(DeckControl.Y).Name); // alias → nazwa kanoniczna
        Assert.Equal(LizardOutputKind.None, map.Of(DeckControl.B).Kind);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"verified\": true }")]
    public void Parse_Broken_ReturnsNull(string json)
    {
        var errors = new List<string>();
        Assert.Null(LizardMap.Parse(json, errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void LoadOrBuiltIn_MissingOrBrokenFile_FallsBackToBuiltIn()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deckmapper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, LizardMap.FileName);
            var errors = new List<string>();

            var missing = LizardMap.LoadOrBuiltIn(path, errors, out var source);
            Assert.Same(LizardMap.BuiltIn, missing);
            Assert.Contains("niezweryfikowana", source);

            File.WriteAllText(path, "{ zepsute");
            var broken = LizardMap.LoadOrBuiltIn(path, errors, out source);
            Assert.Same(LizardMap.BuiltIn, broken);
            Assert.Contains("błąd", source);

            new LizardMap(new Dictionary<DeckControl, LizardOutput> { [DeckControl.A] = LizardOutput.OfKey(new KeyDef(0x1C, false)) },
                true, DateTime.Now).Save(path);
            var loaded = LizardMap.LoadOrBuiltIn(path, errors, out source);
            Assert.True(loaded.Verified);
            Assert.Equal(LizardMap.FileName, source);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("none", "None")]
    [InlineData("", "None")]
    [InlineData("mouse:lmb", "MouseButton")]
    [InlineData("Enter", "Key")]
    [InlineData("sc:1C", "Key")]
    [InlineData("SC:E048", "Key")]
    public void Output_Parse(string text, string kind)
    {
        Assert.True(LizardOutput.TryParse(text, out var output, out _));
        Assert.Equal(kind, output.Kind.ToString());
    }

    [Fact]
    public void ScancodeNames_ResolveToCanonicalName()
    {
        Assert.True(LizardOutput.TryParse("sc:e048", out var up, out _));
        Assert.Equal("Up", up.Name);
        Assert.False(LizardOutput.TryParse("sc:zz", out _, out _));
        Assert.False(LizardOutput.TryParse("sc:", out _, out _));
    }
}
