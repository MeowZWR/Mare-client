using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI
{
    public partial class PFinderWindow : WindowMediatorSubscriberBase
    {

        private readonly MareConfigService _configService;
        private readonly CacheMonitor _cacheMonitor;
        private readonly ServerConfigurationManager _serverConfigurationManager;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly UiSharedService _uiShared;
        private readonly ApiController _apiController;

        private readonly TimeSpan CoolDown = TimeSpan.FromSeconds(15);

        private List<PFinderDto> _pfs = [];
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _autoRefresh = false;
        private string fliter = "";
        private bool Disable => _lastUpdate + CoolDown > DateTime.Now;

        public PFinderWindow(ILogger<PFinderWindow> logger, UiSharedService uiShared, MareConfigService configService,
            CacheMonitor fileCacheManager, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator,
            PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService, ApiController apiController
            ) : base(logger, mareMediator, "招募中心", performanceCollectorService)
        {
            _uiShared = uiShared;
            _configService = configService;
            _cacheMonitor = fileCacheManager;
            _serverConfigurationManager = serverConfigurationManager;
            _dalamudUtilService = dalamudUtilService;
            _apiController = apiController;
            IsOpen = false;
            ShowCloseButton = true;
            RespectCloseHotkey = false;
            AllowClickthrough = false;
            AllowPinning = false;

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(800, 400),
                MaximumSize = new Vector2(800, 2000),
            };

            Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        }

        public override void OnOpen()
        {
            if (!_apiController.IsConnected) return;
            if (_lastUpdate + CoolDown < DateTime.Now)
            {
                _pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
                if (_pfs.Count > 0) _lastUpdate = DateTime.Now;
            }
        }

        protected override void DrawInternal()
        {
            if (!_apiController.IsConnected) return;

            ImGui.SetNextItemWidth(750);
            ImGui.InputText("过滤##Fliter", ref fliter, 64);

            var bottomBarHeight = ImGui.GetFrameHeightWithSpacing() + 5.0f;
            var childsize = new Vector2(0, -bottomBarHeight);
            if (ImGui.BeginChild("##PFlist", childsize, true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                foreach (var pf in _pfs)
                {
                    var str = string.Join("|", pf.Title, pf.Description, pf.Tags, pf.Group.AliasOrGID, pf.User.AliasOrUID);
                    if (!string.IsNullOrEmpty(fliter) && !str.Contains(fliter)) continue;
                    DrawPF(pf);
                }
                ImGui.EndChild();
            }

            ImGui.BeginDisabled(Disable);
            if (ImGui.Button("刷新"))
            {
                _lastUpdate = DateTime.Now;
                _pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.Checkbox("自动刷新", ref _autoRefresh);
            if (!Disable && _autoRefresh)
            {
                _lastUpdate = DateTime.Now;
                _pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
            }
            if (Disable)
            {
                ImGui.SameLine();
                var time = _lastUpdate + CoolDown - DateTime.Now;
                ImGui.Text($"冷却中 : {time:mm\\:ss}");
            }

            ImGui.SameLine(380);
            if (ImGui.Button("创建"))
            {
                var alias = _apiController.DisplayName == _apiController.UID ? null : _apiController.DisplayName;
                Mediator.Publish(new OpenPFinderPopupMessage(new PFinderDto(){User = new UserData(_apiController.UID, alias)}));
            }

        }

        private void DrawPF(PFinderDto pf)
        {
            // 使用一个带边框的表格来包裹整个条目。
            if (ImGui.BeginTable("pf_card_" + pf.Guid, 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
            {
                // === 定义列的属性 ===
                ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 30f);

                // === 绘制表格内容 ===
                ImGui.TableNextRow();

                // --- 第一列：内容区 ---
                ImGui.TableSetColumnIndex(0);

                _uiShared.BigText(pf.Title, ImGuiColors.ParsedBlue);

                if (pf.IsNSFW)
                {
                    UiSharedService.ColorText("NSFW", ImGuiColors.DalamudRed);
                    UiSharedService.AttachToolTip("NSFW/R18+");
                    ImGui.SameLine();
                }
                UiSharedService.ColorText(pf.Tags, ImGuiColors.DalamudGrey);

                var goingon = pf.StartTime < DateTime.Now && pf.EndTime > DateTime.Now;
                UiSharedService.ColorTextWrapped($"{pf.StartTime.ToLocalTime():g} - {pf.EndTime.ToLocalTime():g}", goingon ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudWhite);

                // 将组信息和用户信息并排显示
                UiSharedService.TextWrapped(pf.Open ? "公开" : $"{pf.Group.AliasOrGID}");
                ImGui.SameLine(ImGui.GetColumnWidth() - 200); // 使用相对定位，更健壮
                UiSharedService.ColorTextWrapped(pf.User.AliasOrUID, UiSharedService.IsSupporter(pf.User.UID) ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite);

                // --- 修改开始 ---

                // 我们仍然使用 Child 窗口来创建一个固定高度、带滚动条的区域
                ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5.0f);
                if (ImGui.BeginChild("desc_child_" + pf.Guid, new Vector2(0, 105), true))
                {
                    // 1. 创建一个临时的 string 变量，因为 InputTextMultiline 需要一个 `ref string`
                    var descriptionText = pf.Description ?? string.Empty;

                    // 2. (推荐) 移除输入框的背景和边框，让它看起来像普通文本
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0)); // 透明背景
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0)); // 移除内边距

                    // 3. 使用 InputTextMultiline 并设置 ReadOnly 标志
                    //    - 使用唯一的隐藏标签 "##..."
                    //    - 尺寸设置为 new Vector2(-1, -1) 或 GetContentRegionAvail() 以填满 Child 容器
                    //    - 传入 ImGuiInputTextFlags.ReadOnly
                    ImGui.InputTextMultiline("##desc_text" + pf.Guid,
                        ref descriptionText,
                        (uint)descriptionText.Length + 1, // MaxLength，在只读模式下不重要
                        ImGui.GetContentRegionAvail(),
                        ImGuiInputTextFlags.ReadOnly);

                    // 4. 恢复样式
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor();
                }
                ImGui.EndChild();
                ImGui.PopStyleVar();

                // --- 修改结束 ---

                // --- 第二列：操作区 ---
                ImGui.TableSetColumnIndex(1);

                if (pf.User.UID == _apiController.UID)
                {
                    if (ImGui.Button("修改##" + pf.Guid))
                    {
                        Mediator.Publish(new OpenPFinderPopupMessage(pf.DeepClone()));
                    }

                    ImGui.BeginDisabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl));
                    if (ImGui.Button("删除##" + pf.Guid))
                    {
                        var clone = pf.DeepClone();
                        clone.StartTime = DateTimeOffset.MinValue;
                        clone.EndTime = DateTimeOffset.MinValue.AddMinutes(1);
                        var result = _apiController.UpdatePFinder(clone).Result;
                        _pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
                    }
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        UiSharedService.AttachToolTip("按住Ctrl键以删除");
                    }
                }

                // === 结束表格 ===
                ImGui.EndTable();
            }
        }
    }
}