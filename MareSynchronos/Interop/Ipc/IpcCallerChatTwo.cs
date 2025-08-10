using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Interop.Ipc;

public class IpcCallerChatTwo
{
    private readonly ILogger<IpcCallerChatTwo> _logger;
    private readonly IDalamudPluginInterface _pi;

    private ICallGateSubscriber<int, string, string, DateTime, object?>? _marePush;
    private ICallGateSubscriber<(int major, int minor)>? _chatTwoApiVersion;
    
    // ChatTwo <-> Mare chat IPC providers
    private ICallGateProvider<Dictionary<int, string>>? _mareChatChannelInfos;
    private ICallGateProvider<int, string, object?>? _mareChatSendMessage;

    public IpcCallerChatTwo(ILogger<IpcCallerChatTwo> logger, IDalamudPluginInterface pi)
    {
        _logger = logger;
        _pi = pi;
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        try
        {
            _marePush = _pi.GetIpcSubscriber<int, string, string, DateTime, object?>("ChatTwo.Mare.Push");

            _chatTwoApiVersion ??= _pi.GetIpcSubscriber<(int, int)>("ChatTwo.ApiVersion");
            var version = _chatTwoApiVersion.InvokeFunc();
            APIAvailable = version.Item1 == 1 && version.Item2 >= 0;
        }
        catch
        {
            APIAvailable = false;
            _marePush = null;
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

    /// <summary>
    /// 注册ChatTwo相关的IPC提供者端点
    /// </summary>
    public void RegisterProviders(MareConfigService mareConfigService, PairManager pairManager, ApiController apiController)
    {
        try
        {
            // Provide ChatTwo IPC endpoints for Mare syncshell chat integration
            _mareChatChannelInfos = _pi.GetIpcProvider<Dictionary<int, string>>("MareChat.ChannelInfos");
            _mareChatChannelInfos.RegisterFunc(() => GetMareChatChannelInfos(mareConfigService, pairManager));
            
            _mareChatSendMessage = _pi.GetIpcProvider<int, string, object?>("MareChat.SendMessage");
            _mareChatSendMessage.RegisterAction((index, message) => HandleMareChatSendMessage(index, message, mareConfigService, apiController));
            
            _logger.LogInformation("ChatTwo IPC providers registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register ChatTwo IPC providers");
        }
    }

    /// <summary>
    /// 注销ChatTwo相关的IPC提供者端点
    /// </summary>
    public void UnregisterProviders()
    {
        try
        {
            _mareChatChannelInfos?.UnregisterFunc();
            _mareChatSendMessage?.UnregisterAction();
            _logger.LogDebug("ChatTwo IPC providers unregistered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister ChatTwo IPC providers");
        }
    }

    /// <summary>
    /// 获取Mare聊天频道信息，映射到ChatTwo的频道索引
    /// </summary>
    private Dictionary<int, string> GetMareChatChannelInfos(MareConfigService mareConfigService, PairManager pairManager)
    {
        try
        {
            var result = new Dictionary<int, string>();
            var auto = mareConfigService.Current.AutoJoinChats;
            if (auto == null || auto.Count == 0) return result;

            // Map first 8 AutoJoinChats entries to indices 0..7
            for (int i = 0; i < Math.Min(8, auto.Count); i++)
            {
                var gid = auto[i];
                // Try resolve a friendly name
                var friendly = gid;
                try
                {
                    // Look up group alias if available
                    var group = pairManager.Groups.Keys.FirstOrDefault(g => string.Equals(g.GID, gid, StringComparison.Ordinal));
                    if (group != null)
                    {
                        friendly = pairManager.Groups[group].GroupAliasOrGID;
                    }
                }
                catch { /* ignore */ }

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

    /// <summary>
    /// 处理从ChatTwo发送到Mare的聊天消息
    /// </summary>
    private void HandleMareChatSendMessage(int index, string message, MareConfigService mareConfigService, ApiController apiController)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var auto = mareConfigService.Current.AutoJoinChats;
            if (auto == null || index < 0 || index >= auto.Count) return;
            var gid = auto[index];
            if (string.IsNullOrEmpty(gid)) return;

            var dto = new GroupChatDto(new API.Data.UserData(apiController.UID), new API.Data.GroupData(gid), DateTime.UtcNow, message);
            _ = apiController.GroupChatServer(dto);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling MareChat.SendMessage for index {index}", index);
        }
    }

    /// <summary>
    /// 推送群聊消息到ChatTwo
    /// </summary>
    public void PushGroupChatMessage(string gid, string sender, string message, DateTime time, MareConfigService mareConfigService)
    {
        if (!APIAvailable || _marePush == null) return;
        
        try
        {
            var auto = mareConfigService.Current.AutoJoinChats;
            if (auto == null) return;
            
            var idx = Math.Max(0, auto.FindIndex(g => string.Equals(g, gid, StringComparison.Ordinal)));
            if (idx > 7) idx = 7;

            _marePush.InvokeAction(idx, sender, message, time);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push group chat message to ChatTwo");
        }
    }
}



