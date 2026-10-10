using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

/// <summary>
/// One entry in the local "recent uploads" history (<c>AppConfig.RecentShares</c>):
/// a pack published from this machine, plus the owner token that authorizes
/// updating it on the server. The token is a machine-local secret — it is never
/// part of a RolePack and never shown to recipients of the code.
/// Port of JAVA/model/ShareRecord.java.
/// </summary>
public class ShareRecord
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Server-issued update secret; null when the server didn't return one.</summary>
    [JsonPropertyName("ownerToken")]
    public string? OwnerToken { get; set; }

    /// <summary>The pack's title (RolePack.label) at publish/update time; may be null.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>ISO-8601 instant of the most recent publish or update.</summary>
    [JsonPropertyName("uploadedAt")]
    public string? UploadedAt { get; set; }

    [JsonPropertyName("roleCount")]
    public int RoleCount { get; set; }

    public ShareRecord() { }

    public ShareRecord(string? code, string? ownerToken, string? title, string? uploadedAt, int roleCount)
    {
        Code = code;
        OwnerToken = ownerToken;
        Title = title;
        UploadedAt = uploadedAt;
        RoleCount = roleCount;
    }

    /// <summary>True when this entry carries the secret needed to update its code on the server.</summary>
    public bool Updatable() => !string.IsNullOrEmpty(OwnerToken);
}
