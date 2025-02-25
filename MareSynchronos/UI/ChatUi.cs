using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI
{
    public class ChatUi : WindowMediatorSubscriberBase
    {
        public static List<string> JoinedGroups = new();

        private readonly ILogger<ChatUi> _logger;
        private UiSharedService _uiSharedService;
        private ApiController _apiController;
        private PairManager _pairManager;
        private IdDisplayHandler _idDisplayHandler;
        private MareConfigService _mareConfig;
        private NotificationService _notificationService;

        private string _newMessage = string.Empty;
        private static List<ChatMessage> _chatLogs = new();
        private string _lastActiveGroup;

        public ChatUi(ILogger<ChatUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
            UiSharedService uiSharedService, ApiController apiController, PairManager pairManager,IdDisplayHandler idDisplayHandler,
            MareConfigService mareConfig, NotificationService notificationService) : base(logger, mediator, "同步贝聊天", performanceCollectorService)
        {
            _uiSharedService = uiSharedService;
            _apiController = apiController;
            _pairManager = pairManager;
            _idDisplayHandler = idDisplayHandler;
            _mareConfig = mareConfig;
            _notificationService = notificationService;
            _logger = logger;

            Mediator.Subscribe<ChatMessage>(this, HandleChatMessage);
            mediator.Subscribe<OpenChatUi>(this, _ => IsOpen = true);
#if DEBUG
            IsOpen = true;
#endif
            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(375, 400),
                MaximumSize = new Vector2(1000, 2000),
            };
        }

        private void HandleChatMessage(ChatMessage msg)
        {
            if (!JoinedGroups.Contains(msg.Group)) return;
            _chatLogs.Add(msg);
            if (_chatLogs.Count(x => x.Group == msg.Group) > 50)
            {
                _chatLogs.RemoveAt(_chatLogs.FindIndex(x => x.Group == msg.Group));
            }
            _logger.LogDebug($"Received chat message: '{msg.Message}' from {msg.Sender} in group {msg.Group}");
            if (_mareConfig.Current.PortToChatGui)
            {
                var groupName = _idDisplayHandler
                    .GetGroupText(_pairManager.Groups.First(x => x.Key.GID == msg.Group).Value).text;
                Mediator.Publish(new NotificationMessage(groupName, $"({GetName(msg)}): " + msg.Message, NotificationType.Chat));
            }
        }

        protected override void DrawInternal()
        {
            using (_uiSharedService.GameFont.Push())
            {
                if (ImGui.BeginTabBar("ChatLogs"))
                {

                    var groups = new List<string>(JoinedGroups);
                    foreach (string group in groups)
                    {
                        var IsOpen = true;
                        var groupName = _idDisplayHandler
                            .GetGroupText(_pairManager.Groups.First(x => x.Key.GID == group).Value).text;
                        if (ImGui.BeginTabItem(groupName, ref IsOpen))
                        {
                            if (_lastActiveGroup != group)
                            {
                                _newMessage = string.Empty;
                                _lastActiveGroup = group;
                            }

                            DrawChatLog(group);
                            ImGui.EndTabItem();
                        }

                        if (!IsOpen)
                        {
                            JoinedGroups.Remove(group);
                            if (_lastActiveGroup == group)
                            {
                                _lastActiveGroup = null;
                                _newMessage = string.Empty;
                            }
                        }
                    }
                    ImGui.EndTabBar();
                }
            }
        }

        private void DrawChatLog(string group)
        {
            unsafe
            {
                // 计算输入框的动态高度
                float availableWidth = ImGui.GetContentRegionAvail().X - 50; // 窗口可用宽度减去按钮宽度
                float inputHeight = ImGui.GetFrameHeightWithSpacing();

                // 设置聊天记录区域的高度，确保留出输入区域的空间
                float totalInputAreaHeight = inputHeight * 2 + ImGui.GetStyle().ItemSpacing.Y * 2; // 输入框 + 分隔线 + 按钮
                ImGui.BeginChild($"{group}##chatlog", new Vector2(0, -totalInputAreaHeight), true);
                foreach (ChatMessage msg in _chatLogs.Where(x => x.Group == group))
                {
                    if (msg.Sender == "SYSTEM-INFO")
                    {
                        ImGui.TextWrapped($"[{msg.LocalTime:HH:mm:ss}] 系统信息: {msg.Message}");
                        continue;
                    }

                    var name = GetName(msg);
                    var color = UiSharedService.IsSupporter(msg.Sender) ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite2;
                    ImGui.TextUnformatted($"[{msg.LocalTime:HH:mm:ss}]");
                    ImGui.SameLine();
                    UiSharedService.ColorText($"{name}", color);
                    ImGui.TextWrapped($"{msg.Message}");
                    ImGui.Spacing();
                }
                // 自动滚动到最新消息
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                    ImGui.SetScrollHereY(1.0f);
                ImGui.EndChild();

                // 分隔线
                ImGui.Separator();
                var port = _mareConfig.Current.PortToChatGui;
                if (ImGui.Checkbox("将聊天输出到游戏聊天框", ref port))
                {
                    _mareConfig.Current.PortToChatGui = port;
                    _mareConfig.Save();
                }
                // 使用精确的宽度确保换行一致
                var send = ImGui.InputTextMultiline("##chat_input", ref _newMessage, 4096,
                    new Vector2(availableWidth, inputHeight),
                    ImGuiInputTextFlags.CtrlEnterForNewLine | ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (inputHeight - ImGui.GetFrameHeight()) / 2); // 按钮底部对齐输入框
                if (ImGui.Button("发送") || send)
                {
                    if (!string.IsNullOrEmpty(_newMessage))
                    {
                        var msg = new GroupChatDto(new UserData(_apiController.UID), new GroupData(group), DateTime.UtcNow, _newMessage);
                        _ = _apiController.GroupChatServer(msg);
                        _newMessage = string.Empty; // 清空输入框
                    }
                }
            }
        }

        private string GetName(ChatMessage msg)
        {
            return _apiController.UID == msg.Sender ?
                _apiController.DisplayName :
                _idDisplayHandler.GetPlayerText(_pairManager.GetPairByUID(msg.Sender)).text;
        }
    }
}