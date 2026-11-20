using System.Collections.Generic;

namespace DeckMapper;

/// <summary>
/// Licznik „ile kontrolek trzyma ten klawisz”. Ten sam klawisz bywa przypisany do kilku kontrolek
/// (np. W na RT i na lewej gałce ↑): bez licznika puszczenie gałki wysyłało W↑, choć RT dalej był trzymany,
/// i auto przestawało jechać. Wciśnięcie wysyłamy przy 0 → 1, puszczenie przy 1 → 0.
/// </summary>
sealed class HeldCounter<T> where T : notnull
{
    readonly Dictionary<T, int> _counts = new();

    /// <summary>true = pierwsza kontrolka trzyma ten klawisz – trzeba wysłać wciśnięcie.</summary>
    public bool Press(T key)
    {
        _counts.TryGetValue(key, out int n);
        _counts[key] = n + 1;
        return n == 0;
    }

    /// <summary>true = ostatnia kontrolka puściła klawisz – trzeba wysłać puszczenie.</summary>
    public bool Release(T key)
    {
        if (!_counts.TryGetValue(key, out int n) || n <= 0) return false;
        if (n == 1)
        {
            _counts.Remove(key);
            return true;
        }
        _counts[key] = n - 1;
        return false;
    }

    public int CountOf(T key) => _counts.TryGetValue(key, out int n) ? n : 0;

    public bool IsEmpty => _counts.Count == 0;
}
