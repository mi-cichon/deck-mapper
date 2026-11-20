using System;
using System.Collections.Generic;
using System.Linq;
using DeckMapper;
using Xunit;

namespace DeckMapper.Tests;

public class LizardFilterPlannerTests
{
    static readonly LizardMap Map = LizardMap.BuiltIn;

    static KeyDef Key(string name) => KeyNames.TryGet(name, out var k) ? k : throw new ArgumentException(name);

    [Fact]
    public void Auto_SwallowsAllKeysFromMap_NeverMouse()
    {
        var plan = LizardFilterPlanner.Plan(Map, LizardProfileSettings.Default, globalEnabled: true);

        Assert.True(plan.Active);
        Assert.Equal(new[] { "Enter", "Esc", "Up", "Down", "Left", "Right" }, plan.KeyNames);
        Assert.True(plan.Swallows(Key("Enter")));
        Assert.True(plan.Swallows(Key("Up")));
        Assert.False(plan.Swallows(Key("W")));
        Assert.False(plan.Swallows(Key("NumEnter"))); // E0 1C ≠ 1C
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void GlobalSwitchOff_DisablesEverything()
    {
        var plan = LizardFilterPlanner.Plan(Map, LizardProfileSettings.Default, globalEnabled: false);
        Assert.False(plan.Active);
        Assert.Empty(plan.Codes);
        Assert.Contains("menu", plan.Reason);
    }

    [Fact]
    public void ProfileWithoutFilter_DisablesEverything()
    {
        var plan = LizardFilterPlanner.Plan(Map, LizardProfileSettings.Default with { FilterKeys = false }, true);
        Assert.False(plan.Active);
        Assert.False(plan.Swallows(Key("Enter")));
    }

    [Fact]
    public void ExplicitList_SwallowsOnlyListed_AndWarnsAboutOddEntries()
    {
        var settings = new LizardProfileSettings(true, false, new[] { "enter", "Escape", "Tab", "Blah" });
        var plan = LizardFilterPlanner.Plan(Map, settings, true);

        Assert.True(plan.Swallows(Key("Enter")));
        Assert.True(plan.Swallows(Key("Esc")));
        Assert.True(plan.Swallows(Key("Tab")));   // spoza mapy – połykany, ale z ostrzeżeniem
        Assert.False(plan.Swallows(Key("Up")));
        Assert.Equal(2, plan.Warnings.Count);
        Assert.Contains(plan.Warnings, w => w.Contains("Tab"));
        Assert.Contains(plan.Warnings, w => w.Contains("Blah"));
        Assert.Equal(new[] { "Enter", "Esc", "Tab" }, plan.KeyNames); // aliasy → nazwy kanoniczne, bez duplikatów
    }

    [Fact]
    public void EmptyList_IsInactive()
    {
        var plan = LizardFilterPlanner.Plan(Map, new LizardProfileSettings(true, false, Array.Empty<string>()), true);
        Assert.False(plan.Active);
    }

    [Fact]
    public void StatusOf_ReportsKeyLeaksAndMouse()
    {
        var plan = LizardFilterPlanner.Plan(Map, new LizardProfileSettings(true, false, new[] { "Enter" }), true);

        var a = LizardFilterPlanner.StatusOf(DeckControl.A, Map, plan);
        Assert.True(a.Swallowed);
        Assert.False(a.KeyLeaks);

        var b = LizardFilterPlanner.StatusOf(DeckControl.B, Map, plan);
        Assert.False(b.Swallowed);
        Assert.True(b.KeyLeaks);

        var rt = LizardFilterPlanner.StatusOf(DeckControl.RT, Map, plan);
        Assert.True(rt.Output.IsMouse);
        Assert.False(rt.Swallowed);
        Assert.False(rt.KeyLeaks);

        var x = LizardFilterPlanner.StatusOf(DeckControl.X, Map, plan);
        Assert.Equal(LizardOutputKind.None, x.Output.Kind);
    }

    [Fact]
    public void Toggle_FromAuto_RemovesKey_AndBackToAutoWhenComplete()
    {
        var removed = LizardFilterPlanner.Toggle(LizardProfileSettings.Default, Map, "Esc");
        Assert.False(removed.SwallowAuto);
        Assert.DoesNotContain(removed.SwallowKeys, k => k == "Esc");
        Assert.Equal(5, removed.SwallowKeys.Count);

        var restored = LizardFilterPlanner.Toggle(removed, Map, "Escape"); // alias tego samego klawisza
        Assert.True(restored.SwallowAuto);
        Assert.Empty(restored.SwallowKeys);
    }

    [Fact]
    public void Toggle_KeepsFilterKeysFlag()
    {
        var settings = new LizardProfileSettings(false, true, Array.Empty<string>());
        Assert.False(LizardFilterPlanner.Toggle(settings, Map, "Enter").FilterKeys);
    }
}

public class KeyboardDevicesTests
{
    [Theory]
    [InlineData(@"\\?\HID#VID_046D&PID_C31C&MI_00#7&2a1b&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", true)]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b33b&Col01#9&1#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", true)]
    [InlineData(@"\\?\HID#VID_28DE&PID_1205&MI_00#8&1#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", false)] // lizard Decka
    [InlineData(@"\\?\Root#RDP_KBD#0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", false)]
    [InlineData(@"\\?\ACPI#PNP0303#4&1#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsExternalKeyboard(string? path, bool expected) =>
        Assert.Equal(expected, KeyboardDevices.IsExternalKeyboard(path));
}
