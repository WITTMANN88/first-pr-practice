using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Persists which tweaks are applied and the original registry values captured
/// at apply time. Because most tweaks live in HKLM (machine-wide), the store is
/// kept under %ProgramData% so any elevated user sees the same rollback data.
///
/// This is what makes "Отменить всё" (revert all) reliable even after the app
/// has been closed and reopened.
/// </summary>
public sealed class TweakStateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, TweakState> _state;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public TweakStateStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "STAKEOUT");
        try { Directory.CreateDirectory(dir); } catch { /* handled on save */ }
        _path = Path.Combine(dir, "tweak-state.json");
        _state = Load();
    }

    private Dictionary<string, TweakState> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, TweakState>>(json, JsonOpts)
                       ?? new();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("TweakStateStore.Load", ex);
        }
        return new();
    }

    private void Save()
    {
        try
        {
            lock (_gate)
            {
                var json = JsonSerializer.Serialize(_state, JsonOpts);
                File.WriteAllText(_path, json);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("TweakStateStore.Save", ex);
        }
    }

    public bool IsApplied(string tweakId)
        => _state.TryGetValue(tweakId, out var s) && s.Applied;

    public TweakState GetOrCreate(string tweakId)
    {
        if (!_state.TryGetValue(tweakId, out var s))
        {
            s = new TweakState();
            _state[tweakId] = s;
        }
        return s;
    }

    public void MarkApplied(string tweakId, TweakState state)
    {
        state.Applied = true;
        state.AppliedUtc = DateTime.UtcNow;
        _state[tweakId] = state;
        Save();
    }

    public void MarkReverted(string tweakId)
    {
        if (_state.TryGetValue(tweakId, out var s))
        {
            s.Applied = false;
            s.Saved.Clear();
            s.Notes.Clear();
        }
        Save();
    }

    public IEnumerable<string> AppliedTweakIds()
        => _state.Where(kv => kv.Value.Applied).Select(kv => kv.Key).ToList();

    // --- snapshot (de)serialization helpers --------------------------------

    /// <summary>Convert a live registry snapshot into a serializable record.</summary>
    public static SavedValue ToSaved(RegistryHive hive, string subKey, string name,
        RegistryHelper.ValueSnapshot snap)
    {
        string? encoded = null;
        if (snap.Existed && snap.Value != null)
        {
            var bytes = RegistryValueCodec.Encode(snap.Value, snap.Kind);
            encoded = Convert.ToBase64String(bytes);
        }
        return new SavedValue
        {
            Hive = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
            SubKey = subKey,
            Name = name,
            Existed = snap.Existed,
            Kind = snap.Kind.ToString(),
            ValueBase64 = encoded
        };
    }

    /// <summary>Restore a saved value back into the registry.</summary>
    public static bool RestoreSaved(SavedValue sv)
    {
        var hive = sv.Hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        if (!sv.Existed || sv.ValueBase64 == null)
            return RegistryHelper.DeleteValue(hive, sv.SubKey, sv.Name);

        var kind = Enum.TryParse<RegistryValueKind>(sv.Kind, out var k)
            ? k : RegistryValueKind.String;
        var bytes = Convert.FromBase64String(sv.ValueBase64);
        var value = RegistryValueCodec.Decode(bytes, kind);
        return RegistryHelper.SetValue(hive, sv.SubKey, sv.Name, value, kind);
    }
}
