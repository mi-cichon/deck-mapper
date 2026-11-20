using System.Collections.Generic;
using System.Linq;
using DeckMapper;
using Xunit;

namespace DeckMapper.Tests;

public class ConfigLizardTests
{
    static RawConfig Parse(string json)
    {
        var errors = new List<string>();
        var raw = ConfigLoader.ParseRaw(json, errors);
        Assert.Empty(errors);
        return raw!;
    }

    [Fact]
    public void LegacyProfile_WithoutLizardSection_GetsDefaults()
    {
        var raw = Parse("""{ "disableLizardButtons": true, "bindings": { "A": "Space" } }""");

        var compiled = ConfigLoader.Compile(raw, new List<string>());
        Assert.True(compiled.Lizard.FilterKeys);
        Assert.True(compiled.Lizard.SwallowAuto);

        ConfigLoader.Normalize(raw);
        Assert.NotNull(raw.Lizard);
        Assert.True(raw.Lizard!.FilterKeys);
        Assert.True(raw.Lizard.Swallow.IsAuto);
        Assert.Null(raw.DisableLizardButtons);
    }

    [Fact]
    public void Migration_DropsDisableLizardButtons_AndWritesLizardSection()
    {
        var raw = Parse("""{ "disableLizardButtons": true }""");
        ConfigLoader.Normalize(raw);
        var json = ConfigLoader.Serialize(raw);

        Assert.DoesNotContain("\"disableLizardButtons\"", json);
        Assert.Contains("\"lizard\"", json);
        Assert.Contains("\"swallow\": \"auto\"", json);
    }

    [Fact]
    public void SwallowList_RoundTrips()
    {
        var raw = Parse("""{ "lizard": { "filterKeys": false, "swallow": ["Enter", "Esc"] } }""");
        ConfigLoader.Normalize(raw);
        var again = Parse(ConfigLoader.Serialize(raw));

        Assert.False(again.Lizard!.FilterKeys);
        Assert.False(again.Lizard.Swallow.IsAuto);
        Assert.Equal(new[] { "Enter", "Esc" }, again.Lizard.Swallow.Keys);

        var compiled = ConfigLoader.Compile(again, new List<string>());
        Assert.False(compiled.Lizard.FilterKeys);
        Assert.Equal(new[] { "Enter", "Esc" }, compiled.Lizard.SwallowKeys);
    }

    [Theory]
    [InlineData("\"auto\"", true)]
    [InlineData("\"AUTO\"", true)]
    [InlineData("null", true)]
    [InlineData("[]", false)]
    public void SwallowForms(string swallow, bool auto)
    {
        var raw = Parse($$"""{ "lizard": { "swallow": {{swallow}} } }""");
        Assert.Equal(auto, raw.Lizard!.Swallow?.IsAuto ?? true);
    }

    [Theory]
    [InlineData("\"wszystko\"")]
    [InlineData("42")]
    [InlineData("[1, 2]")]
    public void SwallowInvalid_IsJsonError(string swallow)
    {
        var errors = new List<string>();
        Assert.Null(ConfigLoader.ParseRaw($$"""{ "lizard": { "swallow": {{swallow}} } }""", errors));
        Assert.Single(errors);
    }

    [Fact]
    public void UnknownSwallowKey_IsReportedAndSkipped()
    {
        var raw = Parse("""{ "lizard": { "swallow": ["Enter", "Klawisz"] } }""");
        var errors = new List<string>();
        var compiled = ConfigLoader.Compile(raw, errors);

        Assert.Equal(new[] { "Enter" }, compiled.Lizard.SwallowKeys);
        Assert.Single(errors);
        Assert.Contains("Klawisz", errors[0]);
    }

    [Fact]
    public void DefaultProfile_HasFilterOn_AndNoLegacyOption()
    {
        Assert.DoesNotContain("disableLizardButtons", DefaultConfig.Json);
        var raw = ConfigLoader.DefaultRaw();
        Assert.True(raw.Lizard!.FilterKeys);
        Assert.True(raw.Lizard.Swallow.IsAuto);
        var errors = new List<string>();
        ConfigLoader.Compile(raw, errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void EmptyProfile_GetsFilterOnByDefault()
    {
        var raw = ConfigLoader.EmptyRaw();
        Assert.True(raw.Lizard!.FilterKeys);
        Assert.True(raw.Lizard.Swallow.IsAuto);
    }

    [Fact]
    public void Clone_KeepsLizardSettings()
    {
        var raw = Parse("""{ "lizard": { "filterKeys": false, "swallow": ["Up"] } }""");
        ConfigLoader.Normalize(raw);
        var copy = ConfigLoader.Clone(raw);
        Assert.False(copy.Lizard!.FilterKeys);
        Assert.Equal(new[] { "Up" }, copy.Lizard.Swallow.Keys);
        copy.Lizard.Swallow.Keys.Add("Down");
        Assert.Single(raw.Lizard!.Swallow.Keys); // głęboka kopia
    }

    [Fact]
    public void KeyNames_NameOf_PrefersFirstName()
    {
        Assert.True(KeyNames.TryGet("Escape", out var esc));
        Assert.Equal("Esc", KeyNames.NameOf(esc));
        Assert.True(KeyNames.TryGet("Return", out var enter));
        Assert.Equal("Enter", KeyNames.NameOf(enter));
        Assert.Null(KeyNames.NameOf(new KeyDef(0x7E, true)));
    }
}
