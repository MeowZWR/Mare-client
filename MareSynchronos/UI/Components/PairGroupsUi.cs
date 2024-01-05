using Dalamud.Interface;
using Dalamud.Interface.Components;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class PairGroupsUi
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _mareConfig;
    private readonly SelectPairForGroupUi _selectGroupForPairUi;
    private readonly TagHandler _tagHandler;

    public PairGroupsUi(MareConfigService mareConfig, TagHandler tagHandler, ApiController apiController, SelectPairForGroupUi selectGroupForPairUi)
    {
        _mareConfig = mareConfig;
        _tagHandler = tagHandler;
        _apiController = apiController;
        _selectGroupForPairUi = selectGroupForPairUi;
    }

    public void Draw<T>(List<T> visibleUsers, List<T> onlineUsers, List<T> offlineUsers) where T : DrawPairBase
    {
        // Only render those tags that actually have pairs in them, otherwise
        // we can end up with a bunch of useless pair groups
        var tagsWithPairsInThem = _tagHandler.GetAllTagsSorted();
        var allUsers = visibleUsers.Concat(onlineUsers).Concat(offlineUsers).ToList();
        if (typeof(T) == typeof(DrawUserPair))
        {
            DrawUserPairs(tagsWithPairsInThem, allUsers.Cast<DrawUserPair>().ToList(), visibleUsers.Cast<DrawUserPair>(), onlineUsers.Cast<DrawUserPair>(), offlineUsers.Cast<DrawUserPair>());
        }
    }

    private void DrawButtons(string tag, List<DrawUserPair> availablePairsInThisTag)
    {
        var allArePaused = availablePairsInThisTag.All(pair => pair.UserPair!.OwnPermissions.IsPaused());
        var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var flyoutMenuX = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X;
        var pauseButtonX = UiSharedService.GetIconButtonSize(pauseButton).X;
        var windowX = ImGui.GetWindowContentRegionMin().X;
        var windowWidth = UiSharedService.GetWindowContentRegionWidth();
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        var buttonPauseOffset = windowX + windowWidth - flyoutMenuX - spacingX - pauseButtonX;
        ImGui.SameLine(buttonPauseOffset);
        if (ImGuiComponents.IconButton(pauseButton))
        {
            // If all of the currently visible pairs (after applying filters to the pairs)
            // are paused we display a resume button to resume all currently visible (after filters)
            // pairs. Otherwise, we just pause all the remaining pairs.
            if (allArePaused)
            {
                // If all are paused => resume all
                ResumeAllPairs(availablePairsInThisTag);
            }
            else
            {
                // otherwise pause all remaining
                PauseRemainingPairs(availablePairsInThisTag);
            }
        }
        if (allArePaused)
        {
            UiSharedService.AttachToolTip($"恢复 {tag} 中的所有配对。");
        }
        else
        {
            UiSharedService.AttachToolTip($"暂停 {tag} 中的所有配对");
        }

        var buttonDeleteOffset = windowX + windowWidth - flyoutMenuX;
        ImGui.SameLine(buttonDeleteOffset);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("Group Flyout Menu");
        }

        if (ImGui.BeginPopup("Group Flyout Menu"))
        {
            UiSharedService.DrawWithID($"buttons-{tag}", () => DrawGroupMenu(tag));
            ImGui.EndPopup();
        }
    }

    private void DrawCategory(string tag, IEnumerable<DrawPairBase> onlineUsers, IEnumerable<DrawPairBase> allUsers, IEnumerable<DrawPairBase>? visibleUsers = null)
    {
        IEnumerable<DrawPairBase> usersInThisTag;
        HashSet<string>? otherUidsTaggedWithTag = null;
        bool isSpecialTag = false;
        int visibleInThisTag = 0;
        if (tag is TagHandler.CustomOfflineTag or TagHandler.CustomOnlineTag or TagHandler.CustomVisibleTag or TagHandler.CustomUnpairedTag)
        {
            usersInThisTag = onlineUsers;
            isSpecialTag = true;
        }
        else
        {
            otherUidsTaggedWithTag = _tagHandler.GetOtherUidsForTag(tag);
            usersInThisTag = onlineUsers
                .Where(pair => otherUidsTaggedWithTag.Contains(pair.UID))
                .ToList();
            visibleInThisTag = visibleUsers?.Count(p => otherUidsTaggedWithTag.Contains(p.UID)) ?? 0;
        }

        if (isSpecialTag && !usersInThisTag.Any()) return;

        DrawName(tag, isSpecialTag, visibleInThisTag, usersInThisTag.Count(), otherUidsTaggedWithTag?.Count);
        if (!isSpecialTag)
        {
            if (onlineUsers.Any() && onlineUsers.First() is DrawUserPair)
            {
                UiSharedService.DrawWithID($"group-{tag}-buttons", () => DrawButtons(tag, allUsers.Cast<DrawUserPair>().Where(p => otherUidsTaggedWithTag!.Contains(p.UID)).ToList()));
            }
        }

        if (!_tagHandler.IsTagOpen(tag)) return;

        ImGui.Indent(20);
        DrawPairs(tag, usersInThisTag);
        ImGui.Unindent(20);
    }

    private void DrawGroupMenu(string tag)
    {
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Users, "添加用户到 " + tag))
        {
            _selectGroupForPairUi.Open(tag);
        }
        UiSharedService.AttachToolTip($"添加更多用户到组 {tag}");

        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除 " + tag) && UiSharedService.CtrlPressed())
        {
            _tagHandler.RemoveTag(tag);
        }
        UiSharedService.AttachToolTip($"删除组 {tag} （不会删除其中的配对）" + Environment.NewLine + "按住CTRL建来删除");
    }

    private void DrawName(string tag, bool isSpecialTag, int visible, int online, int? total)
    {
        string displayedName = tag switch
        {
            TagHandler.CustomUnpairedTag => "未配对",
            TagHandler.CustomOfflineTag => "已离线",
            TagHandler.CustomOnlineTag => _mareConfig.Current.ShowOfflineUsersSeparately ? "在线/已暂停" : "Contacts",
            TagHandler.CustomVisibleTag => "可见",
            _ => tag
        };

        string resultFolderName = !isSpecialTag ? $"{displayedName} ({visible}/{online}/{total} 个配对)" : $"{displayedName} ({online} 个配对)";

        //  FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight
        var icon = _tagHandler.IsTagOpen(tag) ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
        UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ToggleTagOpen(tag);
        }
        ImGui.SameLine();
        UiSharedService.FontText(resultFolderName, UiBuilder.DefaultFont);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ToggleTagOpen(tag);
        }

        if (!isSpecialTag && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"组 {tag}");
            ImGui.Separator();
            ImGui.TextUnformatted($"{visible} 个配对可见");
            ImGui.TextUnformatted($"{online} 个配对 已在线/已暂停");
            ImGui.TextUnformatted($"{total} 个配对总数");
            ImGui.EndTooltip();
        }
    }

    private void DrawPairs(string tag, IEnumerable<DrawPairBase> availablePairsInThisCategory)
    {
        // These are all the OtherUIDs that are tagged with this tag
        foreach (var pair in availablePairsInThisCategory)
        {
            UiSharedService.DrawWithID($"tag-{tag}-pair-${pair.UID}", () => pair.DrawPairedClient());
        }
        ImGui.Separator();
    }

    private void DrawUserPairs(List<string> tagsWithPairsInThem, List<DrawUserPair> allUsers, IEnumerable<DrawUserPair> visibleUsers, IEnumerable<DrawUserPair> onlineUsers, IEnumerable<DrawUserPair> offlineUsers)
    {
        if (_mareConfig.Current.ShowVisibleUsersSeparately)
        {
            UiSharedService.DrawWithID("$group-VisibleCustomTag", () => DrawCategory(TagHandler.CustomVisibleTag, visibleUsers, allUsers));
        }
        foreach (var tag in tagsWithPairsInThem)
        {
            if (_mareConfig.Current.ShowOfflineUsersSeparately)
            {
                UiSharedService.DrawWithID($"group-{tag}", () => DrawCategory(tag, onlineUsers, allUsers, visibleUsers));
            }
            else
            {
                UiSharedService.DrawWithID($"group-{tag}", () => DrawCategory(tag, onlineUsers.Concat(offlineUsers).ToList(), allUsers, visibleUsers));
            }
        }
        if (_mareConfig.Current.ShowOfflineUsersSeparately)
        {
            UiSharedService.DrawWithID($"group-OnlineCustomTag", () => DrawCategory(TagHandler.CustomOnlineTag,
                onlineUsers.Where(u => !_tagHandler.HasAnyTag(u.UID)).ToList(), allUsers));
            UiSharedService.DrawWithID($"group-OfflineCustomTag", () => DrawCategory(TagHandler.CustomOfflineTag,
                offlineUsers.Where(u => u.UserPair!.OtherPermissions.IsPaired()).ToList(), allUsers));
        }
        else
        {
            UiSharedService.DrawWithID($"group-OnlineCustomTag", () => DrawCategory(TagHandler.CustomOnlineTag,
                onlineUsers.Concat(offlineUsers).Where(u => u.UserPair!.OtherPermissions.IsPaired() && !_tagHandler.HasAnyTag(u.UID)).ToList(), allUsers));
        }
        UiSharedService.DrawWithID($"group-UnpairedCustomTag", () => DrawCategory(TagHandler.CustomUnpairedTag,
            offlineUsers.Where(u => !u.UserPair!.OtherPermissions.IsPaired()).ToList(), allUsers));
    }

    private void PauseRemainingPairs(List<DrawUserPair> availablePairs)
    {
        foreach (var pairToPause in availablePairs.Where(pair => !pair.UserPair!.OwnPermissions.IsPaused()))
        {
            var perm = pairToPause.UserPair!.OwnPermissions;
            perm.SetPaused(paused: true);
            _ = _apiController.UserSetPairPermissions(new(new(pairToPause.UID), perm));
        }
    }

    private void ResumeAllPairs(List<DrawUserPair> availablePairs)
    {
        foreach (var pairToPause in availablePairs)
        {
            var perm = pairToPause.UserPair!.OwnPermissions;
            perm.SetPaused(paused: false);
            _ = _apiController.UserSetPairPermissions(new(new(pairToPause.UID), perm));
        }
    }

    private void ToggleTagOpen(string tag)
    {
        bool open = !_tagHandler.IsTagOpen(tag);
        _tagHandler.SetTagOpen(tag, open);
    }
}