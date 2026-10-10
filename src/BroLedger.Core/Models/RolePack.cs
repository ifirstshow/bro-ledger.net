using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

// Transcribed method-by-method from Java model/RolePack.java

/// <summary>A shareable collection of roles, published to and fetched from the role-share server.</summary>
public class RolePack
{
    /// <summary>Highest pack schema this build can understand; fetched packs above it are rejected.</summary>
    public const int SchemaVersion = 1;

    // Java 的实例字段与常量同名（static final vs instance）；C# 不能重名，实例字段取 PackSchemaVersion。
    [JsonPropertyName("schemaVersion")] public int PackSchemaVersion { get; set; } = SchemaVersion;

    [JsonPropertyName("label")] public string? Label { get; set; }

    [JsonPropertyName("roles")] public List<Role> Roles { get; set; } = [];

    // Set during sanitization when roles beyond the cap were dropped. Private field with
    // non-property accessors so STJ never serializes it (Jackson does the same).
    private bool _truncated;

    public RolePack() { }

    public RolePack(string? label, List<Role>? roles)
    {
        Label = label;
        Roles = roles ?? [];
    }

    /// <summary>
    /// Builds a pack for publishing. Roles are copied — keeping their catalogue ids, which
    /// recipients record as provenance so a re-import of the same code updates in place —
    /// but the local provenance fields are stripped, so the code of a pack the sender once
    /// imported from never leaks into a pack they publish.
    /// </summary>
    public static RolePack ForSharing(string? label, IEnumerable<Role?>? roles)
    {
        var copies = new List<Role>();
        if (roles != null)
        {
            foreach (var src in roles)
            {
                if (src == null) continue;
                var copy = new Role { Id = src.Id };
                Role.CopyContentInto(src, copy);
                copies.Add(copy);
            }
        }
        return new RolePack(label, copies);
    }

    public bool Truncated() => _truncated;

    public void MarkTruncated() => _truncated = true;
}
