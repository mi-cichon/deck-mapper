using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>Aplikacja w zasobniku: menu, profile, okna.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly Control _ui;
    readonly NotifyIcon _tray;
    readonly Icon _icon;
    readonly ToolStripMenuItem _statusItem;
    readonly ToolStripMenuItem _pauseItem;
    readonly ToolStripMenuItem _profilesItem;
    readonly ToolStripMenuItem _filterItem;
    readonly LizardService _lizard;
    readonly Timer _keyboardCheck;
    readonly WheelOverlay _wheel;
    bool _keyboardWarned;
    readonly Mapper _mapper;
    readonly ControllerWorker _worker;
    readonly ProfileManager _profiles;
    readonly KeyboardHost _keyboard = new();

    DebugForm? _debug;
    EditorForm? _editor;

    public TrayApp()
    {
        // niewidoczna kontrolka do przerzucania zdarzeń z wątku kontrolera na wątek UI
        _ui = new Control();
        _ = _ui.Handle;

        _profiles = new ProfileManager(OnUi);
        var errors = new List<string>();
        var config = _profiles.Initialize(errors);

        _mapper = new Mapper(config);
        _worker = new ControllerWorker(_mapper);
        _profiles.ConfigLoaded += _worker.ApplyConfig;

        _lizard = new LizardService(_worker, _profiles);
        _lizard.Initialize(config);
        _profiles.ConfigLoaded += _lizard.ApplyProfile;
        _lizard.Changed += UpdateStatus;

        // koło profili: logika w wątku kontrolera (Mapper), tu tylko nakładka i zmiana profilu
        _wheel = new WheelOverlay(ProfileManager.IconsDir(_profiles.Store));
        _worker.SetWheel(_profiles.Settings.Wheel);
        _profiles.WheelChanged += _worker.SetWheel;

        _statusItem = new ToolStripMenuItem("Kontroler: szukam…") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Wstrzymaj mapowanie", null, (_, _) => _worker.RequestTogglePause());
        _profilesItem = new ToolStripMenuItem("Profil");
        _profilesItem.DropDownItems.Add(new ToolStripMenuItem("…")); // żeby strzałka podmenu była widoczna od razu
        _profilesItem.DropDownOpening += (_, _) => BuildProfilesMenu();
        _filterItem = new ToolStripMenuItem("Filtr lizarda", null, (_, _) => _lizard.SetGlobalEnabled(!_lizard.GlobalEnabled));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_profilesItem);
        menu.Items.Add("Edytor mapowania…", null, (_, _) => ShowEditor());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Klawiatura ekranowa", null, (_, _) => _keyboard.Toggle());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_filterItem);
        menu.Items.Add("Wykryj mapę lizarda…", null, (_, _) => RunLizardWizard());
        menu.Items.Add("Podgląd kontrolera", null, (_, _) => ShowDebug());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edytuj plik profilu (Notatnik)…", null, (_, _) => EditProfileFile());
        menu.Items.Add("Otwórz folder profili", null, (_, _) => OpenProfilesFolder());
        menu.Items.Add("Przeładuj profil", null, (_, _) => _profiles.ReloadActive());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zamknij", null, (_, _) => ExitThread());

        _icon = Gfx.CreateAppIcon();
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "DeckMapper",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _keyboard.Toggle();
        };

        _profiles.Notify += (title, text, icon) => _tray.ShowBalloonTip(icon == ToolTipIcon.Warning ? 8000 : 2500, title, text, icon);
        _profiles.ActiveChanged += UpdateStatus;

        _mapper.KeyboardToggleRequested += () => OnUi(_keyboard.Toggle);
        _mapper.ProfileRequested += name => OnUi(() =>
        {
            if (name == null) _profiles.Next();
            else _profiles.Activate(name);
        });
        _mapper.EditorRequested += () => OnUi(ShowEditor);
        _mapper.WheelOpened += slots => OnUi(() => _wheel.Open(_profiles.WheelItems(slots)));
        _mapper.WheelSectorChanged += sector => OnUi(() => _wheel.SetSector(sector));
        _mapper.WheelClosed += profile => OnUi(() =>
        {
            if (profile == null)
            {
                _wheel.CloseWheel();
                return;
            }
            _wheel.Toast(profile);
            if (!string.Equals(profile, _profiles.ActiveName, StringComparison.OrdinalIgnoreCase))
                _profiles.Activate(profile, message: null); // napis na środku ekranu zamiast dymka
        });
        _mapper.PausedChanged += paused => OnUi(() =>
        {
            _pauseItem.Checked = paused;
            UpdateStatus();
        });
        _worker.ConnectionChanged += _ => OnUi(UpdateStatus);

        _worker.Start();
        UpdateStatus();

        // klawiatura USB: filtr połyka też jej Enter/Esc/strzałki – jednorazowy dymek
        _keyboardCheck = new Timer { Interval = 15_000 };
        _keyboardCheck.Tick += (_, _) => CheckExternalKeyboard();
        _keyboardCheck.Start();
        OnUi(CheckExternalKeyboard);

        if (!_profiles.Settings.LizardWizardOffered && !_lizard.HasMapFile) OnUi(OfferLizardWizard);

        if (errors.Count > 0)
        {
            var text = string.Join("\n", errors.Take(4));
            if (text.Length > 250) text = text[..250] + "…";
            _tray.ShowBalloonTip(8000, $"DeckMapper – problem z profilem {_profiles.ActiveName}", text, ToolTipIcon.Warning);
            foreach (var e in errors) Log.Info($"Profil {_profiles.ActiveName}: {e}");
        }
        else
        {
            _tray.ShowBalloonTip(3000, $"DeckMapper działa – profil {_profiles.ActiveName}",
                "Lewy klik na ikonce = klawiatura, prawy = menu.", ToolTipIcon.Info);
        }
    }

    void OnUi(Action action)
    {
        if (_ui.IsDisposed) return;
        try { _ui.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    void BuildProfilesMenu()
    {
        _profilesItem.DropDownItems.Clear();
        foreach (var name in _profiles.Names)
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => _profiles.Activate(name))
            {
                Checked = string.Equals(name, _profiles.ActiveName, StringComparison.OrdinalIgnoreCase),
            };
            _profilesItem.DropDownItems.Add(item);
        }
        _profilesItem.DropDownItems.Add(new ToolStripSeparator());
        _profilesItem.DropDownItems.Add("Następny profil", null, (_, _) => _profiles.Next());
        _profilesItem.DropDownItems.Add(new ToolStripMenuItem("Automatyczne przełączanie po programie", null, (_, _) =>
        {
            var edited = _profiles.Settings.Clone();
            edited.AutoSwitch = !edited.AutoSwitch;
            _profiles.UpdateAutoSwitch(edited);
        })
        { Checked = _profiles.Settings.AutoSwitch });
    }

    void RunLizardWizard()
    {
        if (_editor is { IsDisposed: false })
        {
            _editor.Activate();
            _editor.RunLizardWizard();
            return;
        }
        LizardWizard.Run(null, _worker, _lizard);
    }

    void OfferLizardWizard()
    {
        _profiles.MarkLizardWizardOffered();
        int choice = MessageDialog.Ask(null, "Mapa lizarda",
            "DeckMapper połyka klawisze, które kontroler sam wysyła w lizard mode (np. Enter pod A), " +
            "a mysz z touchpada zostawia. Teraz używa wbudowanej, niesprawdzonej listy (Enter, Esc, strzałki).\n\n" +
            "Wykryć dokładną mapę? Zajmie to minutę: wciśniesz po kolei każdy przycisk Decka. " +
            "Później: menu ikonki → Wykryj mapę lizarda.",
            ("Wykryj teraz", DarkButton.Style.Primary), ("Później", DarkButton.Style.Normal));
        if (choice == 0) RunLizardWizard();
    }

    void CheckExternalKeyboard()
    {
        if (_keyboardWarned || !_lizard.Plan.Active) return;
        var external = RawInputDevices.KeyboardPaths().Where(KeyboardDevices.IsExternalKeyboard).ToList();
        if (external.Count == 0) return;
        _keyboardWarned = true;
        Log.Info("Wykryto klawiaturę USB/Bluetooth: " + string.Join(" | ", external));
        _tray.ShowBalloonTip(10000, "Wykryto klawiaturę USB",
            "Filtr lizarda połyka też jej Enter/Esc/strzałki. Wyłącz filtr w menu ikonki, jeśli przeszkadza.",
            ToolTipIcon.Warning);
    }

    void UpdateStatus()
    {
        string status = _worker.Connected ? "Kontroler: połączony" : "Kontroler: szukam…";
        if (_editor is { IsDisposed: false }) status += " (edycja – wstrzymane)";
        else if (_mapper.Paused) status += " (wstrzymane)";
        _statusItem.Text = status;
        _profilesItem.Text = $"Profil: {_profiles.ActiveName}";

        var plan = _lizard.Plan;
        string filter = plan.Active ? "włączony" : "wyłączony";
        _filterItem.Text = $"Filtr lizarda: {filter}" + (plan.Active || !_lizard.GlobalEnabled ? "" : $" ({plan.Reason})");
        _filterItem.Checked = _lizard.GlobalEnabled;
        _filterItem.ToolTipText = _lizard.GlobalEnabled
            ? $"Kliknij, żeby wyłączyć (np. przy klawiaturze USB). {plan.Reason}. Mapa: {_lizard.MapSource}."
            : "Kliknij, żeby włączyć: klawisze lizarda (Enter pod A itd.) będą połykane, mysz z touchpada zostaje.";

        var tip = $"DeckMapper – {_profiles.ActiveName} – {status.Replace("Kontroler: ", "")} – filtr {(plan.Active ? "wł." : "wył.")}";
        _tray.Text = tip.Length > 63 ? tip[..62] + "…" : tip; // NotifyIcon.Text ma limit długości
    }

    void ShowEditor()
    {
        if (_editor == null || _editor.IsDisposed)
        {
            _editor = new EditorForm(_profiles, _worker, _keyboard, _icon, _lizard);
            _editor.FormClosed += (_, _) =>
            {
                _worker.SetSuspended(false);
                _editor = null;
                UpdateStatus();
            };
            _worker.SetSuspended(true);
            _editor.Show();
            UpdateStatus();
        }
        if (_editor.WindowState == FormWindowState.Minimized) _editor.WindowState = FormWindowState.Maximized;
        _editor.Activate();
    }

    void ShowDebug()
    {
        if (_debug == null || _debug.IsDisposed) _debug = new DebugForm(_worker, _mapper);
        _debug.Show();
        _debug.Activate();
    }

    void EditProfileFile()
    {
        try
        {
            var path = _profiles.Store.PathOf(_profiles.ActiveName);
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("Notatnik", e);
        }
    }

    void OpenProfilesFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_profiles.Store.Dir}\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("Folder profili", e);
        }
    }

    protected override void ExitThreadCore()
    {
        _editor?.Dispose();
        _keyboardCheck.Dispose();
        _wheel.Dispose();
        _profiles.Dispose();
        _lizard.Dispose();   // najpierw zdejmij hook
        _worker.Dispose();   // worker przy zamknięciu przywraca domyślny lizard (0x85 + 0x8E)
        _keyboard.Dispose();
        _debug?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _ui.Dispose();
        base.ExitThreadCore();
    }
}
