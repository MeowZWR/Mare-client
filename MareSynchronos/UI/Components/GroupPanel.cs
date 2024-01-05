using Dalamud.Interface.Components;
using Dalamud.Interface;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using System.Globalization;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI;

internal sealed class GroupPanel
{
    private readonly Dictionary<string, bool> _expandedGroupState = new(StringComparer.Ordinal);
    private readonly CompactUi _mainUi;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly Dictionary<string, bool> _showGidForEntry = new(StringComparer.Ordinal);
    private readonly UidDisplayHandler _uidDisplayHandler;
    private readonly UiSharedService _uiShared;
    private List<BannedGroupUserDto> _bannedUsers = new();
    private int _bulkInviteCount = 10;
    private List<string> _bulkOneTimeInvites = new();
    private string _editGroupComment = string.Empty;
    private string _editGroupEntry = string.Empty;
    private bool _errorGroupCreate = false;
    private bool _errorGroupJoin;
    private bool _isPasswordValid;
    private GroupPasswordDto? _lastCreatedGroup = null;
    private bool _modalBanListOpened;
    private bool _modalBulkOneTimeInvitesOpened;
    private bool _modalChangePwOpened;
    private string _newSyncShellPassword = string.Empty;
    private bool _showModalBanList = false;
    private bool _showModalBulkOneTimeInvites = false;
    private bool _showModalChangePassword;
    private bool _showModalCreateGroup;
    private bool _showModalEnterPassword;
    private string _syncShellPassword = string.Empty;
    private string _syncShellToJoin = string.Empty;

    public GroupPanel(CompactUi mainUi, UiSharedService uiShared, PairManager pairManager, UidDisplayHandler uidDisplayHandler, ServerConfigurationManager serverConfigurationManager)
    {
        _mainUi = mainUi;
        _uiShared = uiShared;
        _pairManager = pairManager;
        _uidDisplayHandler = uidDisplayHandler;
        _serverConfigurationManager = serverConfigurationManager;
    }

    private ApiController ApiController => _uiShared.ApiController;

    public void DrawSyncshells()
    {
        UiSharedService.DrawWithID("addsyncshell", DrawAddSyncshell);
        UiSharedService.DrawWithID("syncshelllist", DrawSyncshellList);
        _mainUi.TransferPartHeight = ImGui.GetCursorPosY();
    }

    private void DrawAddSyncshell()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##syncshellid", "同步贝GID或别名（留空创建同步贝）", ref _syncShellToJoin, 20);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);

        bool userCanJoinMoreGroups = _pairManager.GroupPairs.Count < ApiController.ServerInfo.MaxGroupsJoinedByUser;
        bool userCanCreateMoreGroups = _pairManager.GroupPairs.Count(u => string.Equals(u.Key.Owner.UID, ApiController.UID, StringComparison.Ordinal)) < ApiController.ServerInfo.MaxGroupsCreatedByUser;
        bool alreadyInGroup = _pairManager.GroupPairs.Select(p => p.Key).Any(p => string.Equals(p.Group.Alias, _syncShellToJoin, StringComparison.Ordinal)
            || string.Equals(p.Group.GID, _syncShellToJoin, StringComparison.Ordinal));

        if (alreadyInGroup) ImGui.BeginDisabled();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            if (!string.IsNullOrEmpty(_syncShellToJoin))
            {
                if (userCanJoinMoreGroups)
                {
                    _errorGroupJoin = false;
                    _showModalEnterPassword = true;
                    ImGui.OpenPopup("输入同步贝密码");
                }
            }
            else
            {
                if (userCanCreateMoreGroups)
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = false;
                    _showModalCreateGroup = true;
                    ImGui.OpenPopup("创建同步贝");
                }
            }
        }
        UiSharedService.AttachToolTip(_syncShellToJoin.IsNullOrEmpty()
            ? (userCanCreateMoreGroups ? "创建同步贝" : $"你无法创建超过{ApiController.ServerInfo.MaxGroupsCreatedByUser}个同步贝")
            : (userCanJoinMoreGroups ? "加入同步贝" + _syncShellToJoin : $"你无法加入超过{ApiController.ServerInfo.MaxGroupsJoinedByUser}个同步贝"));

        if (alreadyInGroup) ImGui.EndDisabled();

        if (ImGui.BeginPopupModal("输入同步贝密码", ref _showModalEnterPassword, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("在加入任何同步贝之前，请注意，您将自动与同步贝中的每个人配对。");
            ImGui.Separator();
            UiSharedService.TextWrapped("输入同步贝 " + _syncShellToJoin + " 的密码：");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##password", _syncShellToJoin + " Password", ref _syncShellPassword, 255, ImGuiInputTextFlags.Password);
            if (_errorGroupJoin)
            {
                UiSharedService.ColorTextWrapped($"加入此同步贝时发生错误：您加入的同步贝数量已达到最大值({ApiController.ServerInfo.MaxGroupsJoinedByUser}), " +
                    $"同步贝不存在、密码错误、您已加入此同步贝、同步贝已满（{ApiController.ServerInfo.MaxGroupUserCount} 个用户）、或者此同步贝已关闭邀请。",
                    new Vector4(1, 0, 0, 1));
            }
            if (ImGui.Button("加入 " + _syncShellToJoin))
            {
                var shell = _syncShellToJoin;
                var pw = _syncShellPassword;
                _errorGroupJoin = !ApiController.GroupJoin(new(new GroupData(shell), pw)).Result;
                if (!_errorGroupJoin)
                {
                    _syncShellToJoin = string.Empty;
                    _showModalEnterPassword = false;
                }
                _syncShellPassword = string.Empty;
            }
            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("创建同步贝", ref _showModalCreateGroup, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("点击下面的按钮创建一个新的同步贝");
            ImGui.SetNextItemWidth(200);
            if (ImGui.Button("创建同步贝"))
            {
                try
                {
                    _lastCreatedGroup = ApiController.GroupCreate().Result;
                }
                catch
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = true;
                }
            }

            if (_lastCreatedGroup != null)
            {
                ImGui.Separator();
                _errorGroupCreate = false;
                ImGui.TextUnformatted("同步贝ID：" + _lastCreatedGroup.Group.GID);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("同步贝密码：" + _lastCreatedGroup.Password);
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
                {
                    ImGui.SetClipboardText(_lastCreatedGroup.Password);
                }
                UiSharedService.TextWrapped("您可以稍后随时修改同步贝密码。");
            }

            if (_errorGroupCreate)
            {
                UiSharedService.ColorTextWrapped("您已经拥有最大数量的同步贝（3）或加入了最大数量的同步贝（6）。请将您自己的同步贝的所有权转移给其他人或离开现有的同步贝。",
                    new Vector4(1, 0, 0, 1));
            }

            UiSharedService.SetScaledWindowSize(350);
            ImGui.EndPopup();
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawSyncshell(GroupFullInfoDto groupDto, List<Pair> pairsInGroup)
    {
        var name = groupDto.Group.Alias ?? groupDto.GID;
        if (!_expandedGroupState.TryGetValue(groupDto.GID, out bool isExpanded))
        {
            isExpanded = false;
            _expandedGroupState.Add(groupDto.GID, isExpanded);
        }
        var icon = isExpanded ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
        var collapseButton = UiSharedService.GetIconButtonSize(icon);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
        if (ImGuiComponents.IconButton(icon))
        {
            _expandedGroupState[groupDto.GID] = !_expandedGroupState[groupDto.GID];
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + collapseButton.X);
        var pauseIcon = groupDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        if (ImGuiComponents.IconButton(pauseIcon))
        {
            var userPerm = groupDto.GroupUserPermissions ^ GroupUserPermissions.Paused;
            _ = ApiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(groupDto.Group, new UserData(ApiController.UID), userPerm));
        }
        UiSharedService.AttachToolTip((groupDto.GroupUserPermissions.IsPaused() ? "恢复" : "暂停") + "与此同步贝中的所有用户配对。");
        ImGui.SameLine();

        var textIsGid = true;
        string groupName = groupDto.GroupAliasOrGID;

        if (string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal))
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("您是同步贝" + groupName + "的所有者。");
            ImGui.SameLine();
        }
        else if (groupDto.GroupUserInfo.IsModerator())
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("您是同步贝" + groupName + "的主持人。");
            ImGui.SameLine();
        }

        _showGidForEntry.TryGetValue(groupDto.GID, out var showGidInsteadOfName);
        var groupComment = _serverConfigurationManager.GetNoteForGid(groupDto.GID);
        if (!showGidInsteadOfName && !string.IsNullOrEmpty(groupComment))
        {
            groupName = groupComment;
            textIsGid = false;
        }

        if (!string.Equals(_editGroupEntry, groupDto.GID, StringComparison.Ordinal))
        {
            if (textIsGid) ImGui.PushFont(UiBuilder.MonoFont);
            ImGui.TextUnformatted(groupName);
            if (textIsGid) ImGui.PopFont();
            UiSharedService.AttachToolTip("左键单击可在GID和备注之间切换                " + Environment.NewLine +
                          "右键单击为" + groupName + "修改备注" + Environment.NewLine
                          + "用户：" + (pairsInGroup.Count + 1) + ", 所有者：" + groupDto.OwnerAliasOrUID);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsGid;
                if (_showGidForEntry.ContainsKey(groupDto.GID))
                {
                    prevState = _showGidForEntry[groupDto.GID];
                }

                _showGidForEntry[groupDto.GID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _serverConfigurationManager.SetNoteForGid(_editGroupEntry, _editGroupComment);
                _editGroupComment = _serverConfigurationManager.GetNoteForGid(groupDto.GID) ?? string.Empty;
                _editGroupEntry = groupDto.GID;
            }
        }
        else
        {
            var buttonSizes = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X + UiSharedService.GetIconSize(FontAwesomeIcon.LockOpen).X;
            ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", "备注/注释", ref _editGroupComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverConfigurationManager.SetNoteForGid(groupDto.GID, _editGroupComment);
                _editGroupEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editGroupEntry = string.Empty;
            }
            UiSharedService.AttachToolTip("按回车键保存\n点击鼠标右键取消");
        }

        UiSharedService.DrawWithID(groupDto.GID + "settings", () => DrawSyncShellButtons(groupDto, pairsInGroup));

        if (_showModalBanList && !_modalBanListOpened)
        {
            _modalBanListOpened = true;
            ImGui.OpenPopup("禁止名单管理：" + groupDto.GID);
        }

        if (!_showModalBanList) _modalBanListOpened = false;

        if (ImGui.BeginPopupModal("禁止名单管理：" + groupDto.GID, ref _showModalBanList, UiSharedService.PopupWindowFlags))
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "从服务器刷新禁止列表"))
            {
                _bannedUsers = ApiController.GroupGetBannedUsers(groupDto).Result;
            }

            if (ImGui.BeginTable("bannedusertable" + groupDto.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("别名", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("操作人", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("日期", ImGuiTableColumnFlags.None, 2);
                ImGui.TableSetupColumn("原因", ImGuiTableColumnFlags.None, 3);
                ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.None, 1);

                ImGui.TableHeadersRow();

                foreach (var bannedUser in _bannedUsers.ToList())
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.UID);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.BannedBy);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                    ImGui.TableNextColumn();
                    UiSharedService.TextWrapped(bannedUser.Reason);
                    ImGui.TableNextColumn();
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban#" + bannedUser.UID))
                    {
                        _ = ApiController.GroupUnbanUser(bannedUser);
                        _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                    }
                }

                ImGui.EndTable();
            }
            UiSharedService.SetScaledWindowSize(700, 300);
            ImGui.EndPopup();
        }

        if (_showModalChangePassword && !_modalChangePwOpened)
        {
            _modalChangePwOpened = true;
            ImGui.OpenPopup("修改同步贝密码");
        }

        if (!_showModalChangePassword) _modalChangePwOpened = false;

        if (ImGui.BeginPopupModal("修改同步贝密码", ref _showModalChangePassword, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("在这里为同步贝 " + name + " 输入新密码。");
            UiSharedService.TextWrapped("操作不可逆");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##changepw", "为此同步贝修改新密码：" + name, ref _newSyncShellPassword, 255);
            if (ImGui.Button("确认修改"))
            {
                var pw = _newSyncShellPassword;
                _isPasswordValid = ApiController.GroupChangePassword(new(groupDto.Group, pw)).Result;
                _newSyncShellPassword = string.Empty;
                if (_isPasswordValid) _showModalChangePassword = false;
            }

            if (!_isPasswordValid)
            {
                UiSharedService.ColorTextWrapped("密码太短。必须至少包含10个字符。", new Vector4(1, 0, 0, 1));
            }

            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        if (_showModalBulkOneTimeInvites && !_modalBulkOneTimeInvitesOpened)
        {
            _modalBulkOneTimeInvitesOpened = true;
            ImGui.OpenPopup("创建批量一次性邀请");
        }

        if (!_showModalBulkOneTimeInvites) _modalBulkOneTimeInvitesOpened = false;

        if (ImGui.BeginPopupModal("创建批量一次性邀请", ref _showModalBulkOneTimeInvites, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("这允许您一次性为同步贝 " + name + " 创建多达100个的一次性邀请。" + Environment.NewLine
                + "邀请在创建后24小时内有效。");
            ImGui.Separator();
            if (_bulkOneTimeInvites.Count == 0)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderInt("数量##bulkinvites", ref _bulkInviteCount, 1, 100);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.MailBulk, "创建邀请"))
                {
                    _bulkOneTimeInvites = ApiController.GroupCreateTempInvite(groupDto, _bulkInviteCount).Result;
                }
            }
            else
            {
                UiSharedService.TextWrapped("一共创建了 " + _bulkOneTimeInvites.Count + " 个邀请。");
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Copy, "将邀请信息复制到剪贴板"))
                {
                    ImGui.SetClipboardText(string.Join(Environment.NewLine, _bulkOneTimeInvites));
                }
            }

            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        ImGui.Indent(collapseButton.X);
        if (_expandedGroupState[groupDto.GID])
        {
            var visibleUsers = pairsInGroup.Where(u => u.IsVisible)
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DrawGroupPair(groupDto.GID + c.UserData.UID, c, ApiController, groupDto, c.GroupPair.Single(g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)).Value,
                    _uidDisplayHandler))
                .ToList();
            var onlineUsers = pairsInGroup.Where(u => u.IsOnline && !u.IsVisible)
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DrawGroupPair(groupDto.GID + c.UserData.UID, c, ApiController, groupDto, c.GroupPair.Single(g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)).Value,
                    _uidDisplayHandler))
                .ToList();
            var offlineUsers = pairsInGroup.Where(u => !u.IsOnline && !u.IsVisible)
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
                .Select(c => new DrawGroupPair(groupDto.GID + c.UserData.UID, c, ApiController, groupDto, c.GroupPair.Single(g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)).Value,
                    _uidDisplayHandler))
                .ToList();

            if (visibleUsers.Any())
            {
                ImGui.Text("可见");
                ImGui.Separator();
                foreach (var entry in visibleUsers)
                {
                    UiSharedService.DrawWithID(groupDto.GID + entry.UID, () => entry.DrawPairedClient());
                }
            }

            if (onlineUsers.Any())
            {
                ImGui.Text("在线");
                ImGui.Separator();
                foreach (var entry in onlineUsers)
                {
                    UiSharedService.DrawWithID(groupDto.GID + entry.UID, () => entry.DrawPairedClient());
                }
            }

            if (offlineUsers.Any())
            {
                ImGui.Text("离线/未知");
                ImGui.Separator();
                foreach (var entry in offlineUsers)
                {
                    UiSharedService.DrawWithID(groupDto.GID + entry.UID, () => entry.DrawPairedClient());
                }
            }

            ImGui.Separator();
            ImGui.Unindent(ImGui.GetStyle().ItemSpacing.X / 2);
        }
        ImGui.Unindent(collapseButton.X);
    }

    private void DrawSyncShellButtons(GroupFullInfoDto groupDto, List<Pair> groupPairs)
    {
        var infoIcon = FontAwesomeIcon.InfoCircle;

        bool invitesEnabled = !groupDto.GroupPermissions.IsDisableInvites();
        var soundsDisabled = groupDto.GroupPermissions.IsDisableSounds();
        var animDisabled = groupDto.GroupPermissions.IsDisableAnimations();
        var vfxDisabled = groupDto.GroupPermissions.IsDisableVFX();

        var userSoundsDisabled = groupDto.GroupUserPermissions.IsDisableSounds();
        var userAnimDisabled = groupDto.GroupUserPermissions.IsDisableAnimations();
        var userVFXDisabled = groupDto.GroupUserPermissions.IsDisableVFX();

        bool showInfoIcon = !invitesEnabled || soundsDisabled || animDisabled || vfxDisabled || userSoundsDisabled || userAnimDisabled || userVFXDisabled;

        var lockedIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
        var animIcon = animDisabled ? FontAwesomeIcon.Stop : FontAwesomeIcon.Running;
        var soundsIcon = soundsDisabled ? FontAwesomeIcon.VolumeOff : FontAwesomeIcon.VolumeUp;
        var vfxIcon = vfxDisabled ? FontAwesomeIcon.Circle : FontAwesomeIcon.Sun;
        var userAnimIcon = userAnimDisabled ? FontAwesomeIcon.Stop : FontAwesomeIcon.Running;
        var userSoundsIcon = userSoundsDisabled ? FontAwesomeIcon.VolumeOff : FontAwesomeIcon.VolumeUp;
        var userVFXIcon = userVFXDisabled ? FontAwesomeIcon.Circle : FontAwesomeIcon.Sun;

        var iconSize = UiSharedService.GetIconSize(infoIcon);
        var diffLockUnlockIcons = showInfoIcon ? (UiSharedService.GetIconSize(infoIcon).X - iconSize.X) / 2 : 0;
        var barbuttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var isOwner = string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal);

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - barbuttonSize.X - (showInfoIcon ? iconSize.X : 0) - diffLockUnlockIcons - (showInfoIcon ? ImGui.GetStyle().ItemSpacing.X : 0));
        if (showInfoIcon)
        {
            UiSharedService.FontText(infoIcon.ToIconString(), UiBuilder.IconFont);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                {
                    ImGui.Text("同步贝权限");

                    if (!invitesEnabled)
                    {
                        var lockedText = "同步贝已关闭加入权限";
                        UiSharedService.FontText(lockedIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(lockedText);
                    }

                    if (soundsDisabled)
                    {
                        var soundsText = "所有者已禁用声音同步";
                        UiSharedService.FontText(soundsIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(soundsText);
                    }

                    if (animDisabled)
                    {
                        var animText = "所有者已禁用情感动作同步";
                        UiSharedService.FontText(animIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(animText);
                    }

                    if (vfxDisabled)
                    {
                        var vfxText = "所有者已禁用视觉效果VFX同步";
                        UiSharedService.FontText(vfxIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(vfxText);
                    }
                }

                if (userSoundsDisabled || userAnimDisabled || userVFXDisabled)
                {
                    if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                        ImGui.Separator();

                    ImGui.Text("您的权限");

                    if (userSoundsDisabled)
                    {
                        var userSoundsText = "您已禁用声音同步";
                        UiSharedService.FontText(userSoundsIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userSoundsText);
                    }

                    if (userAnimDisabled)
                    {
                        var userAnimText = "您已禁用情感动作同步";
                        UiSharedService.FontText(userAnimIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userAnimText);
                    }

                    if (userVFXDisabled)
                    {
                        var userVFXText = "您已禁用视觉效果VFX同步";
                        UiSharedService.FontText(userVFXIcon.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userVFXText);
                    }

                    if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                        UiSharedService.TextWrapped("请注意同步贝全局禁用权限优先于您自己的设置权限");
                }
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + diffLockUnlockIcons);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("ShellPopup");
        }

        if (ImGui.BeginPopup("ShellPopup"))
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleLeft, "离开同步贝") && UiSharedService.CtrlPressed())
            {
                _ = ApiController.GroupLeave(groupDto);
            }
            UiSharedService.AttachToolTip("按住CTRL键并单击来离开此同步贝" + (!string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal) ? string.Empty : Environment.NewLine
                + "警告：此操作是不可逆" + Environment.NewLine + "离开自己是所有者的同步贝会将所有权转移给同步贝的一个随机成员。"));

            if (UiSharedService.IconTextButton(FontAwesomeIcon.Copy, "复制ID"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.SetClipboardText(groupDto.GroupAliasOrGID);
            }
            UiSharedService.AttachToolTip("复制同步贝ID到剪贴板");

            if (UiSharedService.IconTextButton(FontAwesomeIcon.StickyNote, "复制备注"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.SetClipboardText(UiSharedService.GetNotes(groupPairs));
            }
            UiSharedService.AttachToolTip("将此同步贝中所有用户的备注复制到剪贴板。" + Environment.NewLine + "它们可以通过“设置”->“备注”->“从剪贴板导入备注”选项导入。");

            var soundsText = userSoundsDisabled ? "启用声音同步" : "禁用声音同步";
            if (UiSharedService.IconTextButton(userSoundsIcon, soundsText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableSounds(!perm.IsDisableSounds());
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("设置此同步贝成员的声音同步开关。"
                + Environment.NewLine + "禁用同步将停止此同步贝成员对声音的修改。"
                + Environment.NewLine + "注意：此设置可被同步贝所有者强制覆盖为“禁用”。"
                + Environment.NewLine + "注意：此设置不会影响此同步贝成员的独立配对。");

            var animText = userAnimDisabled ? "启用情感动作同步" : "禁用情感动作同步";
            if (UiSharedService.IconTextButton(userAnimIcon, animText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableAnimations(!perm.IsDisableAnimations());
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("设置此同步贝中成员的情感动作同步开关。"
                + Environment.NewLine + "禁用同步将停止此同步贝中成员对情感动作的修改。"
                + Environment.NewLine + "注意：此设置也可能影响声音同步。"
                + Environment.NewLine + "注意：此设置可被同步贝所有者强制覆盖为“禁用”。"
                + Environment.NewLine + "注意：此设置不会影响同步贝成员的独立配对。");

            var vfxText = userVFXDisabled ? "启用视觉特效同步" : "禁用视觉特效同步";
            if (UiSharedService.IconTextButton(userVFXIcon, vfxText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableVFX(!perm.IsDisableVFX());
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("设置此同步贝中用户的视觉特效（VFX）同步开关。"
                                          + Environment.NewLine + "禁用同步将停止此同步贝成员对视觉特效的修改。"
                                          + Environment.NewLine + "注意：此设置也可能在一定程度上影响情感动作同步。"
                                          + Environment.NewLine + "注意：此设置可被同步贝所有者强制覆盖为“禁用”。"
                                          + Environment.NewLine + "注意：此设置不会影响成员的独立配对。");

            if (isOwner || groupDto.GroupUserInfo.IsModerator())
            {
                ImGui.Separator();

                var changedToIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
                if (UiSharedService.IconTextButton(changedToIcon, invitesEnabled ? "锁定同步贝" : "解锁同步贝"))
                {
                    ImGui.CloseCurrentPopup();
                    var groupPerm = groupDto.GroupPermissions;
                    groupPerm.SetDisableInvites(invitesEnabled);
                    _ = ApiController.GroupChangeGroupPermissionState(new GroupPermissionDto(groupDto.Group, groupPerm));
                }
                UiSharedService.AttachToolTip("修改同步贝加入权限" + Environment.NewLine + "同步贝当前状态为：" + (invitesEnabled ? "可加入" : "不可加入"));

                if (isOwner)
                {
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Passport, "修改密码"))
                    {
                        ImGui.CloseCurrentPopup();
                        _isPasswordValid = true;
                        _showModalChangePassword = true;
                    }
                    UiSharedService.AttachToolTip("修改同步贝密码");
                }

                if (UiSharedService.IconTextButton(FontAwesomeIcon.Broom, "清理同步贝") && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = ApiController.GroupClear(groupDto);
                }
                UiSharedService.AttachToolTip("按住CTRL键并单击来清理此同步贝。" + Environment.NewLine + "警告：此操作不可逆。" + Environment.NewLine
                    + "清理同步贝将从中删除所有临时用户。");

                var groupSoundsText = soundsDisabled ? "启用同步贝声音同步" : "禁用同步贝声音同步";
                if (UiSharedService.IconTextButton(soundsIcon, groupSoundsText))
                {
                    ImGui.CloseCurrentPopup();
                    var perm = groupDto.GroupPermissions;
                    perm.SetDisableSounds(!perm.IsDisableSounds());
                    _ = ApiController.GroupChangeGroupPermissionState(new(groupDto.Group, perm));
                }
                UiSharedService.AttachToolTip("为此同步贝中所有成员设置声音同步开关。" + Environment.NewLine
                    + "注意：同步贝中单独配对的成员之间将忽略此设置。" + Environment.NewLine
                    + "注意：如果启用了同步，成员可以单独将此设置覆盖为禁用。");

                var groupAnimText = animDisabled ? "启用同步贝情感动作同步" : "禁用同步贝情感动作同步";
                if (UiSharedService.IconTextButton(animIcon, groupAnimText))
                {
                    ImGui.CloseCurrentPopup();
                    var perm = groupDto.GroupPermissions;
                    perm.SetDisableAnimations(!perm.IsDisableAnimations());
                    _ = ApiController.GroupChangeGroupPermissionState(new(groupDto.Group, perm));
                }
                UiSharedService.AttachToolTip("为此同步贝中所有成员设置情感动作同步开关。" + Environment.NewLine
                    + "注意：同步贝中单独配对的成员之间将忽略此设置。" + Environment.NewLine
                    + "注意：如果启用了同步，成员可以单独将此设置覆盖为禁用。");

                var groupVFXText = vfxDisabled ? "启用同步贝视觉效果同步" : "禁用同步贝视觉效果同步";
                if (UiSharedService.IconTextButton(vfxIcon, groupVFXText))
                {
                    ImGui.CloseCurrentPopup();
                    var perm = groupDto.GroupPermissions;
                    perm.SetDisableVFX(!perm.IsDisableVFX());
                    _ = ApiController.GroupChangeGroupPermissionState(new(groupDto.Group, perm));
                }
                UiSharedService.AttachToolTip("为此同步贝中所有成员设置视觉效果（VFX）同步开关。" + Environment.NewLine
                    + "注意：同步贝中单独配对的成员之间将忽略此设置。" + Environment.NewLine
                    + "注意：如果启用了同步，成员可以单独将此设置覆盖为禁用。");

                if (UiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "一次性邀请"))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.SetClipboardText(ApiController.GroupCreateTempInvite(groupDto, 1).Result.FirstOrDefault() ?? string.Empty);
                }
                UiSharedService.AttachToolTip("创建一个加入同步贝的一次性密码，有效期为24小时，并将其复制到剪贴板。");

                if (UiSharedService.IconTextButton(FontAwesomeIcon.MailBulk, "批量一次性邀请"))
                {
                    ImGui.CloseCurrentPopup();
                    _showModalBulkOneTimeInvites = true;
                    _bulkOneTimeInvites.Clear();
                }
                UiSharedService.AttachToolTip("打开一个对话框，创建最多100个用于加入同步贝的一次性密码。");

                if (UiSharedService.IconTextButton(FontAwesomeIcon.Ban, "管理黑名单"))
                {
                    ImGui.CloseCurrentPopup();
                    _showModalBanList = true;
                    _bannedUsers = ApiController.GroupGetBannedUsers(groupDto).Result;
                }

                if (isOwner)
                {
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除同步贝") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                    {
                        ImGui.CloseCurrentPopup();
                        _ = ApiController.GroupDelete(groupDto);
                    }
                    UiSharedService.AttachToolTip("按住CTRL+Shift键并单击来删除此同步贝。" + Environment.NewLine + "警告：此操作不可逆。");
                }
            }

            ImGui.EndPopup();
        }
    }

    private void DrawSyncshellList()
    {
        var ySize = _mainUi.TransferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _mainUi.TransferPartHeight - ImGui.GetCursorPosY();
        ImGui.BeginChild("list", new Vector2(_mainUi.WindowContentWidth, ySize), border: false);
        foreach (var entry in _pairManager.GroupPairs.OrderBy(g => g.Key.Group.AliasOrGID, StringComparer.OrdinalIgnoreCase).ToList())
        {
            UiSharedService.DrawWithID(entry.Key.Group.GID, () => DrawSyncshell(entry.Key, entry.Value));
        }
        ImGui.EndChild();
    }
}