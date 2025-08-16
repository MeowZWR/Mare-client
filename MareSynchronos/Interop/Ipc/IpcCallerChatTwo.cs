using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Interop.Ipc;

public class IpcCallerChatTwo : IMediatorSubscriber
{
    private readonly ILogger<IpcCallerChatTwo> _logger;
    private readonly IDalamudPluginInterface _pi;

    private ICallGateSubscriber<int, string, string, DateTime, object?>? _marePush;
    private ICallGateSubscriber<(int major, int minor)>? _chatTwoApiVersion;
    private ICallGateSubscriber<object?>? _mareChannelsUpdated;
    private System.Timers.Timer? _notifyTimer;
    
    private ICallGateProvider<Dictionary<int, string>>? _mareChatChannelInfos;
    private ICallGateProvider<int, string, object?>? _mareChatSendMessage;

    // Cached dependencies
    private PairManager? _pairManager;
    private ApiController? _apiController;

    public IpcCallerChatTwo(ILogger<IpcCallerChatTwo> logger, IDalamudPluginInterface pi)
    {
        _logger = logger;
        _pi = pi;
    }

    public bool APIAvailable { get; private set; }
    public MareMediator Mediator => _pairManager?.Mediator ?? throw new InvalidOperationException("ChatTwo not initialized");

    public void CheckAPI()
    {
        try
        {
            _marePush = _pi.GetIpcSubscriber<int, string, string, DateTime, object?>("ChatTwo.Mare.Push");

            _chatTwoApiVersion ??= _pi.GetIpcSubscriber<(int, int)>("ChatTwo.ApiVersion");
            var version = _chatTwoApiVersion.InvokeFunc();
            APIAvailable = version.Item1 == 1 && version.Item2 >= 0;
            if (APIAvailable)
            {
                _mareChannelsUpdated = _pi.GetIpcSubscriber<object?>("ChatTwo.Mare.ChannelInfosUpdated");
            }
        }
        catch
        {
            APIAvailable = false;
            _marePush = null;
            _mareChannelsUpdated = null;
        }
    }

    public void PushMareMessage(int index, string sender, string content, DateTime timeUtc)
    {
        if (!APIAvailable || _marePush == null) return;
        try
        {
            _marePush.InvokeAction(index, sender, content, timeUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push Mare message to ChatTwo");
        }
    }

    public void Initialize(MareConfigService mareConfigService, PairManager pairManager, ApiController apiController)
    {
        _pairManager ??= pairManager;
        _apiController ??= apiController;
    }

    public void RegisterProviders()
    {
        try
        {
            if (_pairManager == null || _apiController == null)
            {
                throw new InvalidOperationException("ChatTwo not initialized");
            }
            CheckAPI();
            _mareChatChannelInfos = _pi.GetIpcProvider<Dictionary<int, string>>("MareChat.ChannelInfos");
            _mareChatChannelInfos.RegisterFunc(GetMareChatChannelInfos);

            _mareChatSendMessage = _pi.GetIpcProvider<int, string, object?>("MareChat.SendMessage");
            _mareChatSendMessage.RegisterAction(HandleMareChatSendMessage);

            _logger.LogInformation("ChatTwo IPC providers registered successfully");
            Mediator.Subscribe<JoinedGroupsChangedMessage>(this, _ => NotifyChatTwoChannelInfosUpdated());
            Mediator.Subscribe<RefreshUiMessage>(this, _ => ScheduleNotify());
            Mediator.Subscribe<ConnectedMessage>(this, _ => ScheduleNotify());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register ChatTwo IPC providers");
        }
    }

    public void UnregisterProviders()
    {
        try
        {
            NotifyChatTwoChannelInfosUpdated();

            _mareChatChannelInfos?.UnregisterFunc();
            _mareChatSendMessage?.UnregisterAction();
            _mareChannelsUpdated = null;
            _notifyTimer?.Stop();
            _notifyTimer?.Dispose();
            _notifyTimer = null;
            if (_pairManager != null)
            {
                try { Mediator.UnsubscribeAll(this); } catch { /* mediator may be disposed */ }
            }
            _logger.LogDebug("ChatTwo IPC providers unregistered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister ChatTwo IPC providers");
        }
    }

    private void NotifyChatTwoChannelInfosUpdated()
    {
        if (!APIAvailable || _mareChannelsUpdated == null) return;
        try
        {
            _mareChannelsUpdated.InvokeAction();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to notify ChatTwo about Mare channel infos update");
        }
    }

    private void ScheduleNotify()
    {
        if (!APIAvailable || _mareChannelsUpdated == null) return;
        _notifyTimer ??= new System.Timers.Timer(800) { AutoReset = false };
        _notifyTimer.Stop();
        _notifyTimer.Elapsed -= OnNotifyTimerElapsed;
        _notifyTimer.Elapsed += OnNotifyTimerElapsed;
        _notifyTimer.Start();
    }

    private void OnNotifyTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            NotifyChatTwoChannelInfosUpdated();
        }
        catch { /* ignore timer callback errors */ }
    }

    private Dictionary<int, string> GetMareChatChannelInfos()
    {
        try
        {
            var result = new Dictionary<int, string>();
            var joined = MareSynchronos.UI.ChatUi.JoinedGroups;
            if (joined == null || joined.Count == 0) return result;
            for (int i = 0; i < Math.Min(8, joined.Count); i++)
            {
                var gid = joined[i];
                var friendly = gid;
                try
                {
                    var group = _pairManager!.Groups.Keys.FirstOrDefault(g => string.Equals(g.GID, gid, StringComparison.Ordinal));
                    if (group != null) friendly = _pairManager.Groups[group].GroupAliasOrGID;
                    if (string.Equals(gid, "MSS-GLOBAL", StringComparison.OrdinalIgnoreCase)) friendly = "世界";
                }
                catch { /* ignore resolve errors */ }

                result[i] = friendly;
            }
            return result;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error building MareChat.ChannelInfos");
            return new Dictionary<int, string>();
        }
    }

    private void HandleMareChatSendMessage(int index, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var joined = MareSynchronos.UI.ChatUi.JoinedGroups;
            if (joined == null || index < 0 || index >= joined.Count) return;
            var gid = joined[index];
            if (string.IsNullOrEmpty(gid)) return;

            var dto = new GroupChatDto(new UserData(_apiController!.UID, _apiController!.DisplayName), new GroupData(gid), DateTime.UtcNow, message);
            _ = _apiController.GroupChatServer(dto);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling MareChat.SendMessage for index {index}", index);
        }
    }

    public void PushGroupChatMessage(string gid, string sender, string message, DateTime time, MareConfigService mareConfigService)
    {
        _ = mareConfigService;
        if (!APIAvailable || _marePush == null) return;
        
        try
        {
            var joined = MareSynchronos.UI.ChatUi.JoinedGroups;
            if (joined == null) return;
            
            var idx = joined.FindIndex(g => string.Equals(g, gid, StringComparison.Ordinal));
            if (idx < 0) return;
            if (idx > 7) return;

            _marePush.InvokeAction(idx, sender, message, time);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push group chat message to ChatTwo");
        }
    }
}