using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Edytor profili pod ekran dotykowy Decka. Na czas edycji mapowanie jest wstrzymane (worker.SetSuspended),
/// a wciśnięcie przycisku na Decku zaznacza jego wiersz (snapshot z ControllerWorker, odpytywany timerem UI).
/// </summary>
sealed class EditorForm : Form
{
    const int LongPressMs = 600;

    readonly ProfileManager _profiles;
    readonly ControllerWorker _worker;
    readonly KeyboardHost _keyboard;

    // edytowany profil (kopia robocza)
    string _profileName = "";
    RawConfig _raw = ConfigLoader.EmptyRaw();
    CompiledConfig _compiled;
    List<string> _errors = new();
    bool _dirty;
    string? _layer; // null = baza
    DeckControl? _selected;
    bool _loadingSettings;

    // podgląd na żywo
    readonly Timer _timer;
    readonly DeckState _state = new();
    readonly bool[] _now = new bool[DeckControls.Count];
    readonly bool[] _prev = new bool[DeckControls.Count];
    readonly long[] _pressStart = new long[DeckControls.Count];
    readonly bool[] _fired = new bool[DeckControls.Count];
    long _lizardHeldAt = -10_000;   // kiedy ostatnio trzymano L2/R2/klik prawego padu
    bool _swallowLizardClick;       // po wyborze przytrzymaniem lizard mode i tak kliknie myszą przy puszczeniu

    // górny pasek
    readonly Label _profilesTitle;
    readonly DarkButton _tabMapping, _tabSettings, _save, _activate, _close;

    // lewa kolumna
    readonly SimpleList _profileList;
    readonly DarkButton _add, _duplicate, _rename, _delete, _autoSwitch;
    List<string> _profileNames = new();

    // mapowanie
    readonly Panel _mappingPage;
    readonly Panel _layerStrip;
    readonly List<DarkButton> _layerButtons = new();
    readonly DarkButton _addLayer, _removeLayer;
    readonly BindingListView _list;
    readonly Panel _actionBar;
    readonly Label _selectionLabel, _problemsLabel;
    readonly DarkButton _change, _clear, _inherit;

    // ustawienia
    readonly Panel _settingsPage;
    readonly Panel _settingsContent;
    readonly List<List<(Control Control, int Height)>> _settingsBlocks = new();
    StickEditor _leftStick = null!, _rightStick = null!;
    TouchSlider _deadzone = null!, _trigger = null!, _padDeadzone = null!;
    DarkButton _lizard = null!;

    readonly PreviewBar _preview;

    public EditorForm(ProfileManager profiles, ControllerWorker worker, KeyboardHost keyboard, Icon icon)
    {
        _profiles = profiles;
        _worker = worker;
        _keyboard = keyboard;
        _compiled = ConfigLoader.Compile(_raw, new List<string>());

        Text = "DeckMapper – edytor profili";
        Icon = icon;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Normal;
        StartPosition = FormStartPosition.CenterScreen;
        var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        Size = new Size(Math.Min(area.Width, LogicalToDeviceUnits(1280)), Math.Min(area.Height, LogicalToDeviceUnits(800)));
        MinimumSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(420));
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        DoubleBuffered = true;

        // --- górny pasek ---
        _profilesTitle = Theme.Label("Profile", Theme.Title);
        _tabMapping = new DarkButton("Mapowanie");
        _tabMapping.Click += (_, _) => ShowSettings(false);
        _tabSettings = new DarkButton("Ustawienia");
        _tabSettings.Click += (_, _) => ShowSettings(true);
        _save = new DarkButton("Zapisz") { Kind = DarkButton.Style.Primary };
        _save.Click += (_, _) => Save();
        _activate = new DarkButton("Ustaw jako aktywny");
        _activate.Click += (_, _) => ActivateEdited();
        _close = new DarkButton("✕") { Font = Theme.Big };
        _close.Click += (_, _) => Close();

        // --- profile ---
        _profileList = new SimpleList();
        _profileList.ItemClicked += i =>
        {
            if (i < _profileNames.Count) SwitchEditedProfile(_profileNames[i]);
        };
        _add = new DarkButton("Dodaj");
        _add.Click += (_, _) => AddProfile();
        _duplicate = new DarkButton("Duplikuj");
        _duplicate.Click += (_, _) => DuplicateProfile();
        _rename = new DarkButton("Zmień nazwę");
        _rename.Click += (_, _) => RenameProfile();
        _delete = new DarkButton("Usuń");
        _delete.Click += (_, _) => DeleteProfile();
        _autoSwitch = new DarkButton("Auto-przełączanie…");
        _autoSwitch.Click += (_, _) => EditAutoSwitch();

        // --- mapowanie ---
        _mappingPage = new Panel { BackColor = Theme.Back };
        _layerStrip = new Panel { BackColor = Theme.Back };
        _addLayer = new DarkButton("+ Warstwa");
        _addLayer.Click += (_, _) => AddLayer();
        _removeLayer = new DarkButton("Usuń warstwę");
        _removeLayer.Click += (_, _) => RemoveLayer();
        _layerStrip.Controls.Add(_addLayer);
        _layerStrip.Controls.Add(_removeLayer);
        _layerStrip.Resize += (_, _) => LayoutLayerStrip();

        _list = new BindingListView { RowProvider = RowFor };
        _list.ControlClicked += c =>
        {
            bool swallow = _swallowLizardClick && Environment.TickCount64 - _lizardHeldAt < 500;
            _swallowLizardClick = false;
            if (swallow) return;
            SelectControl(c, fromController: false);
            ChangeAction(c);
        };

        _actionBar = new Panel { BackColor = Theme.Back };
        _selectionLabel = Theme.Label("", Theme.Normal);
        _selectionLabel.AutoEllipsis = true;
        _problemsLabel = Theme.Label("", Theme.Small, Theme.Warning);
        _problemsLabel.AutoEllipsis = true;
        _change = new DarkButton("Zmień…");
        _change.Click += (_, _) => { if (_selected is { } c) ChangeAction(c); };
        _clear = new DarkButton("Wyczyść");
        _clear.Click += (_, _) => { if (_selected is { } c) SetAction(c, ""); };
        _inherit = new DarkButton("Z bazy");
        _inherit.Click += (_, _) => { if (_selected is { } c) Inherit(c); };
        _actionBar.Controls.AddRange(new Control[] { _selectionLabel, _problemsLabel, _change, _clear, _inherit });
        _actionBar.Resize += (_, _) => LayoutActionBar();

        _mappingPage.Controls.AddRange(new Control[] { _layerStrip, _list, _actionBar });
        _mappingPage.Resize += (_, _) => LayoutMappingPage();

        // --- ustawienia ---
        _settingsPage = new Panel { BackColor = Theme.Back, AutoScroll = true, Visible = false };
        _settingsContent = new Panel { BackColor = Theme.Back };
        _settingsPage.Controls.Add(_settingsContent);
        _settingsPage.Resize += (_, _) => LayoutSettings();
        BuildSettings();

        _preview = new PreviewBar();

        Controls.AddRange(new Control[]
        {
            _profilesTitle, _tabMapping, _tabSettings, _save, _activate, _close,
            _profileList, _add, _duplicate, _rename, _delete, _autoSwitch,
            _mappingPage, _settingsPage, _preview,
        });

        _profiles.ProfilesChanged += OnProfilesChanged;
        _profiles.ActiveChanged += OnProfilesChanged;

        _timer = new Timer { Interval = 33 };
        _timer.Tick += (_, _) => PollController();

        LoadForEdit(_profiles.ActiveName);
        ShowSettings(false);
        LayoutAll();
    }

    int S(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    // ============================================================ układ

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_preview != null) LayoutAll();
    }

    void LayoutAll()
    {
        var cs = ClientSize;
        if (cs.Width < 100 || cs.Height < 100) return;
        int pad = S(12), gap = S(8), h = S(56);
        int topH = S(64), previewH = S(46);
        int leftW = Math.Clamp(cs.Width / 5, S(210), S(300));
        int mx = pad + leftW + S(16);

        // górny pasek
        _profilesTitle.Bounds = new Rectangle(pad, S(4), leftW, h);
        int x = cs.Width - pad;
        foreach (var (b, w) in new[] { (_close, S(60)), (_activate, S(190)), (_save, S(130)) })
        {
            x -= w;
            b.Bounds = new Rectangle(x, S(4), w, h);
            x -= gap;
        }
        int tabW = Math.Max(S(100), Math.Min(S(170), (x - mx - gap) / 2));
        _tabMapping.Bounds = new Rectangle(mx, S(4), tabW, h);
        _tabSettings.Bounds = new Rectangle(mx + tabW + gap, S(4), tabW, h);

        _preview.Bounds = new Rectangle(0, cs.Height - previewH, cs.Width, previewH);
        int bottom = cs.Height - previewH - pad;

        // lewa kolumna
        int half = (leftW - gap) / 2;
        _autoSwitch.Bounds = new Rectangle(pad, bottom - h, leftW, h);
        _rename.Bounds = new Rectangle(pad, _autoSwitch.Top - gap - h, half, h);
        _delete.Bounds = new Rectangle(pad + half + gap, _rename.Top, leftW - half - gap, h);
        _add.Bounds = new Rectangle(pad, _rename.Top - gap - h, half, h);
        _duplicate.Bounds = new Rectangle(pad + half + gap, _add.Top, leftW - half - gap, h);
        _profileList.Bounds = new Rectangle(pad, topH + gap, leftW, _add.Top - gap - topH - gap);

        // strona główna
        var page = new Rectangle(mx, topH + gap, cs.Width - mx - pad, bottom - topH - gap);
        _mappingPage.Bounds = page;
        _settingsPage.Bounds = page;
    }

    void LayoutMappingPage()
    {
        int h = S(56), gap = S(6);
        var size = _mappingPage.ClientSize;
        _layerStrip.Bounds = new Rectangle(0, 0, size.Width, h);
        _actionBar.Bounds = new Rectangle(0, size.Height - h, size.Width, h);
        _list.Bounds = new Rectangle(0, h + gap, size.Width, Math.Max(0, size.Height - h * 2 - gap * 2));
    }

    void LayoutLayerStrip()
    {
        int h = _layerStrip.Height, gap = S(6);
        int right = _layerStrip.Width;
        _removeLayer.Bounds = new Rectangle(right - S(160), 0, S(160), h);
        _addLayer.Bounds = new Rectangle(_removeLayer.Left - gap - S(140), 0, S(140), h);

        int x = 0, limit = _addLayer.Left - gap;
        foreach (var b in _layerButtons)
        {
            int w = Math.Max(S(100), TextRenderer.MeasureText(b.Text, Theme.Normal).Width + S(36));
            w = Math.Min(w, Math.Max(S(60), limit - x));
            b.Bounds = new Rectangle(x, 0, w, h);
            b.Visible = x + S(40) < limit;
            x += w + gap;
        }
    }

    void LayoutActionBar()
    {
        int h = _actionBar.Height, gap = S(6), bw = S(118);
        int x = _actionBar.Width;
        foreach (var b in new[] { _inherit, _clear, _change })
        {
            if (!b.Visible) continue;
            x -= bw;
            b.Bounds = new Rectangle(x, 0, bw, h);
            x -= gap;
        }
        int lineH = h / 2;
        _selectionLabel.Bounds = new Rectangle(S(4), 0, Math.Max(0, x - S(8)), _problemsLabel.Text.Length > 0 ? lineH + S(4) : h);
        _problemsLabel.Bounds = new Rectangle(S(4), lineH, Math.Max(0, x - S(8)), h - lineH);
        _problemsLabel.Visible = _problemsLabel.Text.Length > 0;
    }

    // ============================================================ profile

    void LoadForEdit(string name)
    {
        var errors = new List<string>();
        var raw = ConfigLoader.LoadRaw(_profiles.Store.PathOf(name), errors, out _);
        if (raw == null)
        {
            MessageDialog.Info(this, "Nie można wczytać profilu",
                $"Profil „{name}” ma błąd i nie da się go edytować:\n{string.Join("\n", errors.Take(3))}\n\n" +
                "Popraw plik ręcznie (menu ikonki → Edytuj plik profilu) albo usuń profil. " +
                "Zapis z edytora nadpisze plik pustym profilem.");
            raw = ConfigLoader.EmptyRaw();
        }
        ConfigLoader.Normalize(raw);

        _profileName = name;
        _raw = raw;
        _dirty = false;
        if (_layer != null && !_raw.Layers!.ContainsKey(_layer)) _layer = null;
        Recompile();
        LoadSettingsControls();
        RebuildLayerStrip();
        RefreshProfileList();
        RefreshAll();
        _list.ScrollToTop();
    }

    void Recompile()
    {
        _errors = new List<string>();
        _compiled = ConfigLoader.Compile(_raw, _errors, _profiles.ProfileExists);
    }

    void MarkDirty()
    {
        bool wasDirty = _dirty;
        _dirty = true;
        Recompile();
        RefreshAll();
        if (!wasDirty) RefreshProfileList(); // suwak woła to przy każdym ruchu – bez czytania folderu za każdym razem
    }

    void OnProfilesChanged()
    {
        if (IsDisposed) return;
        RefreshProfileList();
        RefreshHeader();
    }

    void RefreshProfileList()
    {
        _profileNames = _profiles.Names;
        if (!_profileNames.Contains(_profileName, StringComparer.OrdinalIgnoreCase)) _profileNames.Add(_profileName);
        _profileList.SetItems(_profileNames.Select(n =>
        {
            bool active = string.Equals(n, _profiles.ActiveName, StringComparison.OrdinalIgnoreCase);
            bool edited = string.Equals(n, _profileName, StringComparison.OrdinalIgnoreCase);
            string? sub = active ? "aktywny" : null;
            if (edited && _dirty) sub = (sub != null ? sub + " · " : "") + "niezapisane zmiany";
            return new SimpleList.Item(n, sub, active, edited && _dirty ? Theme.Override : Theme.Ok);
        }).ToList());
        _profileList.SelectedIndex = _profileNames.FindIndex(n => string.Equals(n, _profileName, StringComparison.OrdinalIgnoreCase));
        _delete.Enabled = _profileNames.Count > 1;
    }

    void RefreshHeader()
    {
        _save.Enabled = _dirty;
        bool active = string.Equals(_profileName, _profiles.ActiveName, StringComparison.OrdinalIgnoreCase);
        _activate.Enabled = !active;
        _activate.Text = active ? "Profil aktywny" : "Ustaw jako aktywny";
        Text = $"DeckMapper – edytor profili – {_profileName}{(_dirty ? " *" : "")}";
    }

    /// <summary>Pyta o niezapisane zmiany. true = można kontynuować (zapisane albo odrzucone).</summary>
    bool ConfirmLeave(bool reloadOnDiscard)
    {
        if (!_dirty) return true;
        int choice = Ask("Niezapisane zmiany", $"Profil „{_profileName}” ma niezapisane zmiany.",
            ("Zapisz", DarkButton.Style.Primary), ("Odrzuć", DarkButton.Style.Danger), ("Anuluj", DarkButton.Style.Normal));
        switch (choice)
        {
            case 0:
                return Save();
            case 1:
                _dirty = false;
                if (reloadOnDiscard) LoadForEdit(_profileName);
                return true;
            default:
                return false;
        }
    }

    int Ask(string title, string text, params (string, DarkButton.Style)[] buttons) => MessageDialog.Ask(this, title, text, buttons);

    void SwitchEditedProfile(string name)
    {
        if (string.Equals(name, _profileName, StringComparison.OrdinalIgnoreCase)) return;
        if (!ConfirmLeave(reloadOnDiscard: false)) return;
        LoadForEdit(name);
    }

    bool Save()
    {
        try
        {
            _profiles.Save(_profileName, _raw);
            _dirty = false;
            RefreshAll();
            RefreshProfileList();
            return true;
        }
        catch (Exception e)
        {
            Log.Error("Zapis profilu", e);
            MessageDialog.Info(this, "Nie udało się zapisać", e.Message);
            return false;
        }
    }

    void ActivateEdited()
    {
        if (!ConfirmLeave(reloadOnDiscard: true)) return;
        _profiles.Activate(_profileName);
        RefreshHeader();
        RefreshProfileList();
    }

    string? AskProfileName(string title, string initial, string? renaming = null) =>
        TextPrompt.Ask(this, _keyboard, title, "Nazwa profilu (będzie też nazwą pliku w folderze profiles).", initial,
            v => _profiles.Store.ValidateNewName(v, renaming));

    void AddProfile()
    {
        if (!ConfirmLeave(reloadOnDiscard: true)) return;
        var name = AskProfileName("Nowy profil", "");
        if (name == null) return;
        if (!TryFileOp(() => _profiles.Create(name, ConfigLoader.EmptyRaw()))) return;
        LoadForEdit(name);
    }

    void DuplicateProfile()
    {
        if (!ConfirmLeave(reloadOnDiscard: true)) return;
        var source = _profileName;
        var name = AskProfileName($"Duplikuj „{source}”", source + " kopia");
        if (name == null) return;
        if (!TryFileOp(() => _profiles.Duplicate(source, name))) return;
        LoadForEdit(name);
    }

    void RenameProfile()
    {
        var old = _profileName;
        var name = AskProfileName($"Zmień nazwę „{old}”", old, renaming: old);
        if (name == null || name == old) return;
        if (!TryFileOp(() => _profiles.Rename(old, name))) return;
        _profileName = name;
        RefreshProfileList();
        RefreshAll();
        if (HasProfileReferences(old))
            MessageDialog.Info(this, "Zmieniono nazwę",
                $"Uwaga: akcje @Profile:{old} w profilach nie zmieniły się same – popraw je, jeśli ich używasz.");
    }

    bool HasProfileReferences(string name)
    {
        foreach (var profile in _profiles.Names)
        {
            var raw = ConfigLoader.LoadRaw(_profiles.Store.PathOf(profile), new List<string>(), out var text);
            if (raw != null && text.Contains("@Profile:" + name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    void DeleteProfile()
    {
        if (_profiles.Names.Count <= 1) return;
        int choice = Ask("Usunąć profil?", $"Profil „{_profileName}” zostanie trwale usunięty (plik {_profileName}.json).",
            ("Usuń", DarkButton.Style.Danger), ("Anuluj", DarkButton.Style.Normal));
        if (choice != 0) return;
        var name = _profileName;
        if (!TryFileOp(() => _profiles.Delete(name))) return;
        _dirty = false;
        LoadForEdit(_profiles.ActiveName);
    }

    bool TryFileOp(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e)
        {
            Log.Error("Operacja na profilu", e);
            MessageDialog.Info(this, "Nie udało się", e.Message);
            RefreshProfileList();
            return false;
        }
    }

    void EditAutoSwitch()
    {
        var edited = AutoSwitchDialog.Edit(this, _profiles.Settings, _profiles.Names, _keyboard);
        if (edited != null) _profiles.UpdateAutoSwitch(edited);
    }

    // ============================================================ warstwy

    Dictionary<string, Dictionary<string, string?>> Layers => _raw.Layers!;
    Dictionary<string, string?> Bindings => _raw.Bindings!;

    void RebuildLayerStrip()
    {
        foreach (var b in _layerButtons)
        {
            _layerStrip.Controls.Remove(b);
            b.Dispose();
        }
        _layerButtons.Clear();

        var names = new List<string?> { null };
        names.AddRange(Layers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        foreach (var name in names)
        {
            var b = new DarkButton(name == null ? "Baza" : "Warstwa: " + name) { Checked = name == _layer };
            b.Click += (_, _) =>
            {
                _layer = name;
                foreach (var other in _layerButtons) other.Checked = other == b;
                RefreshAll();
            };
            _layerButtons.Add(b);
            _layerStrip.Controls.Add(b);
        }
        _removeLayer.Enabled = _layer != null;
        LayoutLayerStrip();
    }

    void AddLayer()
    {
        var name = TextPrompt.Ask(this, _keyboard, "Nowa warstwa",
            "Nazwa warstwy. Działa, gdy trzymasz przycisk z akcją „Warstwa” (np. L4).", "",
            v =>
            {
                if (v.Length == 0) return "Nazwa nie może być pusta.";
                if (v.Contains('"')) return "Nazwa nie może zawierać cudzysłowu.";
                return Layers.ContainsKey(v) ? $"Warstwa „{v}” już istnieje." : null;
            });
        if (name == null) return;
        Layers[name] = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        _layer = name;
        RebuildLayerStrip();
        MarkDirty();
        if (!Bindings.Values.Any(v => v != null && v.Trim().Equals("@Layer:" + name, StringComparison.OrdinalIgnoreCase)))
            MessageDialog.Info(this, "Warstwa dodana",
                $"Żeby warstwa „{name}” działała, przypisz w bazie jakiemuś przyciskowi akcję Specjalne → Warstwa: {name} (np. L4).");
    }

    void RemoveLayer()
    {
        if (_layer == null) return;
        var name = _layer;
        int choice = Ask("Usunąć warstwę?", $"Warstwa „{name}” i wszystkie jej przypisania zostaną usunięte.",
            ("Usuń", DarkButton.Style.Danger), ("Anuluj", DarkButton.Style.Normal));
        if (choice != 0) return;
        Layers.Remove(name);
        _layer = null;
        RebuildLayerStrip();
        MarkDirty();
    }

    // ============================================================ przypisania

    BindingListView.Row RowFor(DeckControl control)
    {
        var info = ControlInfo.Of(control);
        string key = control.ToString();
        string? note = info.Note;
        Color? noteColor = null;

        if (info.IsLeftStickDirection && _compiled.LeftStick.Mode != StickMode.Keys)
            (note, noteColor) = ($"nieaktywne – tryb lewej gałki: {ModeName(_compiled.LeftStick.Mode)}", Theme.Warning);
        else if (info.IsRightStickDirection && _compiled.RightStick.Mode != StickMode.Keys)
            (note, noteColor) = ($"nieaktywne – tryb prawej gałki: {ModeName(_compiled.RightStick.Mode)}", Theme.Warning);

        Bindings.TryGetValue(key, out var baseAction);

        if (_layer == null || !Layers.TryGetValue(_layer, out var layer))
        {
            if (KeyCatalog.IsNone(baseAction)) return new("—", RowState.Empty, null, note, noteColor);
            var text = KeyCatalog.Describe(baseAction);
            return new(text, text.StartsWith('⚠') ? RowState.Invalid : RowState.Set, null, note, noteColor);
        }

        if (layer.TryGetValue(key, out var own))
        {
            var text = KeyCatalog.IsNone(own) ? "brak (wyłączone)" : KeyCatalog.Describe(own);
            return new(text, text.StartsWith('⚠') ? RowState.Invalid : RowState.Overridden, "nadpisane w warstwie", note, noteColor);
        }

        return new(KeyCatalog.Describe(baseAction), KeyCatalog.IsNone(baseAction) ? RowState.Empty : RowState.Inherited,
            "z bazy", note, noteColor);
    }

    static string ModeName(StickMode mode) => mode switch
    {
        StickMode.Keys => "klawisze",
        StickMode.Mouse => "mysz",
        _ => "nic",
    };

    void SelectControl(DeckControl control, bool fromController)
    {
        _selected = control;
        _list.Selected = control;
        if (fromController)
        {
            if (_settingsPage.Visible) ShowSettings(false);
            _list.Flash(control);
            _list.EnsureVisible(_list.IndexOf(control));
        }
        _list.Invalidate();
        RefreshActionBar();
    }

    void ChangeAction(DeckControl control)
    {
        var info = ControlInfo.Of(control);
        string key = control.ToString();
        string? current;
        string context = $"Profil „{_profileName}” · ";
        if (_layer == null)
        {
            Bindings.TryGetValue(key, out current);
            context += "baza";
        }
        else
        {
            Layers[_layer].TryGetValue(key, out current);
            context += $"warstwa „{_layer}”";
            if (current == null)
            {
                Bindings.TryGetValue(key, out var inherited);
                context += $" · teraz z bazy: {KeyCatalog.Describe(inherited)}";
            }
        }

        var result = ActionPicker.Pick(this, info.Name, context, current,
            Layers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), _profiles.Names, _keyboard);
        if (result != null) SetAction(control, result);
    }

    void SetAction(DeckControl control, string action)
    {
        string key = control.ToString();
        if (_layer == null)
        {
            if (KeyCatalog.IsNone(action)) Bindings.Remove(key);
            else Bindings[key] = action;
        }
        else
        {
            Layers[_layer][key] = KeyCatalog.IsNone(action) ? "none" : action;
        }
        MarkDirty();
    }

    void Inherit(DeckControl control)
    {
        if (_layer == null) return;
        if (Layers[_layer].Remove(control.ToString())) MarkDirty();
    }

    void RefreshAll()
    {
        _list.Invalidate();
        RefreshActionBar();
        RefreshHeader();
        _removeLayer.Enabled = _layer != null;
    }

    void RefreshActionBar()
    {
        _inherit.Visible = _layer != null;
        bool has = _selected != null;
        _change.Enabled = _clear.Enabled = has;
        _inherit.Enabled = has && _layer != null && Layers.TryGetValue(_layer, out var l) && l.ContainsKey(_selected!.Value.ToString());

        if (_selected is { } c)
        {
            var row = RowFor(c);
            _selectionLabel.Text = $"{ControlInfo.Of(c).Name} → {row.Action}";
            _selectionLabel.ForeColor = Theme.Text;
        }
        else
        {
            _selectionLabel.Text = "Stuknij wiersz albo wciśnij przycisk na Decku (L2/R2/prawy pad: przytrzymaj).";
            _selectionLabel.ForeColor = Theme.TextDim;
        }

        _problemsLabel.Text = _errors.Count == 0 ? "" :
            $"⚠ {_errors.Count} {(_errors.Count == 1 ? "problem" : "problemy")}: {string.Join("; ", _errors.Take(2))}";
        LayoutActionBar();
    }

    void ShowSettings(bool settings)
    {
        _settingsPage.Visible = settings;
        _mappingPage.Visible = !settings;
        _tabSettings.Checked = settings;
        _tabMapping.Checked = !settings;
        if (settings) LayoutSettings();
    }

    // ============================================================ ustawienia profilu

    sealed class StickEditor
    {
        public readonly DarkButton KeysButton, MouseButton, NoneButton;
        public readonly TouchSlider Speed, Curve;
        public readonly Label Note;

        public StickEditor(DarkButton keys, DarkButton mouse, DarkButton none, TouchSlider speed, TouchSlider curve, Label note)
        {
            (KeysButton, MouseButton, NoneButton, Speed, Curve, Note) = (keys, mouse, none, speed, curve, note);
        }
    }

    void BuildSettings()
    {
        _leftStick = BuildStick("Lewa gałka", () => _raw.LeftStick!, "Lewa gałka");
        _rightStick = BuildStick("Prawa gałka", () => _raw.RightStick!, "Prawa gałka");

        var general = new List<(Control, int)> { (Header("Ogólne"), S(40)) };
        _deadzone = AddSlider(general, "Martwa strefa gałek", 0.05, 0.9, 0.01, "0.00", v => _raw.StickDeadzone = v);
        _trigger = AddSlider(general, "Próg wciśnięcia L2 / R2", 0.05, 1, 0.01, "0.00", v => _raw.TriggerThreshold = v);
        _padDeadzone = AddSlider(general, "Martwa strefa lewego touchpada (kierunki)", 0, 0.9, 0.01, "0.00", v => _raw.LeftPadDeadzone = v);

        _lizard = new DarkButton("Wyłącz klawisze lizard mode") { TextAlign = ContentAlignment.MiddleLeft };
        _lizard.Click += (_, _) =>
        {
            _raw.DisableLizardButtons = !_raw.DisableLizardButtons;
            UpdateSettingsState();
            MarkDirty();
        };
        general.Add((_lizard, S(56)));
        general.Add((Note("Kontroler sam wysyła w lizard mode np. Enter (A), Esc (B) i strzałki (d-pad). Włączenie tej opcji " +
                          "je wyłącza, ale może też wyłączyć kliki R2/L2 – sprawdź w grze. Lizard mode (kursor na prawym " +
                          "touchpadzie) zostaje.", Theme.TextDim), S(84)));
        _settingsBlocks.Add(general);

        foreach (var block in _settingsBlocks)
            foreach (var (control, _) in block)
            {
                _settingsContent.Controls.Add(control);
                if (control is Label) EnableDragScroll(control);
            }
        EnableDragScroll(_settingsContent);
    }

    StickEditor BuildStick(string title, Func<RawStick> stick, string listName)
    {
        var block = new List<(Control, int)> { (Header(title), S(40)) };
        var row = new RowPanel();
        var keys = new DarkButton("Klawisze");
        var mouse = new DarkButton("Mysz");
        var none = new DarkButton("Nic");
        row.Controls.AddRange(new Control[] { keys, mouse, none });
        block.Add((row, S(56)));

        void SetMode(string mode)
        {
            stick().Mode = mode;
            UpdateSettingsState();
            MarkDirty();
        }
        keys.Click += (_, _) => SetMode("keys");
        mouse.Click += (_, _) => SetMode("mouse");
        none.Click += (_, _) => SetMode("none");

        var speed = AddSlider(block, "Tryb myszy: prędkość (px/s)", 50, 5000, 50, "0", v => stick().MouseSpeed = v);
        var curve = AddSlider(block, "Tryb myszy: krzywa (1 = liniowo, więcej = precyzyjniej)", 0.5, 5, 0.1, "0.0", v => stick().Curve = v);
        var note = Note("", Theme.TextDim);
        note.Tag = listName;
        block.Add((note, S(64)));
        _settingsBlocks.Add(block);
        return new StickEditor(keys, mouse, none, speed, curve, note);
    }

    Label Header(string text) => Theme.Label(text, Theme.Title);

    static Label Note(string text, Color color)
    {
        var label = Theme.Label(text, Theme.Small, color);
        label.TextAlign = ContentAlignment.TopLeft;
        return label;
    }

    TouchSlider AddSlider(List<(Control, int)> block, string label, double min, double max, double step, string format, Action<float> apply)
    {
        var caption = Theme.Label(label, Theme.Small, Theme.TextDim);
        caption.AutoEllipsis = true;
        var slider = new TouchSlider { Minimum = min, Maximum = max, Step = step, Format = format };
        slider.ValueChanged += () =>
        {
            if (_loadingSettings) return;
            apply((float)Math.Round(slider.Value, 2));
            MarkDirty();
        };
        block.Add((caption, S(26)));
        block.Add((slider, S(52)));
        return slider;
    }

    void LoadSettingsControls()
    {
        _loadingSettings = true;
        try
        {
            _leftStick.Speed.Value = _raw.LeftStick!.MouseSpeed;
            _leftStick.Curve.Value = _raw.LeftStick.Curve;
            _rightStick.Speed.Value = _raw.RightStick!.MouseSpeed;
            _rightStick.Curve.Value = _raw.RightStick.Curve;
            _deadzone.Value = _raw.StickDeadzone;
            _trigger.Value = _raw.TriggerThreshold;
            _padDeadzone.Value = _raw.LeftPadDeadzone;
        }
        finally
        {
            _loadingSettings = false;
        }
        UpdateSettingsState();
    }

    void UpdateSettingsState()
    {
        foreach (var (editor, stick) in new[] { (_leftStick, _raw.LeftStick!), (_rightStick, _raw.RightStick!) })
        {
            var mode = stick.Mode ?? "keys";
            editor.KeysButton.Checked = mode == "keys";
            editor.MouseButton.Checked = mode == "mouse";
            editor.NoneButton.Checked = mode == "none";
            editor.Speed.Enabled = editor.Curve.Enabled = mode == "mouse";
            var name = (string)editor.Note.Tag!;
            (editor.Note.Text, editor.Note.ForeColor) = mode switch
            {
                "mouse" => ("Gałka porusza kursorem (SendInput). W MTA to NIE działa – MTA ignoruje programową mysz; " +
                            "kamera zostaje na prawym touchpadzie (lizard mode).", Theme.Warning),
                "keys" => ($"Kierunki przypisujesz na liście: {name} ↑ ↓ ← →.", Theme.TextDim),
                _ => ("Gałka nic nie wysyła.", Theme.TextDim),
            };
        }
        _lizard.Checked = _raw.DisableLizardButtons;
        _lizard.Text = (_raw.DisableLizardButtons ? "✓  " : "") + "Wyłącz klawisze lizard mode";
    }

    void LayoutSettings()
    {
        if (!_settingsPage.Visible) return;
        int gap = S(20);
        int width = _settingsPage.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        int columns = Math.Clamp(width / S(280), 1, 3);
        int colW = (width - gap * (columns - 1)) / columns;
        var heights = new int[columns];

        foreach (var block in _settingsBlocks)
        {
            int col = Array.IndexOf(heights, heights.Min());
            int y = heights[col];
            foreach (var (control, h) in block)
            {
                control.Bounds = new Rectangle(col * (colW + gap), y, colW, h);
                y += h + S(4);
            }
            heights[col] = y + S(16);
        }

        var scroll = _settingsPage.AutoScrollPosition;
        _settingsContent.Bounds = new Rectangle(scroll.X, scroll.Y, width, heights.Max());
    }

    /// <summary>Przewijanie ustawień przeciągnięciem palca (panel z AutoScroll sam tego nie robi).</summary>
    void EnableDragScroll(Control target)
    {
        bool down = false;
        int startY = 0, startScroll = 0;
        target.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            down = true;
            startY = Cursor.Position.Y;
            startScroll = -_settingsPage.AutoScrollPosition.Y;
        };
        target.MouseMove += (_, _) =>
        {
            if (down) _settingsPage.AutoScrollPosition = new Point(0, Math.Max(0, startScroll - (Cursor.Position.Y - startY)));
        };
        target.MouseUp += (_, _) => down = false;
    }

    // ============================================================ kontroler na żywo

    void PollController()
    {
        _list.Tick();

        if (!_worker.Connected || !_worker.TryGetSnapshot(_state))
        {
            _preview.Set("Kontroler niepodłączony – wybór wciśnięciem i podgląd nie działają.", new List<string>());
            return;
        }

        Mapper.Evaluate(_compiled, _state, _now);
        long t = Environment.TickCount64;
        DeckControl? pick = null;

        for (int i = 0; i < _now.Length; i++)
        {
            var control = (DeckControl)i;
            if (_now[i] && !_prev[i])
            {
                _pressStart[i] = t;
                _fired[i] = !ControlInfo.Of(control).LongPressToSelect;
                if (_fired[i]) pick = control;
            }
            else if (_now[i] && !_fired[i] && t - _pressStart[i] >= LongPressMs)
            {
                _fired[i] = true;
                pick = control;
                _swallowLizardClick = true;
            }
            if (_now[i] && ControlInfo.Of(control).LongPressToSelect) _lizardHeldAt = t;
        }
        Array.Copy(_now, _prev, _now.Length);

        // tylko gdy edytor jest aktywnym oknem (nie w trakcie pickera/dialogu ani gdy gra ma fokus)
        if (pick is { } picked && ActiveForm == this) SelectControl(picked, fromController: true);

        UpdatePreview();
    }

    void UpdatePreview()
    {
        var pressed = ControlInfo.All.Where(i => _now[(int)i.Control]).ToList();
        if (pressed.Count == 0)
        {
            _preview.Set("Na żywo (mapowanie wstrzymane na czas edycji) – wciśnij coś na Decku:", new List<string>());
            return;
        }

        // warstwy aktywne tak jak w Mapperze: przyciski z akcją @Layer w bazie
        var layers = new List<string>();
        foreach (var info in pressed)
            if (ActionParser.TryParse(RawAction(info.Control, Array.Empty<string>()), out var a, out _) && a is LayerAction la)
                layers.Add(la.Name);

        var chips = pressed.Select(info =>
        {
            var action = RawAction(info.Control, layers);
            return $"{info.Symbol} → {(KeyCatalog.IsNone(action) ? "nic" : KeyCatalog.Describe(action))}";
        }).ToList();
        var prefix = layers.Count > 0 ? $"Na żywo (warstwa {string.Join(", ", layers)}):" : "Na żywo:";
        _preview.Set(prefix, chips);
    }

    string? RawAction(DeckControl control, IReadOnlyList<string> layers)
    {
        string key = control.ToString();
        for (int k = layers.Count - 1; k >= 0; k--)
            if (Layers.TryGetValue(layers[k], out var layer) && layer.TryGetValue(key, out var v))
                return v;
        return Bindings.TryGetValue(key, out var b) ? b : null;
    }

    // ============================================================ zamykanie

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Enter/Esc/strzałki z lizard mode nie mają tu nic robić
        if (e.KeyCode is Keys.Enter or Keys.Escape or Keys.Up or Keys.Down or Keys.Left or Keys.Right)
            e.Handled = e.SuppressKeyPress = true;
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !ConfirmLeave(reloadOnDiscard: false))
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _profiles.ProfilesChanged -= OnProfilesChanged;
        _profiles.ActiveChanged -= OnProfilesChanged;
        base.OnFormClosed(e);
    }
}
