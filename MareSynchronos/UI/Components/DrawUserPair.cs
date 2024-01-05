using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using System.Numerics;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Dto.User;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class DrawUserPair : DrawPairBase
{
    private readonly SelectGroupForPairUi _selectGroupForPairUi;

    public DrawUserPair(string id, Pair entry, UidDisplayHandler displayHandler, ApiController apiController, SelectGroupForPairUi selectGroupForPairUi) : base(id, entry, apiController, displayHandler)
    {
        if (_pair.UserPair == null) throw new ArgumentException("配对必须是用户", nameof(entry));
        _pair = entry;
        _selectGroupForPairUi = selectGroupForPairUi;
    }

    public bool IsOnline => _pair.IsOnline;
    public bool IsVisible => _pair.IsVisible;
    public UserPairDto UserPair => _pair.UserPair!;

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        FontAwesomeIcon connectionIcon;
        Vector4 connectionColor;
        string connectionText;
        if (!(_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired()))
        {
            connectionIcon = FontAwesomeIcon.ArrowUp;
            connectionText = _pair.UserData.AliasOrUID + " 还未回加您";
            connectionColor = ImGuiColors.DalamudRed;
        }
        else if (_pair.UserPair!.OwnPermissions.IsPaused() || _pair.UserPair!.OtherPermissions.IsPaused())
        {
            connectionIcon = FontAwesomeIcon.PauseCircle;
            connectionText = "与 " + _pair.UserData.AliasOrUID + " 的配对已暂停";
            connectionColor = ImGuiColors.DalamudYellow;
        }
        else
        {
            connectionIcon = FontAwesomeIcon.Check;
            connectionText = "您已与此用户配对：" + _pair.UserData.AliasOrUID;
            connectionColor = ImGuiColors.ParsedGreen;
        }

        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(connectionIcon.ToIconString(), connectionColor);
        ImGui.PopFont();
        UiSharedService.AttachToolTip(connectionText);
        if (_pair is { IsOnline: true, IsVisible: true })
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.Eye.ToIconString(), ImGuiColors.ParsedGreen);
            ImGui.PopFont();
            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " 可见：" + _pair.PlayerName!);
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var pauseIcon = _pair.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = UiSharedService.GetIconButtonSize(pauseIcon);
        var barButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var entryUID = _pair.UserData.AliasOrUID;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        var rightSideStart = 0f;

        if (_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired())
        {
            var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
            var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
            var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

            if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
            {
                var infoIconPosDist = windowEndX - barButtonSize.X - spacingX - pauseIconSize.X - spacingX;
                var icon = FontAwesomeIcon.ExclamationTriangle;
                var iconwidth = UiSharedService.GetIconSize(icon);

                rightSideStart = infoIconPosDist - iconwidth.X;
                ImGui.SameLine(infoIconPosDist - iconwidth.X);

                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
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
                        var userAnimText = "Animation sync disabled with " + _pair.UserData.AliasOrUID;
                        UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("你：" + (_pair.UserPair!.OwnPermissions.IsDisableAnimations() ? "禁用" : "启用") + "，他们：" + (_pair.UserPair!.OtherPermissions.IsDisableAnimations() ? "禁用" : "启用"));
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = "VFX sync disabled with " + _pair.UserData.AliasOrUID;
                        UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("你：" + (_pair.UserPair!.OwnPermissions.IsDisableVFX() ? "禁用" : "启用") + "，他们：" + (_pair.UserPair!.OtherPermissions.IsDisableVFX() ? "禁用" : "启用"));
                    }

                    ImGui.EndTooltip();
                }
            }

            if (rightSideStart == 0f)
            {
                rightSideStart = windowEndX - barButtonSize.X - spacingX * 2 - pauseIconSize.X;
            }
            ImGui.SameLine(windowEndX - barButtonSize.X - spacingX - pauseIconSize.X);
            ImGui.SetCursorPosY(originalY);
            if (ImGuiComponents.IconButton(pauseIcon))
            {
                var perm = _pair.UserPair!.OwnPermissions;
                perm.SetPaused(!perm.IsPaused());
                _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
            }
            UiSharedService.AttachToolTip(!_pair.UserPair!.OwnPermissions.IsPaused()
                ? "暂停与这个用户的配对：" + entryUID
                : "恢复与这个用户的配对：" + entryUID);
        }

        // Flyout Menu
        if (rightSideStart == 0f)
        {
            rightSideStart = windowEndX - barButtonSize.X;
        }
        ImGui.SameLine(windowEndX - barButtonSize.X);
        ImGui.SetCursorPosY(originalY);

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }
        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            UiSharedService.DrawWithID($"buttons-{_pair.UserData.UID}", () => DrawPairedClientMenu(_pair));
            ImGui.EndPopup();
        }

        return rightSideStart;
    }

    private void DrawPairedClientMenu(Pair entry)
    {
        if (!entry.IsPaused)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.User, "打开档案"))
            {
                _displayHandler.OpenProfile(entry);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("在新窗口中打开此用户的档案");
        }
        if (entry.IsVisible)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Sync, "重新加载最后一次数据"))
            {
                entry.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("这将上次接收的角色数据重新应用到此角色");
        }

        if (UiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "循环暂停状态"))
        {
            _ = _apiController.CyclePause(entry.UserData);
            ImGui.CloseCurrentPopup();
        }
        var entryUID = entry.UserData.AliasOrUID;
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Folder, "配对组"))
        {
            _selectGroupForPairUi.Open(entry);
        }
        UiSharedService.AttachToolTip("为 " + entryUID + " 选择配对组");

        var isDisableSounds = entry.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "启用声音同步" : "禁用声音同步";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute;
        if (UiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        var isDisableAnims = entry.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "启用情感动作同步" : "禁用情感动作同步";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (UiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        var isDisableVFX = entry.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "启用视觉特效VFX同步" : "禁用视觉特效VFX同步";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (UiSharedService.IconTextButton(disableVFXIcon, disableVFXText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "永久取消配对") && UiSharedService.CtrlPressed())
        {
            _ = _apiController.UserRemovePair(new(entry.UserData));
        }
        UiSharedService.AttachToolTip("按住CTRL键单击以永久取消与 " + entryUID + " 的配对。");

        ImGui.Separator();
        if (!entry.IsPaused)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "举报月海档案"))
            {
                ImGui.CloseCurrentPopup();
                _showModalReport = true;
            }
            UiSharedService.AttachToolTip("向管理团队举报此用户的月海档案文件");
        }
    }
}