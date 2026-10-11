using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BroLedger.App.ViewModels;

/// <summary>One checklist entry — everything checked by default (Java setRoles).</summary>
internal sealed partial class ShareCheckRole : ObservableObject
{
    public ShareCheckRole(Role role) => Role = role;
    public Role Role { get; }
    [ObservableProperty] private bool _isChecked = true;
}

/// <summary>One recent-uploads row: the record plus its one-line cell text.</summary>
internal sealed record RecentShareItem(ShareRecord Record)
{
    public string Text { get; } = RoleShareViewModel.RecentCellText(Record);
}

/// <summary>
/// UI-free half of the share dialog (Java ui/RoleShareController): the checked-role list,
/// the recent-uploads history with update-in-place selection, and the publish/update flow
/// (blocking service call on a worker thread, completion marshalled back via
/// Dispatcher.UIThread). Clipboard + window chrome stay in RoleShareWindow.
/// </summary>
internal partial class RoleShareViewModel : ObservableObject
{
    /// <summary>Sanity cap on the user-entered pack title; the server stores contents opaquely.</summary>
    public const int MaxTitleLength = 60;

    private readonly UiContext _ctx;

    public RoleShareViewModel(UiContext ctx)
    {
        _ctx = ctx;
        RefreshRecentList();
    }

    public ObservableCollection<ShareCheckRole> CheckRoles { get; } = [];
    public ObservableCollection<RecentShareItem> Recent { get; } = [];

    [ObservableProperty] private RecentShareItem? _selectedRecent;
    [ObservableProperty] private string? _packName;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusWarning;
    [ObservableProperty] private string _shareButtonLabel = "Share";
    [ObservableProperty] private bool _newCodeVisible;
    [ObservableProperty] private bool _shareEnabled = true;
    [ObservableProperty] private bool _recentVisible;

    /// <summary>The published/updated code is ready — the window copies it to the clipboard.</summary>
    public event Action<string>? CodeReady;

    partial void OnSelectedRecentChanged(RecentShareItem? value) => ApplyRecentSelection(value);

    /// <summary>Populates the checkbox list with the given roles, all checked by default.</summary>
    public void SetRoles(IReadOnlyList<Role> roles)
    {
        CheckRoles.Clear();
        foreach (var r in roles) CheckRoles.Add(new ShareCheckRole(r));
    }

    public void SelectAllRoles() { foreach (var c in CheckRoles) c.IsChecked = true; }

    public void SelectNoneRoles() { foreach (var c in CheckRoles) c.IsChecked = false; }

    public List<Role> CheckedRoles() =>
        CheckRoles.Where(c => c.IsChecked).Select(c => c.Role).ToList();

    // ---- recent uploads ---------------------------------------------------------

    /// <summary>Java refreshRecentList — clear selection, reload, hide the box when empty.</summary>
    public void RefreshRecentList()
    {
        SelectedRecent = null;
        Recent.Clear();
        var shares = _ctx.AppConfig.RecentShares;
        if (shares != null)
        {
            foreach (var s in shares) Recent.Add(new RecentShareItem(s));
        }
        RecentVisible = Recent.Count > 0;
    }

    /// <summary>Java newCode — back to publishing a fresh code.</summary>
    public void NewCode() => SelectedRecent = null;

    /// <summary>Java onRecentSelectionChanged.</summary>
    private void ApplyRecentSelection(RecentShareItem? item)
    {
        var selected = item?.Record;
        if (selected != null && selected.Updatable())
        {
            ShareButtonLabel = "Update " + selected.Code;
            NewCodeVisible = true;
            PackName = selected.Title ?? "";
            SetStatus("Publishing will overwrite " + selected.Code
                + " for everyone who has the code.");
        }
        else
        {
            ShareButtonLabel = "Share";
            NewCodeVisible = false;
            if (selected != null)
            {
                SetStatus("This upload can't be updated — Share publishes a new code.");
            }
        }
    }

    // ---- publish / update flow ----------------------------------------------------

    /// <summary>
    /// Java share(): validation and status here; the blocking service call runs on a worker
    /// thread and completes via <see cref="ShareSucceeded"/>/<see cref="ShareFailed"/>
    /// marshalled back on the UI thread. Returns false when validation refused the share.
    /// </summary>
    public bool Share()
    {
        var selected = CheckedRoles();
        if (selected.Count == 0)
        {
            SetWarning("Select at least one role to share.");
            return false;
        }

        var title = NormalizedTitle(PackName);
        // forSharing strips local provenance so previously imported pack codes never leak.
        var pack = RolePack.ForSharing(title, selected);

        var target = SelectedRecent?.Record;
        var updateTarget = target != null && target.Updatable() ? target : null;

        ShareEnabled = false;
        if (updateTarget != null) SetStatus("Updating " + updateTarget.Code + "…");
        else SetStatus("Publishing…");

        var service = RoleShareService.Instance;
        Task.Run(() =>
        {
            RoleShareService.PublishResult? result = null;
            Exception? failure = null;
            try
            {
                if (updateTarget != null)
                {
                    service.Update(updateTarget.Code, updateTarget.OwnerToken, pack);
                    result = new RoleShareService.PublishResult(updateTarget.Code!, updateTarget.OwnerToken);
                }
                else
                {
                    result = service.Publish(pack);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (failure != null) ShareFailed(failure, updateTarget != null);
                else ShareSucceeded(result!, title, selected.Count, updateTarget != null);
            });
        });
        return true;
    }

    /// <summary>Java task.setOnSucceeded: persist history, show the code, request the copy.</summary>
    public void ShareSucceeded(RoleShareService.PublishResult result, string? title,
        int roleCount, bool updateMode)
    {
        _ctx.AppConfig.RecordShare(new ShareRecord(
            result.Code, result.OwnerToken, title,
            DateTimeOffset.UtcNow.ToString("o"), roleCount));
        _ctx.AppConfig.Save();
        RefreshRecentList();
        Code = result.Code;
        SetStatus(updateMode
            ? "Updated — everyone with this code gets the new version. Code copied."
            : "Published — code copied to clipboard.");
        ShareEnabled = true;
        CodeReady?.Invoke(result.Code);
    }

    /// <summary>Java task.setOnFailed.</summary>
    public void ShareFailed(Exception ex, bool updateMode)
    {
        SetWarning((updateMode ? "Update failed: " : "Publish failed: ") + FailureMessage(ex));
        ShareEnabled = true;
    }

    // ---- helpers -------------------------------------------------------------------

    private void SetStatus(string text) { StatusText = text; StatusWarning = false; }
    private void SetWarning(string text) { StatusText = text; StatusWarning = true; }

    /// <summary>Trimmed, length-capped title, or null when blank (pack stays untitled).</summary>
    public static string? NormalizedTitle(string? raw)
    {
        if (raw == null) return null;
        var title = raw.Trim();
        if (title.Length == 0) return null;
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    /// <summary>One line in the recent-uploads list: title, code, size, date, updatability.</summary>
    public static string RecentCellText(ShareRecord r)
    {
        var title = !string.IsNullOrEmpty(r.Title) ? r.Title! : "Untitled";
        var date = r.UploadedAt != null && r.UploadedAt.Length >= 10 ? r.UploadedAt[..10] : "";
        var sb = new StringBuilder(title).Append(" — ").Append(r.Code)
            .Append(" · ").Append(r.RoleCount).Append(" role(s)");
        if (date.Length > 0) sb.Append(" · ").Append(date);
        if (!r.Updatable()) sb.Append(" (not updatable)");
        return sb.ToString();
    }

    /// <summary>Null-safe status text for a failed share/fetch task; also used by the import VM.</summary>
    public static string FailureMessage(Exception? ex)
    {
        if (ex == null) return "unknown error";
        return ex.Message ?? ex.GetType().Name;
    }
}
