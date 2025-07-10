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
        }

        public override void OnOpen()
        {
            if (!_apiController.IsConnected) return;
            if (_lastUpdate + CoolDown < DateTime.Now)
            {
                _pfs = _apiController.RefreshPfinderList(new UserDto(new UserData(_apiController.UID))).Result;
                if (_pfs.Count > 0) _lastUpdate = DateTime.Now;
            }
        }

        protected override void DrawInternal()
        {
            if (!_apiController.IsConnected) return;

            ImGui.SetNextItemWidth(750);
            ImGui.InputText("过滤##Fliter", ref fliter, 64);

            var childsize = ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetItemRectSize().Y + 5);
            if (ImGui.BeginChild("##PFlist", childsize, true))
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
                _pfs = _apiController.RefreshPfinderList(new UserDto(new UserData(_apiController.UID))).Result;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.Checkbox("自动刷新", ref _autoRefresh);
            if (!Disable && _autoRefresh)
            {
                _lastUpdate = DateTime.Now;
                _pfs = _apiController.RefreshPfinderList(new UserDto(new UserData(_apiController.UID))).Result;
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

        public void DrawPF(PFinderDto pf)
        {
            if (ImGui.BeginChild(pf.Guid.ToString() + "##preview", new Vector2(ImGui.GetContentRegionAvail().X,200), true, ImGuiWindowFlags.NoScrollbar))
            {
                _uiShared.BigText(pf.Title, ImGuiColors.ParsedBlue);

                UiSharedService.ColorText( pf.IsNSFW ? "NSFW" : "", ImGuiColors.DalamudRed);
                UiSharedService.AttachToolTip("NSFW/R18+");
                ImGui.SameLine();
                UiSharedService.ColorText(pf.Tags, ImGuiColors.DalamudGrey);

                var goingon = pf.StartTime < DateTime.Now && pf.EndTime > DateTime.Now;
                UiSharedService.ColorTextWrapped($"{pf.StartTime.ToLocalTime():g} - {pf.EndTime.ToLocalTime():g}", goingon ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudWhite);
                ImGui.SameLine(360);
                UiSharedService.TextWrapped(pf.Open ? "公开" : $"{pf.Group.AliasOrGID}");
                ImGui.SameLine(640);
                UiSharedService.ColorTextWrapped(pf.User.AliasOrUID, UiSharedService.IsSupporter(pf.User.UID) ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite);

                ImGui.SetCursorPosY(80);
                if (ImGui.BeginChild(pf.Guid + "##preview" + "###desc", new Vector2(ImGui.GetContentRegionAvail().X, 105), true))
                {
                    UiSharedService.TextWrapped(pf.Description);
                    ImGui.EndChild();
                }

                ImGui.SetCursorPos(new Vector2(725, 10));
                if (pf.User.UID == _apiController.UID)
                {

                    if (ImGui.Button("修改"))
                    {
                        Mediator.Publish(new OpenPFinderPopupMessage(pf.DeepClone()));
                    }
                    ImGui.BeginDisabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl));
                    ImGui.SetCursorPos(new Vector2(725, 35));
                    if (ImGui.Button("删除"))
                    {
                        var clone = pf.DeepClone();
                        clone.StartTime = DateTime.MinValue;
                        clone.EndTime = DateTime.MinValue + TimeSpan.FromSeconds(1);
                        _ = _apiController.UpdatePFinder(clone);
                        _pfs = _apiController.RefreshPfinderList(new UserDto(new UserData(_apiController.UID))).Result;
                    }
                    UiSharedService.AttachToolTip("按住Ctrl键以删除");
                    ImGui.EndDisabled();
                }

                ImGui.EndChild();
            }
        }
    }
}