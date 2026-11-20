using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DeckMapper;

/// <summary>settings.json obok exe: aktywny profil i automatyczne przełączanie po procesie.</summary>
sealed class AppSettings
{
    public string? ActiveProfile { get; set; }

    /// <summary>Przełączaj profil po nazwie procesu okna na pierwszym planie.</summary>
    public bool AutoSwitch { get; set; }

    /// <summary>Profil, gdy żadna reguła nie pasuje. null = zostaw bieżący.</summary>
    public string? DefaultProfile { get; set; }

    public List<ProcessRule> ProcessRules { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (settings != null)
                {
                    settings.ProcessRules = (settings.ProcessRules ?? new())
                        .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Process)).ToList();
                    return settings;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("Wczytywanie settings.json", e);
        }

        return new AppSettings
        {
            ProcessRules = { new ProcessRule { Process = "gta_sa.exe", Profile = "MTA" } },
        };
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Log.Error("Zapis settings.json", e);
        }
    }

    public AppSettings Clone() => new()
    {
        ActiveProfile = ActiveProfile,
        AutoSwitch = AutoSwitch,
        DefaultProfile = DefaultProfile,
        ProcessRules = ProcessRules.Select(r => new ProcessRule { Process = r.Process, Profile = r.Profile }).ToList(),
    };
}

sealed class ProcessRule
{
    public string Process { get; set; } = "";
    public string Profile { get; set; } = "";

    /// <summary>"GTA_SA.exe" i "gta_sa" to ten sam proces.</summary>
    public static string NormalizeProcess(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }
}
