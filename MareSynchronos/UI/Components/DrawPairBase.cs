using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairBase
{
    protected static bool _showModalReport = false;
    protected readonly ApiController _apiController;
    protected readonly UidDisplayHandler _displayHandler;
    protected Pair _pair;
    private static bool _reportPopupOpen = false;
    private static string _reportReason = string.Empty;
    private readonly string _id;

    protected DrawPairBase(string id, Pair entry, ApiController apiController, UidDisplayHandler uIDDisplayHandler)
    {
        _id = id;
        _pair = entry;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
    }

    public string UID => _pair.UserData.UID;

    public void DrawPairedClient()
    {
        var originalY = ImGui.GetCursorPosY();
        var pauseIconSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);
        var textSize = ImGui.CalcTextSize(_pair.UserData.AliasOrUID);

        var textPosY = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;
        DrawLeftSide(textPosY, originalY);
        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightSide(textPosY, originalY);
        DrawName(originalY, posX, rightSide);

        if (_showModalReport && !_reportPopupOpen)
        {
            ImGui.OpenPopup("举报月海档案");
            _reportPopupOpen = true;
        }

        if (!_showModalReport) _reportPopupOpen = false;

        if (ImGui.BeginPopupModal("举报月海档案", ref _showModalReport, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("举报 " + (_pair.UserData.AliasOrUID) + " 的月海档案");
            ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new System.Numerics.Vector2(500 - ImGui.GetStyle().ItemSpacing.X * 2, 200));
            UiSharedService.TextWrapped($"注意：发送举报后，有问题的档案可能会被全面禁用。{Environment.NewLine}" +
                $"报告将发送给当前连接中为您提供月海同步器服务的团队。{Environment.NewLine}" +
                $"报告将包括您的用户名和联系信息（Discord用户名）。{Environment.NewLine}" +
                $"根据违规的严重程度，该用户的月海档案或帐户可能被永久禁用或禁止。");
            UiSharedService.ColorTextWrapped("向管理团队发送垃圾信息或提供错误的举报将不被容忍，可能导致您的账户被永久停用。", ImGuiColors.DalamudRed);
            if (string.IsNullOrEmpty(_reportReason)) ImGui.BeginDisabled();
            if (ImGui.Button("发送举报"))
            {
                ImGui.CloseCurrentPopup();
                var reason = _reportReason;
                _ = _apiController.UserReportProfile(new(_pair.UserData, reason));
                _reportReason = string.Empty;
                _showModalReport = false;
                _reportPopupOpen = false;
            }
            if (string.IsNullOrEmpty(_reportReason)) ImGui.EndDisabled();
            UiSharedService.SetScaledWindowSize(500);
            ImGui.EndPopup();
        }
    }

    protected abstract void DrawLeftSide(float textPosY, float originalY);

    protected abstract float DrawRightSide(float textPosY, float originalY);

    private void DrawName(float originalY, float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, originalY, () => rightSide - leftSide);
    }
}