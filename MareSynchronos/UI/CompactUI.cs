﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
    public float TransferPartHeight;
    public float WindowContentWidth;
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly FileUploadManager _fileTransferManager;
    private readonly GroupPanel _groupPanel;
    private readonly PairGroupsUi _pairGroupsUi;
    private readonly PairManager _pairManager;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;
    private readonly SelectPairForGroupUi _selectPairsForGroupUi;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Stopwatch _timeout = new();
    private readonly UidDisplayHandler _uidDisplayHandler;
    private readonly UiSharedService _uiShared;
    private bool _buttonState;
    private string _characterOrCommentFilter = string.Empty;
    private Pair? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private string _pairToAdd = string.Empty;
    private int _secretKeyIdx = -1;
    private bool _showModalForUserAddition;
    private bool _showSyncShells;
    private bool _wasOpen;

    public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager, UidDisplayHandler uidDisplayHandler) : base(logger, mediator, "###MareSynchronosMainUI")
    {
        _uiShared = uiShared;
        _configService = configService;
        _apiController = apiController;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _fileTransferManager = fileTransferManager;
        _uidDisplayHandler = uidDisplayHandler;
        var tagHandler = new TagHandler(_serverManager);

        _groupPanel = new(this, uiShared, _pairManager, uidDisplayHandler, _serverManager);
        _selectGroupForPairUi = new(tagHandler, uidDisplayHandler);
        _selectPairsForGroupUi = new(tagHandler, uidDisplayHandler);
        _pairGroupsUi = new(configService, tagHandler, apiController, _selectPairsForGroupUi);

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Mare Synchronos {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###MareSynchronosMainUI";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        WindowName = "Mare Synchronos " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosMainUI";
#endif
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));

        Flags |= ImGuiWindowFlags.NoDocking;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
        };
    }

    public override void Draw()
    {
        WindowContentWidth = UiSharedService.GetWindowContentRegionWidth();
        if (!_apiController.IsCurrentVersion)
        {
            var ver = _apiController.CurrentClientVersion;
            if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
            var unsupported = "不支持的版本";
            var uidTextSize = ImGui.CalcTextSize(unsupported);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
            ImGui.TextColored(ImGuiColors.DalamudRed, unsupported);
            if (_uiShared.UidFontBuilt) ImGui.PopFont();
            UiSharedService.ColorTextWrapped($"您安装的月海同步器版本已过期，当前版本问：{ver.Major}.{ver.Minor}.{ver.Build}。 " +
                $"强烈建议更新月海同步器到最新版本。打开插件管理器/xlplugins并更新插件。", ImGuiColors.DalamudRed);
        }

        UiSharedService.DrawWithID("header", DrawUIDHeader);
        ImGui.Separator();
        UiSharedService.DrawWithID("serverstatus", DrawServerStatus);

        if (_apiController.ServerState is ServerState.Connected)
        {
            var hasShownSyncShells = _showSyncShells;

            ImGui.PushFont(UiBuilder.IconFont);
            if (!hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = false;
            }
            if (!hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();
            UiSharedService.AttachToolTip("独立配对");

            ImGui.SameLine();

            ImGui.PushFont(UiBuilder.IconFont);
            if (hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = true;
            }
            if (hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();

            UiSharedService.AttachToolTip("同步贝");

            ImGui.Separator();
            if (!hasShownSyncShells)
            {
                UiSharedService.DrawWithID("pairlist", DrawPairList);
            }
            else
            {
                UiSharedService.DrawWithID("syncshells", _groupPanel.DrawSyncshells);
            }
            ImGui.Separator();
            UiSharedService.DrawWithID("transfers", DrawTransfers);
            TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
            UiSharedService.DrawWithID("group-user-popup", () => _selectPairsForGroupUi.Draw(_pairManager.DirectPairs));
            UiSharedService.DrawWithID("grouping-popup", () => _selectGroupForPairUi.Draw());
        }

        if (_configService.Current.OpenPopupOnAdd && _pairManager.LastAddedUser != null)
        {
            _lastAddedUser = _pairManager.LastAddedUser;
            _pairManager.LastAddedUser = null;
            ImGui.OpenPopup("为新用户设置备注");
            _showModalForUserAddition = true;
            _lastAddedUserComment = string.Empty;
        }

        if (ImGui.BeginPopupModal("为新用户设置备注", ref _showModalForUserAddition, UiSharedService.PopupWindowFlags))
        {
            if (_lastAddedUser == null)
            {
                _showModalForUserAddition = false;
            }
            else
            {
                UiSharedService.TextWrapped($"您已成功添加 {_lastAddedUser.UserData.AliasOrUID}。在下面的字段中为用户设置本地备注：");
                ImGui.InputTextWithHint("##noteforuser", $"Note for {_lastAddedUser.UserData.AliasOrUID}", ref _lastAddedUserComment, 100);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Save, "保存备注"))
                {
                    _serverManager.SetNoteForUid(_lastAddedUser.UserData.UID, _lastAddedUserComment);
                    _lastAddedUser = null;
                    _lastAddedUserComment = string.Empty;
                    _showModalForUserAddition = false;
                }
            }
            UiSharedService.SetScaledWindowSize(275);
            ImGui.EndPopup();
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
        }
    }

    public override void OnClose()
    {
        _uidDisplayHandler.Clear();
        base.OnClose();
    }

    private void DrawAddCharacter()
    {
        ImGui.Dummy(new(10));
        var keys = _serverManager.CurrentServer!.SecretKeys;
        if (keys.Any())
        {
            if (_secretKeyIdx == -1) _secretKeyIdx = keys.First().Key;
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Plus, "为当前角色添加密钥"))
            {
                _serverManager.CurrentServer!.Authentications.Add(new MareConfiguration.Models.Authentication()
                {
                    CharacterName = _uiShared.PlayerName,
                    WorldId = _uiShared.WorldId,
                    SecretKeyIdx = _secretKeyIdx
                });

                _serverManager.Save();

                _ = _apiController.CreateConnections(forceGetToken: true);
            }

            _uiShared.DrawCombo("密钥##addCharacterSecretKey", keys, (f) => f.Value.FriendlyName, (f) => _secretKeyIdx = f.Key);
        }
        else
        {
            UiSharedService.ColorTextWrapped("没有为当前服务器配置密钥。", ImGuiColors.DalamudYellow);
        }
    }

    private void DrawAddPair()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##otheruid", "其他玩家UID或别名", ref _pairToAdd, 20);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        var canAdd = !_pairManager.DirectPairs.Any(p => string.Equals(p.UserData.UID, _pairToAdd, StringComparison.Ordinal) || string.Equals(p.UserData.Alias, _pairToAdd, StringComparison.Ordinal));
        if (!canAdd)
        {
            ImGuiComponents.DisabledButton(FontAwesomeIcon.Plus);
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                _ = _apiController.UserAddPair(new(new(_pairToAdd)));
                _pairToAdd = string.Empty;
            }
            UiSharedService.AttachToolTip("与" + (_pairToAdd.IsNullOrEmpty() ? "其他玩家" : _pairToAdd + "配对"));
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawFilter()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowUp);
        var playButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);
        if (!_configService.Current.ReverseUserSort)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
            {
                _configService.Current.ReverseUserSort = true;
                _configService.Save();
            }
            UiSharedService.AttachToolTip("按名称降序排列");
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
            {
                _configService.Current.ReverseUserSort = false;
                _configService.Save();
            }
            UiSharedService.AttachToolTip("按名称升序排列");
        }
        ImGui.SameLine();

        var users = GetFilteredUsers();
        var userCount = users.Count;

        var spacing = userCount > 0
            ? playButtonSize.X + ImGui.GetStyle().ItemSpacing.X * 2
            : ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(WindowContentWidth - buttonSize.X - spacing);
        ImGui.InputTextWithHint("##filter", "根据UID或备注筛选", ref _characterOrCommentFilter, 255);

        if (userCount == 0) return;

        var pausedUsers = users.Where(u => u.UserPair!.OwnPermissions.IsPaused() && u.UserPair.OtherPermissions.IsPaired()).ToList();
        var resumedUsers = users.Where(u => !u.UserPair!.OwnPermissions.IsPaused() && u.UserPair.OtherPermissions.IsPaired()).ToList();

        if (!pausedUsers.Any() && !resumedUsers.Any()) return;
        ImGui.SameLine();

        switch (_buttonState)
        {
            case true when !pausedUsers.Any():
                _buttonState = false;
                break;

            case false when !resumedUsers.Any():
                _buttonState = true;
                break;

            case true:
                users = pausedUsers;
                break;

            case false:
                users = resumedUsers;
                break;
        }

        var button = _buttonState ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;

        if (!_timeout.IsRunning || _timeout.ElapsedMilliseconds > 15000)
        {
            _timeout.Reset();

            if (ImGuiComponents.IconButton(button) && UiSharedService.CtrlPressed())
            {
                foreach (var entry in users)
                {
                    var perm = entry.UserPair!.OwnPermissions;
                    perm.SetPaused(!perm.IsPaused());
                    _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, perm));
                }

                _timeout.Start();
                _buttonState = !_buttonState;
            }
            UiSharedService.AttachToolTip($"Hold Control to {(button == FontAwesomeIcon.Play ? "resume" : "pause")} pairing with {users.Count} out of {userCount} displayed users.");
        }
        else
        {
            var availableAt = (15000 - _timeout.ElapsedMilliseconds) / 1000;
            ImGuiComponents.DisabledButton(button);
            UiSharedService.AttachToolTip($"Next execution is available at {availableAt} seconds");
        }
    }

    private void DrawPairList()
    {
        UiSharedService.DrawWithID("addpair", DrawAddPair);
        UiSharedService.DrawWithID("pairs", DrawPairs);
        TransferPartHeight = ImGui.GetCursorPosY();
        UiSharedService.DrawWithID("filter", DrawFilter);
    }

    private void DrawPairs()
    {
        var ySize = TransferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - TransferPartHeight - ImGui.GetCursorPosY();
        var users = GetFilteredUsers()
            .OrderBy(
                u => _configService.Current.ShowCharacterNameInsteadOfNotesForVisible && !string.IsNullOrEmpty(u.PlayerName)
                    ? (_configService.Current.PreferNotesOverNamesForVisible ? u.GetNote() : u.PlayerName)
                    : (u.GetNote() ?? u.UserData.AliasOrUID), StringComparer.OrdinalIgnoreCase).ToList();

        if (_configService.Current.ReverseUserSort)
        {
            users.Reverse();
        }

        var onlineUsers = users.Where(u => u.IsOnline || u.UserPair!.OwnPermissions.IsPaused()).Select(c => new DrawUserPair("Online" + c.UserData.UID, c, _uidDisplayHandler, _apiController, _selectGroupForPairUi)).ToList();
        var visibleUsers = users.Where(u => u.IsVisible).Select(c => new DrawUserPair("Visible" + c.UserData.UID, c, _uidDisplayHandler, _apiController, _selectGroupForPairUi)).ToList();
        var offlineUsers = users.Where(u => !u.IsOnline && !u.UserPair!.OwnPermissions.IsPaused()).Select(c => new DrawUserPair("Offline" + c.UserData.UID, c, _uidDisplayHandler, _apiController, _selectGroupForPairUi)).ToList();

        ImGui.BeginChild("list", new Vector2(WindowContentWidth, ySize), border: false);

        _pairGroupsUi.Draw(visibleUsers, onlineUsers, offlineUsers);

        ImGui.EndChild();
    }

    private void DrawServerStatus()
    {
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("用户在线");
#if DEBUG
        string shardConnection = $"Shard: {_apiController.ServerInfo.ShardName}";
#else
        string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}";
#endif
        var shardTextSize = ImGui.CalcTextSize(shardConnection);
        var printShard = !string.IsNullOrEmpty(_apiController.ServerInfo.ShardName) && shardConnection != string.Empty;

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.ParsedGreen, userCount);
            ImGui.SameLine();
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.Text("用户在线");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.DalamudRed, "未连接到任何服务器");
        }

        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(shardConnection);
        }

        ImGui.SameLine();
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }
        var color = UiSharedService.GetBoolColor(!_serverManager.CurrentServer!.FullPause);
        var connectedIcon = !_serverManager.CurrentServer.FullPause ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX(0 + ImGui.GetStyle().ItemSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.UserCircle))
            {
                Mediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
            }
            UiSharedService.AttachToolTip("编辑您的月海档案");
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        if (_apiController.ServerState is not (ServerState.Reconnecting or ServerState.Disconnecting))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGuiComponents.IconButton(connectedIcon))
            {
                _serverManager.CurrentServer.FullPause = !_serverManager.CurrentServer.FullPause;
                _serverManager.Save();
                _ = _apiController.CreateConnections();
            }
            ImGui.PopStyleColor();
            UiSharedService.AttachToolTip(!_serverManager.CurrentServer.FullPause ? "断开服务器：" + _serverManager.CurrentServer.ServerName : "连接服务器：" + _serverManager.CurrentServer.ServerName);
        }
    }

    private void DrawTransfers()
    {
        var currentUploads = _fileTransferManager.CurrentUploads.ToList();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Upload.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentUploads.Any())
        {
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.Text($"{doneUploads}/{totalUploads}");
            var uploadText = $"({UiSharedService.ByteToString(totalUploaded)}/{UiSharedService.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(WindowContentWidth - textSize.X);
            ImGui.Text(uploadText);
        }
        else
        {
            ImGui.Text("没有上传任务");
        }

        var currentDownloads = _currentDownloads.SelectMany(d => d.Value.Values).ToList();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Download.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentDownloads.Any())
        {
            var totalDownloads = currentDownloads.Sum(c => c.TotalFiles);
            var doneDownloads = currentDownloads.Sum(c => c.TransferredFiles);
            var totalDownloaded = currentDownloads.Sum(c => c.TransferredBytes);
            var totalToDownload = currentDownloads.Sum(c => c.TotalBytes);

            ImGui.Text($"{doneDownloads}/{totalDownloads}");
            var downloadText =
                $"({UiSharedService.ByteToString(totalDownloaded)}/{UiSharedService.ByteToString(totalToDownload)})";
            var textSize = ImGui.CalcTextSize(downloadText);
            ImGui.SameLine(WindowContentWidth - textSize.X);
            ImGui.Text(downloadText);
        }
        else
        {
            ImGui.Text("没有下载任务");
        }

        if (UiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "月海角色数据分析", WindowContentWidth))
        {
            Mediator.Publish(new OpenDataAnalysisUiMessage());
        }

        ImGui.SameLine();
    }

    private void DrawUIDHeader()
    {
        var uidText = GetUidText();
        var buttonSizeX = 0f;

        if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
        var uidTextSize = ImGui.CalcTextSize(uidText);
        if (_uiShared.UidFontBuilt) ImGui.PopFont();

        var originalPos = ImGui.GetCursorPos();
        ImGui.SetWindowFontScale(1.5f);
        var buttonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Cog);
        buttonSizeX -= buttonSize.X - ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            Mediator.Publish(new OpenSettingsUiMessage());
        }
        UiSharedService.AttachToolTip("打开设置窗口");

        ImGui.SameLine(); //Important to draw the uidText consistently
        ImGui.SetCursorPos(originalPos);

        if (_apiController.ServerState is ServerState.Connected)
        {
            buttonSizeX += UiSharedService.GetIconButtonSize(FontAwesomeIcon.Copy).X - ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_apiController.DisplayName);
            }
            UiSharedService.AttachToolTip("复制您的UID到剪贴板");
            ImGui.SameLine();
        }
        ImGui.SetWindowFontScale(1f);

        ImGui.SetCursorPosY(originalPos.Y + buttonSize.Y / 2 - uidTextSize.Y / 2 - ImGui.GetStyle().ItemSpacing.Y / 2);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 + buttonSizeX - uidTextSize.X / 2);
        if (_uiShared.UidFontBuilt) ImGui.PushFont(_uiShared.UidFont);
        ImGui.TextColored(GetUidColor(), uidText);
        if (_uiShared.UidFontBuilt) ImGui.PopFont();

        if (_apiController.ServerState is not ServerState.Connected)
        {
            UiSharedService.ColorTextWrapped(GetServerError(), GetUidColor());
            if (_apiController.ServerState is ServerState.NoSecretKey)
            {
                DrawAddCharacter();
            }
        }
    }

    private List<Pair> GetFilteredUsers()
    {
        return _pairManager.DirectPairs.Where(p =>
        {
            if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
            return p.UserData.AliasOrUID.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                   (p.GetNote()?.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (p.PlayerName?.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList();
    }

    private string GetServerError()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => "正在尝试连接服务器。",
            ServerState.Reconnecting => "与服务器的连接中断，正在尝试重新连接。",
            ServerState.Disconnected => "您当前已断开与月海同步服务器的连接。",
            ServerState.Disconnecting => "正在断开与服务器的连接。",
            ServerState.Unauthorized => "服务器响应：" + _apiController.AuthFailureMessage,
            ServerState.Offline => "您选择的月海同步服务器当前处于脱机状态。",
            ServerState.VersionMisMatch =>
                "您的插件或连接到的服务器已过期。请立即更新您的插件。如果您已经这样做了，请联系服务器提供商，将其服务器更新到最新版本。",
            ServerState.RateLimited => "您因过于频繁地重新连接而受到限制。请断开连接并等待10分钟，然后重试。",
            ServerState.Connected => string.Empty,
            ServerState.NoSecretKey => "您没有为当前角色设置密钥。使用下面的按钮或打开设置为当前角色设置密钥。您可以对多个角色使用同一密钥。",
            _ => string.Empty
        };
    }

    private Vector4 GetUidColor()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudRed,
            ServerState.Connected => ImGuiColors.ParsedGreen,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.RateLimited => ImGuiColors.DalamudYellow,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    private string GetUidText()
    {
        return _apiController.ServerState switch
        {
            ServerState.Reconnecting => "正在重新连接",
            ServerState.Connecting => "正在连接",
            ServerState.Disconnected => "已断开连接",
            ServerState.Disconnecting => "正在断开连接",
            ServerState.Unauthorized => "未认证",
            ServerState.VersionMisMatch => "版本不匹配",
            ServerState.Offline => "不可用",
            ServerState.RateLimited => "速率限制",
            ServerState.NoSecretKey => "无密钥",
            ServerState.Connected => _apiController.DisplayName,
            _ => string.Empty
        };
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}