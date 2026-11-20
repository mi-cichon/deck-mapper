using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeckMapper;

/// <summary>
/// Lizard mode + filtr klawiszy (wątek UI). Mysz zostaje sprzętowa (lizard w całości – tylko taką mysz MTA
/// przyjmuje na serwerze), a klawisze lizarda połyka LizardKeyFilter według planu z LizardFilterPlanner.
/// Kontroler: tylko przywracanie domyślnego lizarda (0x85 + 0x8E) – przy połączeniu (worker), przy zmianie
/// trybu filtra i przy zamknięciu (worker). Żadnego 0x81 w normalnym działaniu.
/// </summary>
sealed class LizardService : IDisposable
{
    readonly ControllerWorker _worker;
    readonly ProfileManager _profiles;
    readonly LizardKeyFilter _filter = new();
    LizardProfileSettings _profile = LizardProfileSettings.Default;
    bool _initialized;

    public LizardService(ControllerWorker worker, ProfileManager profiles)
    {
        _worker = worker;
        _profiles = profiles;
        Map = LizardMap.BuiltIn;
        MapSource = "wbudowana";
        Plan = LizardFilterPlanner.Plan(Map, _profile, false);
    }

    public static string MapPath => Path.Combine(AppContext.BaseDirectory, LizardMap.FileName);

    public LizardMap Map { get; private set; }
    public string MapSource { get; private set; }
    public SwallowPlan Plan { get; private set; }
    public bool GlobalEnabled => _profiles.Settings.LizardFilterEnabled;
    public bool HasMapFile => File.Exists(MapPath);

    /// <summary>Zmienił się plan, mapa albo przełącznik – do odświeżenia menu, tooltipa i edytora.</summary>
    public event Action? Changed;

    public void Initialize(CompiledConfig config)
    {
        _profile = config.Lizard;
        LoadMap();
        _initialized = true;
        Recompute("start aplikacji", restoreOnChange: false); // worker i tak przywraca lizarda przy połączeniu
    }

    public void ApplyProfile(CompiledConfig config)
    {
        _profile = config.Lizard;
        Recompute("zmiana profilu");
    }

    public void SetGlobalEnabled(bool enabled)
    {
        if (enabled == GlobalEnabled) return;
        _profiles.SetLizardFilterEnabled(enabled);
        Recompute(enabled ? "włączony w menu" : "wyłączony w menu");
    }

    public void ReloadMap()
    {
        LoadMap();
        Recompute("nowa mapa lizarda");
    }

    public void SaveMap(LizardMap map)
    {
        map.Save(MapPath);
        Log.Info($"Mapa lizarda zapisana: {MapPath} ({map.Outputs.Count} kontrolek, klawisze: " +
                 $"{string.Join(", ", map.Keys.Select(k => k.Name))})");
        ReloadMap();
    }

    /// <summary>Kreator mapy: filtr nic nie połyka, żeby było widać, co wysyła lizard.</summary>
    public bool FilterPaused
    {
        get => _filter.Paused;
        set => _filter.Paused = value;
    }

    /// <summary>Plan dla edytowanego (jeszcze niezapisanego) profilu – do plakietek i ostrzeżeń w edytorze.</summary>
    public SwallowPlan PlanFor(LizardProfileSettings profile) => LizardFilterPlanner.Plan(Map, profile, GlobalEnabled);

    void LoadMap()
    {
        var errors = new List<string>();
        Map = LizardMap.LoadOrBuiltIn(MapPath, errors, out var source);
        MapSource = source;
        foreach (var e in errors) Log.Info("Mapa lizarda: " + e);
        Log.Info($"Mapa lizarda: {source}; klawiszy: {Map.Keys.Count}");
    }

    void Recompute(string why, bool restoreOnChange = true)
    {
        if (!_initialized) return;
        var old = Plan;
        var plan = LizardFilterPlanner.Plan(Map, _profile, GlobalEnabled);
        bool activeChanged = plan.Active != old.Active;
        bool codesChanged = !plan.Codes.SetEquals(old.Codes);
        Plan = plan;
        _filter.Apply(plan);

        if (activeChanged || codesChanged || !restoreOnChange)
        {
            Log.Info($"Filtr lizarda: {(plan.Active ? "WŁĄCZONY" : "wyłączony")} ({why}) – {plan.Reason}");
            foreach (var w in plan.Warnings) Log.Info("Filtr lizarda: " + w);
        }
        // zmiana trybu (filtr ↔ lizard bez filtra): kontroler do znanego, domyślnego stanu
        if (activeChanged && restoreOnChange) _worker.RequestLizardRestore($"zmiana trybu: {why}");

        Changed?.Invoke();
    }

    public void Dispose() => _filter.Dispose();
}
