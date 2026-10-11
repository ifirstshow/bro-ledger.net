using System.Collections.ObjectModel;
using Avalonia.Threading;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BroLedger.App.ViewModels;

/// <summary>One checklist entry; the label carries the update-in-place flag (Java importCheckboxLabel).</summary>
internal sealed partial class ImportCheckRole : ObservableObject
{
    public ImportCheckRole(Role role, string label)
    {
        Role = role;
        Label = label;
    }
    public Role Role { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isChecked = true; // Java populateRoleList: default checked
}

/// <summary>
/// UI-free half of the import dialog (Java ui/RoleImportController): code validation, the
/// fetch flow (blocking service call on a worker thread, completion marshalled back via
/// Dispatcher.UIThread), the checked import checklist with "(updates existing)" flags and
/// the oversized-pack truncation notice. Window chrome stays in RoleImportWindow.
/// </summary>
internal partial class RoleImportViewModel : ObservableObject
{
    private readonly UiContext _ctx;
    private RolePack? _fetchedPack;

    /// <summary>The normalized code the current <see cref="_fetchedPack"/> was fetched with.</summary>
    private string? _fetchedCode;

    public RoleImportViewModel(UiContext ctx) => _ctx = ctx;

    public ObservableCollection<ImportCheckRole> Items { get; } = [];

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _packTitle; // null → label hidden (Java showPackTitle)
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusWarning;
    [ObservableProperty] private bool _fetchEnabled = true;
    [ObservableProperty] private bool _importEnabled;

    /// <summary>Java onImported — fired after a successful import (the window closes).</summary>
    public event Action? Imported;

    /// <summary>
    /// Java fetch's pre-network half: validates the code, clears prior fetch state and shows
    /// "Fetching…". Returns the normalized code, or null when the input cannot be a code.
    /// </summary>
    public string? TryBeginFetch()
    {
        var code = RoleShareService.NormalizeCode(Code);
        if (code == null)
        {
            SetWarning("Enter a valid share code — 8 letters/digits, e.g. AB3D9F2K.");
            return null;
        }

        _fetchedPack = null;
        _fetchedCode = null;
        Items.Clear();
        PackTitle = null;
        FetchEnabled = false;
        ImportEnabled = false;
        SetStatus("Fetching…");
        return code;
    }

    /// <summary>Java fetch — the blocking network hop on a worker thread.</summary>
    public void Fetch()
    {
        var code = TryBeginFetch();
        if (code == null) return;

        Task.Run(() =>
        {
            RolePack? pack = null;
            Exception? failure = null;
            try
            {
                pack = RoleShareService.Instance.Fetch(code);
            }
            catch (Exception e)
            {
                failure = e;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (failure != null) FetchFailed(failure);
                else CompleteFetch(pack!, code);
            });
        });
    }

    /// <summary>Java task.setOnSucceeded — populate the checklist, title and status.</summary>
    public void CompleteFetch(RolePack pack, string code)
    {
        _fetchedPack = pack;
        _fetchedCode = code;
        foreach (var r in pack.Roles)
        {
            var updates = _ctx.Roles.FindBySource(code, r.Id) != null;
            var item = new ImportCheckRole(r, ImportCheckboxLabel(r.Name ?? "", updates));
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImportCheckRole.IsChecked)) UpdateImportButtonState();
            };
            Items.Add(item);
        }
        PackTitle = PackTitleText(pack, code);
        SetStatus(FetchSuccessStatus(pack));
        FetchEnabled = true;
        UpdateImportButtonState();
    }

    /// <summary>Java task.setOnFailed.</summary>
    public void FetchFailed(Exception ex)
    {
        SetWarning("Fetch failed: " + RoleShareViewModel.FailureMessage(ex));
        FetchEnabled = true;
    }

    public void SelectAllRoles() { foreach (var i in Items) i.IsChecked = true; }

    public void SelectNoneRoles() { foreach (var i in Items) i.IsChecked = false; }

    /// <summary>Java updateImportButtonState — import is enabled only while any row is checked.</summary>
    public void UpdateImportButtonState() => ImportEnabled = Items.Any(i => i.IsChecked);

    /// <summary>Java doImport — imports the checked subset, then notifies (the window closes).</summary>
    public void DoImport()
    {
        if (_fetchedPack == null) return;
        var selected = Items.Where(i => i.IsChecked).Select(i => i.Role).ToList();
        if (selected.Count == 0) return;

        _ctx.Roles.ImportPack(_fetchedPack, selected, _fetchedCode);
        Imported?.Invoke();
    }

    // ---- helpers ----------------------------------------------------------------

    private void SetStatus(string text) { StatusText = text; StatusWarning = false; }
    private void SetWarning(string text) { StatusText = text; StatusWarning = true; }

    /// <summary>Checkbox text for one fetched role; flags roles that will update a previous import.</summary>
    public static string ImportCheckboxLabel(string name, bool updatesExisting) =>
        updatesExisting ? name + "  (updates existing)" : name;

    /// <summary>The fetched pack's display title; untitled packs fall back to showing the code.</summary>
    public static string PackTitleText(RolePack pack, string code) =>
        !string.IsNullOrEmpty(pack.Label) ? pack.Label! : "Untitled pack (" + code + ")";

    /// <summary>Java fetch onSucceeded's status line, truncation notice included.</summary>
    public static string FetchSuccessStatus(RolePack pack) =>
        "Found " + pack.Roles.Count + " role(s)"
        + (pack.Truncated() ? " (oversized pack — extra roles were dropped)" : "") + ".";
}
