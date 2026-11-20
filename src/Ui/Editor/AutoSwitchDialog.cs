using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>Reguły automatycznego przełączania profilu po procesie okna na pierwszym planie.</summary>
sealed class AutoSwitchDialog : DarkDialog
{
    readonly AppSettings _settings;
    readonly List<string> _profiles;
    readonly KeyboardHost _keyboard;

    readonly DarkButton _enabled;
    readonly SimpleList _rules;
    readonly DarkButton _defaultButton;
    readonly DarkButton _changeProfile, _removeRule;

    AutoSwitchDialog(AppSettings settings, List<string> profiles, KeyboardHost keyboard)
    {
        _settings = settings;
        _profiles = profiles;
        _keyboard = keyboard;

        CenterOn(null, S(900), S(720));
        int pad = S(20), gap = S(10), h = S(56);
        int w = Width - pad * 2;

        var title = Theme.Label("Automatyczne przełączanie profili", Theme.Title);
        title.Bounds = new Rectangle(pad, pad, w, S(36));
        var info = Theme.Label("Co ~1 s sprawdzany jest proces okna na pierwszym planie. Profil zmienia się tylko przy zmianie " +
                               "okna, więc ręczny wybór profilu w grze zostaje. Okna DeckMappera są pomijane.", Theme.Small, Theme.TextDim);
        info.TextAlign = ContentAlignment.TopLeft;
        info.Bounds = new Rectangle(pad, title.Bottom, w, S(40));

        _enabled = new DarkButton();
        _enabled.Bounds = new Rectangle(pad, info.Bottom + S(6), w, h);
        _enabled.Click += (_, _) =>
        {
            _settings.AutoSwitch = !_settings.AutoSwitch;
            Refresh2();
        };

        int listTop = _enabled.Bottom + gap;
        int sideW = S(260);
        int bottomY = Height - pad - h;
        int defaultY = bottomY - gap - h;

        _rules = new SimpleList { Bounds = new Rectangle(pad, listTop, w - sideW - gap, defaultY - gap - listTop) };
        _rules.ItemClicked += i =>
        {
            _rules.SelectedIndex = i;
            Refresh2();
        };

        int sx = _rules.Right + gap, sy = listTop;
        DarkButton Side(string text, Action action)
        {
            var b = new DarkButton(text) { Bounds = new Rectangle(sx, sy, sideW, h) };
            b.Click += (_, _) => action();
            sy += h + gap;
            Controls.Add(b);
            return b;
        }
        Side("Dodaj z uruchomionych…", AddFromRunning);
        Side("Dodaj ręcznie…", AddManual);
        _changeProfile = Side("Zmień profil reguły…", ChangeRuleProfile);
        _removeRule = Side("Usuń regułę", RemoveRule);

        _defaultButton = new DarkButton { Bounds = new Rectangle(pad, defaultY, w, h), TextAlign = ContentAlignment.MiddleLeft };
        _defaultButton.Click += (_, _) => ChooseDefault();

        var cancel = new DarkButton("Anuluj");
        var save = new DarkButton("Zapisz") { Kind = DarkButton.Style.Primary };
        cancel.Bounds = new Rectangle(pad, bottomY, (w - gap) / 2, h);
        save.Bounds = new Rectangle(cancel.Right + gap, bottomY, (w - gap) / 2, h);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        save.Click += (_, _) => DialogResult = DialogResult.OK;

        Controls.AddRange(new Control[] { title, info, _enabled, _rules, _defaultButton, cancel, save });
        Refresh2();
    }

    void Refresh2()
    {
        _enabled.Text = _settings.AutoSwitch ? "✓  Automatyczne przełączanie: WŁĄCZONE" : "Automatyczne przełączanie: wyłączone";
        _enabled.Checked = _settings.AutoSwitch;

        _rules.SetItems(_settings.ProcessRules.Select(r =>
        {
            bool missing = !_profiles.Contains(r.Profile, StringComparer.OrdinalIgnoreCase);
            return new SimpleList.Item(r.Process, "→ profil " + r.Profile + (missing ? "  (nie istnieje!)" : ""),
                SubColor: missing ? Theme.Warning : null);
        }).ToList());

        bool hasRule = _rules.SelectedIndex >= 0 && _rules.SelectedIndex < _settings.ProcessRules.Count;
        _changeProfile.Enabled = _removeRule.Enabled = hasRule;
        _defaultButton.Text = "Gdy żaden proces nie pasuje: " +
                              (string.IsNullOrEmpty(_settings.DefaultProfile) ? "zostaw bieżący profil" : "profil " + _settings.DefaultProfile);
    }

    string? PickProfile(string title)
    {
        int i = ChoiceDialog.Ask(this, title, _profiles.Select(p => new SimpleList.Item(p)));
        return i >= 0 ? _profiles[i] : null;
    }

    void AddRule(string process)
    {
        process = process.Trim();
        if (process.Length == 0) return;
        if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process += ".exe";
        var profile = PickProfile($"Profil dla {process}");
        if (profile == null) return;

        var norm = ProcessRule.NormalizeProcess(process);
        _settings.ProcessRules.RemoveAll(r => ProcessRule.NormalizeProcess(r.Process) == norm);
        _settings.ProcessRules.Add(new ProcessRule { Process = process, Profile = profile });
        _rules.SelectedIndex = _settings.ProcessRules.Count - 1;
        Refresh2();
    }

    void AddFromRunning()
    {
        var running = ForegroundProcess.ListWindowed();
        if (running.Count == 0)
        {
            MessageDialog.Info(this, "Brak programów", "Nie znaleziono uruchomionych programów z oknem.");
            return;
        }
        int i = ChoiceDialog.Ask(this, "Wybierz program (np. uruchomioną grę)",
            running.Select(r => new SimpleList.Item(r.Process, string.IsNullOrWhiteSpace(r.Title) ? null : r.Title)));
        if (i >= 0) AddRule(running[i].Process);
    }

    void AddManual()
    {
        var name = TextPrompt.Ask(this, _keyboard, "Nazwa procesu", "Np. gta_sa.exe (MTA uruchamia grę jako gta_sa.exe).", "",
            v => v.Length == 0 ? "Wpisz nazwę procesu." : null);
        if (name != null) AddRule(name);
    }

    void ChangeRuleProfile()
    {
        int i = _rules.SelectedIndex;
        if (i < 0 || i >= _settings.ProcessRules.Count) return;
        var profile = PickProfile($"Profil dla {_settings.ProcessRules[i].Process}");
        if (profile == null) return;
        _settings.ProcessRules[i].Profile = profile;
        Refresh2();
    }

    void RemoveRule()
    {
        int i = _rules.SelectedIndex;
        if (i < 0 || i >= _settings.ProcessRules.Count) return;
        _settings.ProcessRules.RemoveAt(i);
        _rules.SelectedIndex = -1;
        Refresh2();
    }

    void ChooseDefault()
    {
        var items = new List<SimpleList.Item> { new("Zostaw bieżący profil", "nic nie zmieniaj") };
        items.AddRange(_profiles.Select(p => new SimpleList.Item(p)));
        int i = ChoiceDialog.Ask(this, "Gdy żaden proces nie pasuje", items);
        if (i < 0) return;
        _settings.DefaultProfile = i == 0 ? null : _profiles[i - 1];
        Refresh2();
    }

    /// <summary>Edytuje kopię ustawień; zwraca ją po „Zapisz”, null po „Anuluj”.</summary>
    public static AppSettings? Edit(IWin32Window? owner, AppSettings current, List<string> profiles, KeyboardHost keyboard)
    {
        using var dialog = new AutoSwitchDialog(current.Clone(), profiles, keyboard);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._settings : null;
    }
}
