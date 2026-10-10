using System.Text.Json;
using System.Text.Json.Serialization;
using BroLedger.Core.Models;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java service/RoleService.java.
// Role display order is persisted in AppConfig.RoleOrder (list of UUIDs).

/// <summary>
/// Manages the global role catalogue, persisted to ~/.bro-ledger/roles.json.
/// Roles are universal across save files.
/// </summary>
public sealed class RoleService
{
    // ponytail: JUL → ILogger，默认 NullLogger；host DI 落地时替换
    private static readonly ILogger Log = NullLogger.Instance;

    private static RoleService? _instance;

    private static string DefaultFile() => Path.Combine(AppPaths.DataDir(), "roles.json");

    // Insertion-order store: id → Role。.NET 无 LinkedHashMap，用 List 保持插入序语义。
    private readonly List<Role> _store = [];
    private readonly string _rolesFile;

    private RoleService(string file)
    {
        _rolesFile = file;
        Load();
    }

    public static RoleService Instance => _instance ??= new(DefaultFile());

    /// <summary>For tests only — creates a fresh instance backed by a custom file path.</summary>
    internal static RoleService CreateForTest(string file) => _instance = new RoleService(file);

    /// <summary>For tests only — resets the singleton so the next Instance call starts fresh.</summary>
    internal static void ResetInstance() => _instance = null;

    // ---- query -------------------------------------------------------------

    /// <summary>
    /// All roles in user-defined order (AppConfig.RoleOrder).
    /// Falls back to insertion order when no custom order is configured.
    /// </summary>
    public List<Role> GetAll()
    {
        var order = AppConfig.Instance.RoleOrder;
        if (order == null || order.Count == 0)
        {
            return [.. _store];
        }
        var result = new List<Role>();
        var inOrder = new HashSet<string>(order);
        foreach (var id in order)
        {
            var r = GetById(id);
            if (r != null) result.Add(r);
        }
        // Append any roles not yet in RoleOrder (e.g. migrated from old data)
        foreach (var r in _store)
        {
            if (r.Id == null || !inOrder.Contains(r.Id)) result.Add(r);
        }
        return result;
    }

    public Role? GetById(string? id) => id != null ? _store.Find(r => r.Id == id) : null;

    /// <summary>
    /// The local role previously imported from <paramref name="packCode"/> as the sender's
    /// <paramref name="sourceRoleId"/>, or null when there is no such import (or either arg is null).
    /// </summary>
    public Role? FindBySource(string? packCode, string? sourceRoleId)
    {
        if (packCode == null || sourceRoleId == null) return null;
        foreach (var r in _store)
        {
            if (packCode == r.SourcePackCode && sourceRoleId == r.SourceRoleId)
            {
                return r;
            }
        }
        return null;
    }

    // ---- mutations ---------------------------------------------------------

    public Role Add(string? name, bool frontline)
    {
        var id = Guid.NewGuid().ToString();
        var r = new Role
        {
            Id = id,
            Name = name ?? "New Role",
            Frontline = frontline
        };
        _store.Add(r);
        var cfg = AppConfig.Instance;
        cfg.RoleOrder ??= [];
        cfg.RoleOrder.Add(id);
        cfg.Save();
        Flush();
        return r;
    }

    /// <summary>
    /// Imports the <paramref name="selected"/> subset of a fetched role pack. A role previously
    /// imported from the same pack (matched by <c>sourcePackCode</c> + the sender's role id) is
    /// updated <em>in place</em> — keeping its local UUID, so brothers' assignments follow the
    /// update. Otherwise a new role is created with a fresh UUID (never reusing the sender's
    /// id, so name collisions are harmless) and its provenance recorded for future re-imports.
    /// </summary>
    public List<Role> ImportPack(RolePack? pack, List<Role>? selected, string? sourcePackCode)
    {
        var affected = new List<Role>();
        if (pack == null || selected == null || selected.Count == 0) return affected;

        var cfg = AppConfig.Instance;
        cfg.RoleOrder ??= [];

        foreach (var src in selected)
        {
            var existing = FindBySource(sourcePackCode, src.Id);
            if (existing != null)
            {
                Role.CopyContentInto(src, existing);
                affected.Add(existing);
                continue;
            }
            var r = new Role { Id = Guid.NewGuid().ToString() };
            Role.CopyContentInto(src, r);
            r.SourcePackCode = sourcePackCode;
            r.SourceRoleId = src.Id;
            _store.Add(r);
            cfg.RoleOrder.Add(r.Id);
            affected.Add(r);
        }
        cfg.Save();
        Flush();
        return affected;
    }

    public void Update(Role? role)
    {
        if (role?.Id == null) return;
        // LinkedHashMap.put 语义：已有键原位替换，新键追加到末尾。
        var idx = _store.FindIndex(r => r.Id == role.Id);
        if (idx >= 0) _store[idx] = role;
        else _store.Add(role);
        Flush();
    }

    public void Delete(string? id)
    {
        _store.RemoveAll(r => r.Id == id);
        var cfg = AppConfig.Instance;
        if (cfg.RoleOrder != null)
        {
            if (id != null) cfg.RoleOrder.Remove(id);
            cfg.Save();
        }
        Flush();
    }

    /// <summary>Moves the role with the given id to <paramref name="newIndex"/> in the display order.</summary>
    public void Move(string id, int newIndex)
    {
        // Build from GetAll() so the working list is always in sync with the displayed order,
        // regardless of whether RoleOrder is null, empty, or only partially populated.
        var order = new List<string>();
        foreach (var r in GetAll()) order.Add(r.Id!);
        var current = order.IndexOf(id);
        if (current < 0) return;
        order.RemoveAt(current);
        newIndex = Math.Max(0, Math.Min(newIndex, order.Count));
        order.Insert(newIndex, id);
        var cfg = AppConfig.Instance;
        cfg.RoleOrder = order;
        cfg.Save();
        // Java 如此：move 只写 AppConfig，不 flush roles.json。
    }

    // ---- persistence -------------------------------------------------------

    private void Load()
    {
        if (!File.Exists(_rolesFile)) return;
        try
        {
            var w = JsonSerializer.Deserialize(File.ReadAllText(_rolesFile), DataJsonContext.Default.RoleServiceWrapper);
            if (w?.Roles != null)
            {
                foreach (var r in w.Roles)
                {
                    if (r.Id != null) _store.Add(r);
                }
            }
        }
        catch (Exception e)
        {
            Log.LogWarning("RoleService: load failed — {Message}", e.Message);
        }
    }

    private void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_rolesFile))!);
            var w = new Wrapper { Roles = [.. _store] };
            File.WriteAllText(_rolesFile, JsonSerializer.Serialize(w, DataJsonContext.Default.RoleServiceWrapper));
        }
        catch (Exception e)
        {
            Log.LogWarning("RoleService: flush failed — {Message}", e.Message);
        }
    }

    // ---- STJ wrapper -------------------------------------------------------

    public sealed class Wrapper
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("roles")] public List<Role> Roles { get; set; } = [];
    }
}
