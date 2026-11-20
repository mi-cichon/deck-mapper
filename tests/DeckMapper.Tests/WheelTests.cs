using System;
using System.Collections.Generic;
using System.Linq;
using DeckMapper;
using Xunit;

namespace DeckMapper.Tests;

public class WheelSelectorTests
{
    [Theory]
    // 4 sloty: 0 = góra, 1 = prawo, 2 = dół, 3 = lewo
    [InlineData(0f, 1f, 4, 0)]
    [InlineData(1f, 0f, 4, 1)]
    [InlineData(0f, -1f, 4, 2)]
    [InlineData(-1f, 0f, 4, 3)]
    [InlineData(0.6f, 0.7f, 4, 0)]   // 40° od góry – jeszcze sektor 0 (±45°)
    [InlineData(0.75f, 0.6f, 4, 1)]  // 51° – już sektor 1
    [InlineData(-0.1f, 1f, 4, 0)]    // lekko w lewo od góry – nadal 0 (zawijanie przez 360°)
    // 8 slotów: co 45°
    [InlineData(0.7f, 0.7f, 8, 1)]
    [InlineData(0.7f, -0.7f, 8, 3)]
    [InlineData(-0.7f, 0.7f, 8, 7)]
    // 2 sloty: góra / dół
    [InlineData(0.9f, 0.1f, 2, 0)]
    [InlineData(0.9f, -0.1f, 2, 1)]
    // 3 sloty: 0 u góry, 1 prawo-dół (120°), 2 lewo-dół (240°)
    [InlineData(0.8f, -0.5f, 3, 1)]
    [InlineData(-0.8f, -0.5f, 3, 2)]
    public void Sector_FromAngle(float x, float y, int count, int expected) =>
        Assert.Equal(expected, WheelSelector.Sector(x, y, count, 0.3f));

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.2f, 0.2f)]   // |v| ≈ 0.28 < 0.3
    [InlineData(float.NaN, 1f)]
    public void Sector_InsideDeadzone_IsNull(float x, float y) =>
        Assert.Null(WheelSelector.Sector(x, y, 4, 0.3f));

    [Fact]
    public void Sector_ZeroCount_IsNull() => Assert.Null(WheelSelector.Sector(0, 1, 0, 0.3f));

    [Fact]
    public void Select_StickHasPriority_PadAsFallback()
    {
        var settings = new WheelSettings { StickDeadzone = 0.5f, PadDeadzone = 0.35f };

        // gałka wychylona w prawo, palec na padzie u góry → gałka wygrywa
        Assert.Equal(1, WheelSelector.Select(1f, 0f, true, 0f, 1f, 4, settings));
        // gałka w martwej strefie → pad
        Assert.Equal(0, WheelSelector.Select(0.3f, 0f, true, 0f, 1f, 4, settings));
        // pad niedotknięty → nic
        Assert.Null(WheelSelector.Select(0.3f, 0f, false, 0f, 1f, 4, settings));
        // palec przy środku padu → nic (puszczenie = anuluj)
        Assert.Null(WheelSelector.Select(0f, 0f, true, 0.1f, 0.2f, 4, settings));
    }

    [Fact]
    public void Result_EmptySlotOrCenter_Cancels()
    {
        var slots = new List<string?> { "MTA", null, "Pulpit", null };
        Assert.Equal("MTA", WheelSelector.Result(slots, 0));
        Assert.Equal("Pulpit", WheelSelector.Result(slots, 2));
        Assert.Null(WheelSelector.Result(slots, 1));   // pusty slot
        Assert.Null(WheelSelector.Result(slots, null)); // środek
        Assert.Null(WheelSelector.Result(slots, 7));   // poza zakresem
    }
}

public class WheelSlotsTests
{
    static readonly string[] Profiles = { "MTA", "Pulpit", "Jazda" };

    static string? Find(string name) => Profiles.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void DefaultWheel_IsEmpty_WithFourSlots()
    {
        var wheel = new WheelSettings();
        Assert.Equal(WheelSettings.DefaultSlots, wheel.Slots.Count);
        Assert.All(wheel.Slots, Assert.Null);
    }

    [Fact]
    public void Normalize_ClampsCount_DropsMissing_FixesCase()
    {
        var removed = new List<string>();
        var slots = WheelSlots.Normalize(new string?[] { "mta", "  ", "Usunięty" }, Find, removed);
        Assert.Equal(new string?[] { "MTA", null, null }, slots);
        Assert.Equal(new[] { "Usunięty" }, removed);

        Assert.Equal(2, WheelSlots.Normalize(new string?[] { "MTA" }, Find).Count);
        Assert.Equal(2, WheelSlots.Normalize(null, Find).Count);
        Assert.Equal(8, WheelSlots.Normalize(Enumerable.Repeat<string?>("MTA", 12), Find).Count);
    }

    [Fact]
    public void Rename_And_Remove_UpdateSlots()
    {
        var slots = new List<string?> { "MTA", "Pulpit", "mta", null };
        Assert.Equal(new string?[] { "GTA", "Pulpit", "GTA", null }, WheelSlots.Rename(slots, "MTA", "GTA"));
        Assert.Equal(new string?[] { null, "Pulpit", null, null }, WheelSlots.Remove(slots, "MTA"));
        Assert.Equal("MTA", slots[0]); // wejście bez zmian
    }

    [Fact]
    public void Resize_RemovesEmptyFirst()
    {
        var slots = new List<string?> { "MTA", null, "Pulpit", null, "Jazda" };
        Assert.Equal(new string?[] { "MTA", "Pulpit", "Jazda" }, WheelSlots.Resize(slots, 3));
        Assert.Equal(new string?[] { "MTA", "Pulpit" }, WheelSlots.Resize(new string?[] { "MTA", "Pulpit", "Jazda" }, 2));
        Assert.Equal(6, WheelSlots.Resize(slots, 6).Count);
        Assert.Equal(WheelSettings.MaxSlots, WheelSlots.Resize(slots, 99).Count);
        Assert.Equal(WheelSettings.MinSlots, WheelSlots.Resize(slots, 0).Count);
    }

    [Fact]
    public void Move_SwapsWithNeighbour_AndWraps()
    {
        var slots = new List<string?> { "A", "B", "C" };
        Assert.Equal(new string?[] { "B", "A", "C" }, WheelSlots.Move(slots, 0, 1));
        Assert.Equal(new string?[] { "C", "B", "A" }, WheelSlots.Move(slots, 0, -1));
        Assert.Equal(new string?[] { "C", "B", "A" }, WheelSlots.Move(slots, 2, 1));
        Assert.Equal(slots, WheelSlots.Move(slots, 5, 1));
    }

    [Fact]
    public void Set_EmptyStringMeansEmptySlot()
    {
        var slots = new List<string?> { "A", "B" };
        Assert.Equal(new string?[] { "A", null }, WheelSlots.Set(slots, 1, " "));
        Assert.Equal(new string?[] { "A", "C" }, WheelSlots.Set(slots, 1, "C"));
    }
}

public class WheelLabelTests
{
    [Theory]
    [InlineData("MTA", null, "MTA")]
    [InlineData("Multi Theft Auto", null, "Multi")]
    [InlineData("Multi Theft Auto", "  ", "Multi")]
    [InlineData("Multi Theft Auto", "GTA", "GTA")]
    [InlineData("X", " Jazda! ", "Jazda!")]
    [InlineData("X", "Za długa", "Za dłu")]
    public void LabelFor(string name, string? label, string expected) =>
        Assert.Equal(expected, WheelLabels.For(name, label));

    [Fact]
    public void Length_CountsEmojiAsOne()
    {
        Assert.Equal(1, WheelLabels.Length("🚗"));
        Assert.Equal(3, WheelLabels.Length("🚗AB"));
        Assert.Null(WheelLabels.Validate("🚗🚗🚗🚗🚗🚗"));
        Assert.NotNull(WheelLabels.Validate("1234567"));
        Assert.Null(WheelLabels.Validate(null));
    }
}

public class WheelConfigTests
{
    [Fact]
    public void ProfileWheelAction_Parses()
    {
        Assert.True(ActionParser.TryParse("@ProfileWheel", out var action, out _));
        Assert.IsType<ProfileWheelAction>(action);
        Assert.True(ActionParser.TryParse("@profilewheel", out action, out _));
        Assert.IsType<ProfileWheelAction>(action);
    }

    [Fact]
    public void WheelLabelAndIcon_RoundTripInProfile()
    {
        var errors = new List<string>();
        var raw = ConfigLoader.ParseRaw("""{ "wheelLabel": "Jazda", "wheelIcon": "car.png" }""", errors)!;
        ConfigLoader.Normalize(raw);
        var again = ConfigLoader.ParseRaw(ConfigLoader.Serialize(raw), errors)!;
        Assert.Empty(errors);
        Assert.Equal("Jazda", again.WheelLabel);
        Assert.Equal("car.png", again.WheelIcon);
    }

    [Fact]
    public void TooLongWheelLabel_IsReported()
    {
        var errors = new List<string>();
        var raw = ConfigLoader.ParseRaw("""{ "wheelLabel": "Bardzo długa" }""", errors)!;
        ConfigLoader.Compile(raw, errors);
        Assert.Single(errors);
        Assert.Contains("wheelLabel", errors[0]);
    }

    [Fact]
    public void ProfileWithoutWheelFields_SerializesWithoutThem()
    {
        var raw = ConfigLoader.EmptyRaw();
        var text = ConfigLoader.Serialize(raw);
        var json = text[text.IndexOf("\n{", StringComparison.Ordinal)..]; // bez ściągi w komentarzu na górze (ma przykłady)
        Assert.DoesNotContain("wheelLabel", json);
        Assert.DoesNotContain("wheelIcon", json);
    }
}
