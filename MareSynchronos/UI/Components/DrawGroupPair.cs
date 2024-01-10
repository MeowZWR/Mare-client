using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Dto.User;
using MareSynchronos.UI.Handlers;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.UI.Components;

public class DrawGroupPair : DrawPairBase
{
    private static string _banReason = string.Empty;
    private static bool _banUserPopupOpen;
    private static bool _showModalBanUser;
    private readonly GroupPairFullInfoDto _fullInfoDto;
    private readonly GroupFullInfoDto _group;

    public DrawGroupPair(string id, Pair entry, ApiController apiController, GroupFullInfoDto group, GroupPairFullInfoDto fullInfoDto, UidDisplayHandler handler) : base(id, entry, apiController, handler)
    {
        _group = group;
        _fullInfoDto = fullInfoDto;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        var entryUID = _pair.UserData.AliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var presenceIcon = _pair.IsVisible ? FontAwesomeIcon.Eye : (_pair.IsOnline ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink);
        var presenceColor = (_pair.IsOnline || _pair.IsVisible) ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var presenceText = entryUID + " 已离线";

        ImGui.SetCursorPosY(textPosY);
        if (_pair.IsPaused)
        {
            presenceIcon = FontAwesomeIcon.Question;
            presenceColor = ImGuiColors.DalamudGrey;
            presenceText = entryUID + " 联机状态未知（已暂停）";

            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
            ImGui.PopFont();

            UiSharedService.AttachToolTip("与 " + entryUID + " 的配对已暂停");
        }
        else
        {
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.Check.ToIconString(), ImGuiColors.ParsedGreen);
            ImGui.PopFont();

            UiSharedService.AttachToolTip("您与 " + entryUID + "已配对");
        }

        if (_pair.IsOnline && !_pair.IsVisible) presenceText = entryUID + " 上线了";
        else if (_pair.IsOnline && _pair.IsVisible) presenceText = entryUID + " is visible: " + _pair.PlayerName;

        ImGui.SameLine();
        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(presenceIcon.ToIconString(), presenceColor);
        ImGui.PopFont();
        UiSharedService.AttachToolTip(presenceText);

        if (entryIsOwner)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("用户是此同步贝的所有者");
        }
        else if (entryIsMod)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("用户是此同步贝的主持人");
        }
        else if (entryIsPinned)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("用户是此同步贝的固定成员");
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var entryUID = _fullInfoDto.UserAliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var userIsOwner = string.Equals(_group.OwnerUID, _apiController.UID, StringComparison.OrdinalIgnoreCase);
        var userIsModerator = _group.GroupUserInfo.IsModerator();

        var soundsDisabled = _fullInfoDto.GroupUserPermissions.IsDisableSounds();
        var animDisabled = _fullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var vfxDisabled = _fullInfoDto.GroupUserPermissions.IsDisableVFX();
        var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
        var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
        var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

        bool showInfo = (individualAnimDisabled || individualSoundsDisabled || animDisabled || soundsDisabled);
        bool showPlus = _pair.UserPair == null;
        bool showBars = (userIsOwner || (userIsModerator && !entryIsMod && !entryIsOwner)) || !_pair.IsPaused;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var permIcon = (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled) ? FontAwesomeIcon.ExclamationTriangle
            : ((soundsDisabled || animDisabled || vfxDisabled) ? FontAwesomeIcon.InfoCircle : FontAwesomeIcon.None);
        var infoIconWidth = UiSharedService.GetIconSize(permIcon).X;
        var plusButtonWidth = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X;
        var barButtonWidth = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X;

        var pos = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() + spacing
            - (showInfo ? (infoIconWidth + spacing) : 0)
            - (showPlus ? (plusButtonWidth + spacing) : 0)
            - (showBars ? (barButtonWidth + spacing) : 0);

        ImGui.SameLine(pos);
        if (individualAnimDisabled || individualSoundsDisabled)
        {
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            UiSharedService.FontText(permIcon.ToIconString(), UiBuilder.IconFont);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();

                ImGui.Text("独立用户权限");

                if (individualSoundsDisabled)
                {
                    var userSoundsText = "与 " + _pair.UserData.AliasOrUID + " 的声音同步已禁用";
                    UiSharedService.FontText(FontAwesomeIcon.VolumeOff.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userSoundsText);
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text("你：" + (_pair.UserPair!.OwnPermissions.IsDisableSounds() ? "禁用" : "启用") + "，他们：" + (_pair.UserPair!.OtherPermissions.IsDisableSounds() ? "禁用" : "启用"));
                }

                if (individualAnimDisabled)
                {
                    var userAnimText = "与 " + _pair.UserData.AliasOrUID + " 的情感动作同步已禁用";
                    UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userAnimText);
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text("你：" + (_pair.UserPair!.OwnPermissions.IsDisableAnimations() ? "禁用" : "启用") + "，他们：" + (_pair.UserPair!.OtherPermissions.IsDisableAnimations() ? "禁用" : "启用"));
                }

                if (individualVFXDisabled)
                {
                    var userVFXText = "与 " + _pair.UserData.AliasOrUID + " 的视觉特效同步已禁用";
                    UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userVFXText);
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text("你：" + (_pair.UserPair!.OwnPermissions.IsDisableVFX() ? "禁用" : "启用") + "，他们：" + (_pair.UserPair!.OtherPermissions.IsDisableVFX() ? "禁用" : "启用"));
                }

                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }
        else if ((animDisabled || soundsDisabled))
        {
            ImGui.SetCursorPosY(textPosY);
            UiSharedService.FontText(permIcon.ToIconString(), UiBuilder.IconFont);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();

                ImGui.Text("同步贝用户权限");

                if (soundsDisabled)
                {
                    var userSoundsText = "来自 " + _pair.UserData.AliasOrUID + " 的声音同步已禁用";
                    UiSharedService.FontText(FontAwesomeIcon.VolumeOff.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userSoundsText);
                }

                if (animDisabled)
                {
                    var userAnimText = "来自 " + _pair.UserData.AliasOrUID + " 的情感动作同步已禁用";
                    UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userAnimText);
                }

                if (vfxDisabled)
                {
                    var userVFXText = "来自 " + _pair.UserData.AliasOrUID + " 的视觉特效同步已禁用";
                    UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.Text(userVFXText);
                }

                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }

        if (showPlus)
        {
            ImGui.SetCursorPosY(originalY);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _ = _apiController.UserAddPair(new UserDto(new(_pair.UserData.UID)));
            }
            UiSharedService.AttachToolTip("与 " + entryUID + " 进行独立配对");
            ImGui.SameLine();
        }

        if (showBars)
        {
            ImGui.SetCursorPosY(originalY);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
            {
                ImGui.OpenPopup("Popup");
            }
        }

        if (ImGui.BeginPopup("Popup"))
        {
            if ((userIsModerator || userIsOwner) && !(entryIsMod || entryIsOwner))
            {
                var pinText = entryIsPinned ? "临时成员" : "固定成员";
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Thumbtack, pinText))
                {
                    ImGui.CloseCurrentPopup();
                    var userInfo = _fullInfoDto.GroupPairStatusInfo ^ GroupUserInfo.IsPinned;
                    _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_fullInfoDto.Group, _fullInfoDto.User, userInfo));
                }
                UiSharedService.AttachToolTip("将此成员固定到同步贝。在手动启动同步贝清理的情况下，固定成员将不会被清除");

                if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "移除成员") && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupRemoveUser(_fullInfoDto);
                }

                UiSharedService.AttachToolTip("按住CTRL键并单击以从同步贝 " + (_pair.UserData.AliasOrUID) + " 删除该用户");
                if (UiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "禁止用户"))
                {
                    _showModalBanUser = true;
                    ImGui.CloseCurrentPopup();
                }
                UiSharedService.AttachToolTip("禁止用户进入此同步贝");
            }

            if (userIsOwner)
            {
                string modText = entryIsMod ? "降级成员" : "提升成员";
                if (UiSharedService.IconTextButton(FontAwesomeIcon.UserShield, modText) && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    var userInfo = _fullInfoDto.GroupPairStatusInfo ^ GroupUserInfo.IsModerator;
                    _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_fullInfoDto.Group, _fullInfoDto.User, userInfo));
                }
                UiSharedService.AttachToolTip("按住CTRL键可更改主持人权限给 " + (_fullInfoDto.UserAliasOrUID) + Environment.NewLine +
                    "主持人可以踢出、禁止/取消禁止、固定/取消固定成员和清理同步贝。");
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Crown, "移交所有权") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupChangeOwnership(_fullInfoDto);
                }
                UiSharedService.AttachToolTip("按住CTRL+SHIFT键并单击，将此同步贝的所有权转移给 " + (_fullInfoDto.UserAliasOrUID) + Environment.NewLine + "警告：此操作不可逆。");
            }

            ImGui.Separator();
            if (!_pair.IsPaused)
            {
                if (UiSharedService.IconTextButton(FontAwesomeIcon.User, "打开月海档案"))
                {
                    _displayHandler.OpenProfile(_pair);
                    ImGui.CloseCurrentPopup();
                }
                UiSharedService.AttachToolTip("在新窗口中打开此用户的月海档案");
                if (UiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "举报月海档案"))
                {
                    ImGui.CloseCurrentPopup();
                    _showModalReport = true;
                }
                UiSharedService.AttachToolTip("向管理团队举报此用户的月海档案");
            }
            ImGui.EndPopup();
        }

        if (_showModalBanUser && !_banUserPopupOpen)
        {
            ImGui.OpenPopup("禁止用户");
            _banUserPopupOpen = true;
        }

        if (!_showModalBanUser) _banUserPopupOpen = false;

        if (ImGui.BeginPopupModal("禁止用户", ref _showModalBanUser, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("用户 " + (_fullInfoDto.UserAliasOrUID) + " 将被移出同步贝并禁止进入。");
            ImGui.InputTextWithHint("##banreason", "禁止原因", ref _banReason, 255);
            if (ImGui.Button("禁止用户"))
            {
                ImGui.CloseCurrentPopup();
                var reason = _banReason;
                _ = _apiController.GroupBanUser(new GroupPairDto(_group.Group, _fullInfoDto.User), reason);
                _banReason = string.Empty;
            }
            UiSharedService.TextWrapped("原因将显示在公告栏中。当前服务器端别名如果存在（自定义ID）将自动附加到原因。");
            UiSharedService.SetScaledWindowSize(300);
            ImGui.EndPopup();
        }

        return pos - spacing;
    }
}