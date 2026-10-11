using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java service/RoleShareService.java.
// Exception mapping: NoSuchElementException → KeyNotFoundException,
// IllegalArgumentException → ArgumentException, IOException → System.IO.IOException.
// Exception message text is verbatim from the Java original (em-dashes included).

/// <summary>
/// Publishes and fetches role packs against the serverless role-share store.
/// The store is opaque JSON keyed by a short share code — see server/ (Cloudflare Worker).
/// </summary>
public sealed partial class RoleShareService
{
    /// <summary>Public base URL of the deployed role-share Worker. Swappable for tests via CreateForTest.</summary>
    public const string DefaultBaseUrl = "https://bro-ledger-roles.niclas-cf8.workers.dev";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Sanity cap on roles accepted from a fetched pack — the store's contents are untrusted.</summary>
    private const int MaxPackRoles = 100;

    /// <summary>Sanity cap on a role's name length in a fetched pack.</summary>
    private const int MaxRoleNameLength = 60;

    /// <summary>Sanity cap on the number of perk-plan entries accepted from a fetched pack.</summary>
    private const int MaxPerkPlanEntries = 100;

    /// <summary>Sanity cap on a target-stat threshold accepted from a fetched pack.</summary>
    private const int MaxTargetStat = 999;

    private static RoleShareService? _instance;

    private readonly string _baseUrl;
    private readonly HttpClient _client;

    private RoleShareService(string baseUrl)
    {
        _baseUrl = baseUrl;
        // Java: HttpClient.newBuilder().connectTimeout(10s) + per-request 20s timeout.
        _client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) })
        {
            Timeout = RequestTimeout,
        };
    }

    public static RoleShareService Instance
    {
        get
        {
            if (_instance == null)
            {
                var overrideUrl = Environment.GetEnvironmentVariable("BROLEDGER_ROLESHARE_BASEURL");
                _instance = new RoleShareService(
                    string.IsNullOrWhiteSpace(overrideUrl) ? DefaultBaseUrl : overrideUrl);
            }
            return _instance;
        }
    }

    /// <summary>For tests only — creates a fresh instance pointed at a local stub URL.</summary>
    internal static RoleShareService CreateForTest(string baseUrl) => _instance = new RoleShareService(baseUrl);

    /// <summary>For tests only — resets the singleton so the next Instance call starts fresh.</summary>
    internal static void ResetInstance() => _instance = null;

    /// <summary>
    /// Normalizes raw user input to canonical share-code form (trimmed, upper-cased),
    /// or returns null when the input cannot be a valid code.
    /// </summary>
    public static string? NormalizeCode(string? raw)
    {
        if (raw == null) return null;
        var code = raw.Trim().ToUpperInvariant();
        return CodePattern().IsMatch(code) ? code : null;
    }

    // Mirrors the Worker's code format: 8 chars from a 32-char alphabet (no 0/O/1/I).
    // Java Pattern.matches() is full-match; the anchors are the same contract.
    [GeneratedRegex("^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{8}$")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Result of a successful publish: the share code, plus the server-issued owner token
    /// that authorizes <see cref="Update"/> on that code — or a null token when the server
    /// predates updates (the pack is then immutable).
    /// </summary>
    public sealed record PublishResult(string Code, string? OwnerToken);

    /// <summary>
    /// Publishes a role pack and returns its share code and owner token.
    /// Blocking — callers must invoke this off the UI thread.
    /// </summary>
    public PublishResult Publish(RolePack pack)
    {
        var body = JsonSerializer.Serialize(pack, DataJsonContext.Default.RolePack);
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/packs")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        var response = Send(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new IOException("Publish failed: HTTP " + (int)response.StatusCode);
        }
        var node = JsonNode.Parse(ReadBody(response));
        if (node == null || node["code"] == null)
        {
            throw new IOException("Publish failed: no code in response");
        }
        var tokenNode = node["ownerToken"];
        return new PublishResult(node["code"]!.GetValue<string>(), tokenNode?.GetValue<string>());
    }

    /// <summary>
    /// Overwrites the pack stored under <paramref name="rawCode"/> with new contents,
    /// authorized by the owner token issued when the code was first published. Everyone
    /// holding the code sees the new contents on their next fetch.
    /// Blocking — callers must invoke this off the UI thread.
    /// </summary>
    public void Update(string? rawCode, string? ownerToken, RolePack pack)
    {
        var code = NormalizeCode(rawCode);
        if (code == null)
        {
            throw new ArgumentException("Not a valid share code: " + rawCode);
        }
        if (string.IsNullOrEmpty(ownerToken))
        {
            throw new ArgumentException("No owner token for code " + code);
        }
        var body = JsonSerializer.Serialize(pack, DataJsonContext.Default.RolePack);
        using var request = new HttpRequestMessage(HttpMethod.Put, _baseUrl + "/packs/" + code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Owner-Token", ownerToken);
        var response = Send(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException(
                "Code " + code + " has expired — publish as a new code instead.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new IOException("Code " + code + " can't be updated from this computer.");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new IOException("Update failed: HTTP " + (int)response.StatusCode);
        }
    }

    /// <summary>
    /// Fetches a role pack by its share code. The code is normalized first; an input that
    /// cannot be a valid code fails fast with <see cref="ArgumentException"/>, without a
    /// network round-trip (and without ever reaching URI construction).
    /// Blocking — callers must invoke this off the UI thread.
    /// </summary>
    public RolePack Fetch(string? rawCode)
    {
        var code = NormalizeCode(rawCode);
        if (code == null)
        {
            throw new ArgumentException("Not a valid share code: " + rawCode);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/packs/" + code);
        var response = Send(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException("No role pack found for code: " + code);
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new IOException("Fetch failed: HTTP " + (int)response.StatusCode);
        }
        return Sanitize(JsonSerializer.Deserialize(ReadBody(response), DataJsonContext.Default.RolePack));
    }

    /// <summary>
    /// Hardens a fetched pack against malformed or hostile content — the store accepts any
    /// well-formed JSON from anyone, so the deserialized shape cannot be trusted: roles
    /// may be null or contain nulls, names may be missing, and the per-stat arrays may have
    /// the wrong length (UI code indexes them 0–7) or out-of-range values. Packs declaring a
    /// schema version newer than this build understands are rejected outright; oversized packs
    /// are truncated and flagged via <see cref="RolePack.Truncated"/>.
    /// </summary>
    internal static RolePack Sanitize(RolePack? pack)
    {
        if (pack == null) throw new IOException("Fetch failed: empty pack");
        if (pack.PackSchemaVersion > RolePack.SchemaVersion)
        {
            throw new IOException("This pack was made with a newer version of the app"
                + " (pack schema " + pack.PackSchemaVersion + ", supported " + RolePack.SchemaVersion + ")");
        }
        pack.Roles ??= [];
        pack.Roles.RemoveAll(r => r == null);
        if (pack.Roles.Count > MaxPackRoles)
        {
            pack.Roles = pack.Roles.Take(MaxPackRoles).ToList();
            pack.MarkTruncated();
        }
        foreach (var role in pack.Roles)
        {
            if (role.Name == null) role.Name = "";
            if (role.Name.Length > MaxRoleNameLength) role.Name = role.Name[..MaxRoleNameLength];
            role.TargetStats = NormalizedTargets(role.TargetStats);
            role.Priority = NormalizedPriorities(role.Priority);
            role.PerkPlanTemplate = NormalizedPerkPlan(role.PerkPlanTemplate);
        }
        return pack;
    }

    /// <summary>
    /// Length-8 guarantee for target stats; thresholds are clamped to
    /// [0, MaxTargetStat] (negative = not set, oversized values capped).
    /// </summary>
    private static int[] NormalizedTargets(int[]? a)
    {
        if (a == null || a.Length != 8) return new int[8];
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] < 0) a[i] = 0;
            else if (a[i] > MaxTargetStat) a[i] = MaxTargetStat;
        }
        return a;
    }

    /// <summary>
    /// Keeps only well-formed perk-plan entries — non-null keys with a value of
    /// "OPTIONAL" or "PLANNED" — capped at <see cref="MaxPerkPlanEntries"/>.
    /// Returns null when the input is null/empty or nothing survives filtering.
    /// </summary>
    private static Dictionary<string, string>? NormalizedPerkPlan(Dictionary<string, string>? entries)
    {
        if (entries == null || entries.Count == 0) return null;
        var accepted = new Dictionary<string, string>();
        foreach (var entry in entries)
        {
            if (entry.Key == null) continue; // unreachable for a .NET Dictionary; port fidelity
            if (entry.Value is not ("OPTIONAL" or "PLANNED")) continue;
            accepted.Add(entry.Key, entry.Value);
            if (accepted.Count >= MaxPerkPlanEntries) break;
        }
        return accepted.Count == 0 ? null : accepted;
    }

    /// <summary>Length-8 guarantee for priorities; values clamped to the valid 1–3 range.</summary>
    private static int[] NormalizedPriorities(int[]? a)
    {
        if (a == null || a.Length != 8) return [3, 3, 3, 3, 3, 3, 3, 3];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = Math.Max(1, Math.Min(3, a[i]));
        }
        return a;
    }

    private HttpResponseMessage Send(HttpRequestMessage request) => _client.Send(request);

    // HttpClient.Send is synchronous; read the body without sync-over-async.
    private static string ReadBody(HttpResponseMessage response)
    {
        using var reader = new StreamReader(response.Content.ReadAsStream());
        return reader.ReadToEnd();
    }
}
