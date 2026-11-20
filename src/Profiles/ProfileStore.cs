using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DeckMapper;

/// <summary>Folder profiles/ obok exe – jeden plik JSON na profil.</summary>
sealed class ProfileStore
{
    public const string DefaultProfileName = "MTA";

    public ProfileStore(string baseDir)
    {
        Dir = Path.Combine(baseDir, "profiles");
    }

    public string Dir { get; }

    public string PathOf(string name) => Path.Combine(Dir, name + ".json");

    public List<string> List()
    {
        try
        {
            if (!Directory.Exists(Dir)) return new List<string>();
            return Directory.GetFiles(Dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Error("Lista profili", e);
            return new List<string>();
        }
    }

    /// <summary>Nazwa profilu z właściwą wielkością liter albo null, gdy go nie ma.</summary>
    public string? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        return List().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Zwraca opis błędu albo null, gdy nazwa jest poprawna i wolna.</summary>
    public string? ValidateNewName(string name, string? renaming = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "Nazwa nie może być pusta.";
        if (name.Length > 40) return "Nazwa może mieć najwyżej 40 znaków.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' }) >= 0)
            return "Nazwa zawiera niedozwolone znaki (\\ / : * ? \" < > |).";
        if (name.EndsWith('.') || name.StartsWith('.')) return "Nazwa nie może zaczynać się ani kończyć kropką.";
        var existing = Find(name);
        if (existing != null && !string.Equals(existing, renaming, StringComparison.OrdinalIgnoreCase))
            return $"Profil „{existing}” już istnieje.";
        return null;
    }

    /// <summary>
    /// Pierwsze uruchomienie: tworzy folder i przenosi stary mapping.json do profiles/MTA.json
    /// (oryginał zostaje jako mapping.json.bak). Bez starego pliku zapisuje profil domyślny.
    /// </summary>
    public void EnsureCreated(string legacyMappingPath)
    {
        Directory.CreateDirectory(Dir);
        if (List().Count > 0) return;

        var target = PathOf(DefaultProfileName);
        if (File.Exists(legacyMappingPath))
        {
            File.Copy(legacyMappingPath, target, overwrite: false);
            try
            {
                File.Move(legacyMappingPath, legacyMappingPath + ".bak", overwrite: true);
            }
            catch (Exception e)
            {
                Log.Error("Zmiana nazwy mapping.json", e);
            }
            Log.Info($"Zmigrowano {legacyMappingPath} do {target}");
        }
        else
        {
            File.WriteAllText(target, DefaultConfig.Json, new UTF8Encoding(false));
        }
    }

    public void Save(string name, RawConfig raw) => ConfigLoader.Save(PathOf(name), raw);

    public void Duplicate(string source, string target) => File.Copy(PathOf(source), PathOf(target), overwrite: false);

    public void Rename(string oldName, string newName)
    {
        string from = PathOf(oldName), to = PathOf(newName);
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            // sama zmiana wielkości liter – przez plik tymczasowy, żeby Windows ją zauważył
            string temp = Path.Combine(Dir, Guid.NewGuid().ToString("N") + ".tmp");
            File.Move(from, temp);
            File.Move(temp, to);
        }
        else
        {
            File.Move(from, to);
        }
    }

    public void Delete(string name) => File.Delete(PathOf(name));
}
