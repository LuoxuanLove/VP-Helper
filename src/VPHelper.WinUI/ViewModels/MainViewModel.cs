using System;

namespace VPHelper.WinUI.ViewModels;

/// <summary>Top-level shell view model: exposes the child view models and rankland attribution.</summary>
public sealed class MainViewModel : ObservableObject
{
    public LoginViewModel Login { get; }
    public ContestsViewModel Contests { get; }
    public OfficialViewModel Official { get; }

    /// <summary>Data-source attribution shown in the UI (AGPL-3.0 requirement, DESIGN 6.4).</summary>
    public string DataAttribution => "数据来源：algoux/srk-collection (AGPL-3.0)";

    public MainViewModel(LoginViewModel login, ContestsViewModel contests, OfficialViewModel official)
    {
        Login = login;
        Contests = contests;
        Official = official;
    }
}
