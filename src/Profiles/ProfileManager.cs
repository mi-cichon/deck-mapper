using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Profile: aktywny profil, przełączanie, przeładowanie po zmianie pliku, automatyczne przełączanie
/// po procesie. Używany wyłącznie z wątku UI; nowy config trafia do workera przez zdarzenie ConfigLoaded.
/// </summary>
sealed class ProfileManager : IDisposable
{
    readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    readonly string _legacyPath = Path.Combine(AppContext.BaseDirectory, "mapping.json");
    readonly Action<Action> _onUi;
    readonly Timer _reloadTimer;
    readonly Timer _autoTimer;
    FileSystemWatcher? _watcher;

    string _loadedText = "";
    string? _lastForeground;

    public ProfileManager(Action<Action> onUi)
    {
        _onUi = onUi;
        Store = new ProfileStore(AppContext.BaseDirectory);
        Settings = new AppSettings();

        _reloadTimer = new Timer { Interval = 400 };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            CheckExternalChanges();
        };

        _autoTimer = new Timer { Interval = 1000 };
        _autoTimer.Tick += (_, _) => AutoSwitchTick();
    }

    public ProfileStore Store { get; }
    public AppSettings Settings { get; private set; }
    public string ActiveName { get; private set; } = ProfileStore.DefaultProfileName;

    public event Action<CompiledConfig>? ConfigLoaded;
    public event Action? ActiveChanged;
    /// <summary>Lista profili mogła się zmienić (dodanie, usunięcie, zmiana nazwy, zmiana pliku).</summary>
    public event Action? ProfilesChanged;
    public event Action<string, string, ToolTipIcon>? Notify;
    /// <summary>Zmieniły się sloty koła profili (edycja, zmiana nazwy/usunięcie profilu).</summary>
    public event Action<WheelSettings>? WheelChanged;

    public List<string> Names => Store.List();

    public bool ProfileExists(string name) => Store.Find(name) != null;

    /// <summary>Migracja, wczytanie ustawień i aktywnego profilu. Zwraca config dla Mappera.</summary>
    public CompiledConfig Initialize(List<string> errors)
    {
        try
        {
            Store.EnsureCreated(_legacyPath);
        }
        catch (Exception e)
        {
            Log.Error("Tworzenie folderu profili", e);
            errors.Add("Nie udało się utworzyć folderu profiles: " + e.Message);
        }

        Settings = AppSettings.Load(_settingsPath);
        var removed = new List<string>();
        Settings.Wheel.Slots = WheelSlots.Normalize(Settings.Wheel.Slots, Store.Find, removed);
        foreach (var r in removed) Log.Info($"Koło profili: profil „{r}” nie istnieje – slot wyczyszczony");
        var name = Store.Find(Settings.ActiveProfile) ?? Store.Find(ProfileStore.DefaultProfileName) ?? Names.FirstOrDefault();

        CompiledConfig? config = null;
        if (name != null)
        {
            ActiveName = name;
            config = LoadProfile(name, errors, out _loadedText);
        }
        if (Settings.ActiveProfile != ActiveName)
        {
            Settings.ActiveProfile = ActiveName;
            SaveSettings();
        }

        StartWatcher();
        _autoTimer.Start();
        return config ?? ConfigLoader.LoadDefault();
    }

    CompiledConfig? LoadProfile(string name, List<string> errors, out string text)
    {
        var raw = ConfigLoader.LoadRaw(Store.PathOf(name), errors, out text);
        return raw == null ? null : ConfigLoader.Compile(raw, errors, ProfileExists);
    }

    /// <summary>Wczytuje i włącza profil. message = treść dymka (null = bez dymka, chyba że są błędy).</summary>
    public bool Activate(string name, string? message = "Aktywny profil")
    {
        var canonical = Store.Find(name);
        if (canonical == null)
        {
            Notify?.Invoke("DeckMapper", $"Nie ma profilu „{name}”.", ToolTipIcon.Warning);
            return false;
        }

        var errors = new List<string>();
        var config = LoadProfile(canonical, errors, out var text);
        if (config == null)
        {
            ShowErrors(canonical, errors);
            return false;
        }

        bool changed = !string.Equals(canonical, ActiveName, StringComparison.Ordinal);
        ActiveName = canonical;
        _loadedText = text;
        if (Settings.ActiveProfile != canonical)
        {
            Settings.ActiveProfile = canonical;
            SaveSettings();
        }

        ConfigLoaded?.Invoke(config);
        if (changed) ActiveChanged?.Invoke();

        if (errors.Count > 0) ShowErrors(canonical, errors);
        else if (message != null) Notify?.Invoke($"Profil: {canonical}", message, ToolTipIcon.Info);
        return true;
    }

    public void Next()
    {
        var names = Names;
        if (names.Count == 0) return;
        int at = names.FindIndex(n => string.Equals(n, ActiveName, StringComparison.OrdinalIgnoreCase));
        Activate(names[(at + 1) % names.Count]);
    }

    public void ReloadActive() => Activate(ActiveName, "Profil przeładowany.");

    void ShowErrors(string profile, List<string> errors)
    {
        var text = string.Join("\n", errors.Take(4));
        if (errors.Count > 4) text += $"\n…i jeszcze {errors.Count - 4}";
        if (text.Length > 250) text = text[..250] + "…";
        Notify?.Invoke($"DeckMapper – problem z profilem {profile}", text, ToolTipIcon.Warning);
        foreach (var e in errors) Log.Info($"Profil {profile}: {e}");
    }

    // ---------- operacje na plikach (z edytora) ----------

    public void Save(string name, RawConfig raw)
    {
        Store.Save(name, raw);
        ProfilesChanged?.Invoke();
        if (string.Equals(name, ActiveName, StringComparison.OrdinalIgnoreCase))
            Activate(ActiveName, "Zapisano i przeładowano.");
    }

    public void Create(string name, RawConfig raw)
    {
        Store.Save(name, raw);
        ProfilesChanged?.Invoke();
    }

    public void Duplicate(string source, string target)
    {
        Store.Duplicate(source, target);
        ProfilesChanged?.Invoke();
    }

    public void Rename(string oldName, string newName)
    {
        Store.Rename(oldName, newName);

        foreach (var rule in Settings.ProcessRules)
            if (string.Equals(rule.Profile, oldName, StringComparison.OrdinalIgnoreCase))
                rule.Profile = newName;
        if (string.Equals(Settings.DefaultProfile, oldName, StringComparison.OrdinalIgnoreCase))
            Settings.DefaultProfile = newName;
        Settings.Wheel.Slots = WheelSlots.Rename(Settings.Wheel.Slots, oldName, newName);

        bool wasActive = string.Equals(ActiveName, oldName, StringComparison.OrdinalIgnoreCase);
        if (wasActive)
        {
            ActiveName = newName;
            Settings.ActiveProfile = newName;
        }
        SaveSettings();
        ProfilesChanged?.Invoke();
        WheelChanged?.Invoke(Settings.Wheel);
        if (wasActive) ActiveChanged?.Invoke();
    }

    public void Delete(string name)
    {
        bool wasActive = string.Equals(ActiveName, name, StringComparison.OrdinalIgnoreCase);
        Store.Delete(name);

        Settings.ProcessRules.RemoveAll(r => string.Equals(r.Profile, name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(Settings.DefaultProfile, name, StringComparison.OrdinalIgnoreCase))
            Settings.DefaultProfile = null;
        Settings.Wheel.Slots = WheelSlots.Remove(Settings.Wheel.Slots, name);
        SaveSettings();
        ProfilesChanged?.Invoke();
        WheelChanged?.Invoke(Settings.Wheel);

        if (wasActive)
        {
            var other = Names.FirstOrDefault();
            if (other != null) Activate(other);
        }
    }

    public void UpdateAutoSwitch(AppSettings edited)
    {
        Settings.AutoSwitch = edited.AutoSwitch;
        Settings.DefaultProfile = string.IsNullOrWhiteSpace(edited.DefaultProfile) ? null : edited.DefaultProfile;
        Settings.ProcessRules = edited.Clone().ProcessRules;
        _lastForeground = null; // sprawdź od nowa przy następnym tyknięciu
        SaveSettings();
    }

    public void SetWheelSlots(List<string?> slots)
    {
        Settings.Wheel.Slots = WheelSlots.Normalize(slots, Store.Find);
        SaveSettings();
        WheelChanged?.Invoke(Settings.Wheel);
    }

    public static string IconsDir(ProfileStore store) => Path.Combine(store.Dir, "icons");

    /// <summary>Sloty koła do narysowania: etykieta i ikona z pliku profilu, oznaczenie aktywnego.</summary>
    public List<WheelItem> WheelItems(IReadOnlyList<string?>? slots = null)
    {
        var items = new List<WheelItem>();
        foreach (var slot in slots ?? Settings.Wheel.Slots)
        {
            var name = slot == null ? null : Store.Find(slot);
            if (name == null)
            {
                items.Add(WheelItem.EmptySlot);
                continue;
            }
            var raw = ConfigLoader.LoadRaw(Store.PathOf(name), new List<string>(), out _);
            items.Add(new WheelItem(name, WheelLabels.For(name, raw?.WheelLabel), raw?.WheelIcon,
                string.Equals(name, ActiveName, StringComparison.OrdinalIgnoreCase)));
        }
        return items;
    }

    public void SetLizardFilterEnabled(bool enabled)
    {
        Settings.LizardFilterEnabled = enabled;
        SaveSettings();
    }

    public void MarkLizardWizardOffered()
    {
        Settings.LizardWizardOffered = true;
        SaveSettings();
    }

    void SaveSettings() => Settings.Save(_settingsPath);

    // ---------- zmiany plików z zewnątrz ----------

    void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(Store.Dir, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            FileSystemEventHandler changed = (_, _) => _onUi(ScheduleCheck);
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (_, _) => _onUi(ScheduleCheck);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            Log.Error("Obserwowanie folderu profili", e);
        }
    }

    void ScheduleCheck()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    void CheckExternalChanges()
    {
        ProfilesChanged?.Invoke();

        var path = Store.PathOf(ActiveName);
        if (!File.Exists(path)) return; // usunięty z zewnątrz – zostaje wczytana wersja

        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException) { ScheduleCheck(); return; } // plik jeszcze zapisywany

        // własny zapis z edytora albo zmiana innego pliku – nic do roboty
        if (text == _loadedText) return;
        Activate(ActiveName, "Plik zmieniony – profil przeładowany.");
    }

    // ---------- automatyczne przełączanie ----------

    void AutoSwitchTick()
    {
        if (!Settings.AutoSwitch)
        {
            _lastForeground = null;
            return;
        }

        var process = ForegroundProcess.GetName(out bool own);
        if (own || process == null) return; // nasze okna (edytor) nie zmieniają profilu

        process = ProcessRule.NormalizeProcess(process);
        if (process == _lastForeground) return; // reagujemy tylko na zmianę okna – ręczny wybór zostaje
        _lastForeground = process;

        var rule = Settings.ProcessRules.FirstOrDefault(r => ProcessRule.NormalizeProcess(r.Process) == process);
        var target = rule?.Profile ?? Settings.DefaultProfile;
        if (string.IsNullOrWhiteSpace(target)) return;

        var canonical = Store.Find(target);
        if (canonical == null || string.Equals(canonical, ActiveName, StringComparison.Ordinal)) return;
        Activate(canonical, $"Przełączono automatycznie ({process}.exe).");
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _reloadTimer.Dispose();
        _autoTimer.Dispose();
    }
}
