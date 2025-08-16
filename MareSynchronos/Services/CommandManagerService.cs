using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;
using System.Globalization;

namespace MareSynchronos.Services;

public sealed class CommandManagerService : IDisposable
{
    private const string _commandName = "/mare";

    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly MareMediator _mediator;
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public CommandManagerService(ICommandManager commandManager, PerformanceCollectorService performanceCollectorService,
        ServerConfigurationManager serverConfigurationManager, CacheMonitor periodicFileScanner,
        ApiController apiController, MareMediator mediator, MareConfigService mareConfigService, PairManager pairManager)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _serverConfigurationManager = serverConfigurationManager;
        _cacheMonitor = periodicFileScanner;
        _apiController = apiController;
        _mediator = mediator;
        _mareConfigService = mareConfigService;
        _pairManager = pairManager;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开Mare主UI" + Environment.NewLine + Environment.NewLine +
                "其他可用命令:" + Environment.NewLine +
                "\t /mare toggle - 若已连接到Mare服务器, 断开连接, 否则, 连接到当前设置的服务器" + Environment.NewLine +
                "\t /mare toggle on|off - 根据参数连接或断开连接" + Environment.NewLine +
                "\t /mare gpose - 打开Mare角色数据中心界面" + Environment.NewLine +
                "\t /mare analyze - 打开Mare角色数据分析界面" + Environment.NewLine +
                "\t /mare settings - 打开设置界面" + Environment.NewLine +
                "\t /mare chat - 打开聊天窗口" + Environment.NewLine +
                "\t /mare r - 回复上一个同步贝聊天" + Environment.NewLine +
                "\t /mare 同步贝名 - 回复特定同步贝聊天" + Environment.NewLine +
                "\t /mare pf - 打开招募中心"
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(_commandName);
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.Trim().Split(" ", 2, StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_mareConfigService.Current.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        if (!_mareConfigService.Current.HasValidSetup())
            return;

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_apiController.ServerState == WebAPI.SignalR.Utils.ServerState.Disconnecting)
            {
                _mediator.Publish(new NotificationMessage("Mare正在断开连接", "Mare正在断开连接时不能使用 /toggle 命令",
                    NotificationType.Error));
            }

            if (_serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnectionsAsync();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _cacheMonitor.InvokeScan();
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
        else if (string.Equals(splitArgs[0], "chat", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(ChatUi)));
        }
        else if (string.Equals(splitArgs[0], "r", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(ChatUi.LastChatGroup))
            {
                _mediator.Publish(new NotificationMessage("错误", "未检测到上一次聊天的同步贝,请使用 '/mare 贝名称' 进行指定", NotificationType.Error, TimeSpan.FromSeconds(10)));
                return;
            }
            var msg = new GroupChatDto(new UserData(_apiController.UID), new GroupData(ChatUi.LastChatGroup), DateTime.UtcNow, splitArgs[1]);
            _ = _apiController.GroupChatServer(msg);
        }
        else if (string.Equals(splitArgs[0], "pf", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(PFinderWindow)));
        }
        else if (GetGIDByName(splitArgs[0], out var gid))
        {
            ChatUi.LastChatGroup = gid;
            var msg = new GroupChatDto(new UserData(_apiController.UID, _apiController.DisplayName), new GroupData(ChatUi.LastChatGroup), DateTime.UtcNow, splitArgs[1]);
            _ = _apiController.GroupChatServer(msg);
        }
        else
        {
            _mediator.Publish(new NotificationMessage("错误", "输入的Mare命令有误, 请确认.", NotificationType.Error, TimeSpan.FromSeconds(5)));
        }
    }
    private bool GetGIDByName(string name, out string gid)
    {
        if (name is "世界" or "MSS-GLOBAL")
        {
            gid = "MSS-GLOBAL";
            return true;
        }
        var pairedgroup = _pairManager.Groups.Select(x => x.Value).FirstOrDefault(x => x.GroupAliasOrGID == name);
        if (pairedgroup is null)
        {
            gid = string.Empty;
            return false;
        }
        gid = pairedgroup.GroupAliasOrGID;
        return true;
    }
}