using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using Microsoft.Extensions.Logging;
using MareSynchronos.WebAPI.SignalR.Utils;
using MareSynchronos.PlayerData.Pairs;
using System.Text.Json;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.PlayerData.Handlers;
using System.Collections.Concurrent;
using MareSynchronos.FileCache;
using System.Net;

namespace MareSynchronos.UI;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly FileUploadManager _fileTransferManager;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly FileCompactor _fileCompactor;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly PairManager _pairManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private bool _deleteAccountPopupModalShown = false;
    private bool _deleteFilesPopupModalShown = false;
    private string _exportDescription = string.Empty;
    private string _lastTab = string.Empty;
    private bool? _notesSuccessfullyApplied = null;
    private bool _overwriteExistingLabels = false;
    private bool _readClearCache = false;
    private bool _readExport = false;
    private bool _wasOpen = false;

    private bool useManualProxy;
    private string proxyProtocol = string.Empty;
    private string proxyHost = string.Empty;
    private int proxyPort;
    private int proxyProtocolIndex;
    private string proxyStatus = "Unknown";
    private readonly string[] proxyProtocols = new string[] { "http", "https", "socks5" };

    public SettingsUi(ILogger<SettingsUi> logger,
        UiSharedService uiShared, MareConfigService configService,
        MareCharaFileManager mareCharaFileManager, PairManager pairManager,
        ServerConfigurationManager serverConfigurationManager,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        FileUploadManager fileTransferManager,
        FileTransferOrchestrator fileTransferOrchestrator,
        FileCompactor fileCompactor) : base(logger, mediator, "Mare Synchronos 设置")
    {
        _configService = configService;
        _mareCharaFileManager = mareCharaFileManager;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;
        _performanceCollector = performanceCollector;
        _fileTransferManager = fileTransferManager;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCompactor = fileCompactor;
        _uiShared = uiShared;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(800, 2000),
        };

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) => LastCreatedCharacterData = msg.CharacterData);
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
    }

    public CharacterData? LastCreatedCharacterData { private get; set; }
    private ApiController ApiController => _uiShared.ApiController;

    public override void Draw()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        base.OnClose();
    }

    private void DrawBlockedTransfers()
    {
        _lastTab = "BlockedTransfers";
        UiSharedService.ColorTextWrapped("您试图上传或下载但创建者禁止传输的文件将显示在此处。 " +
                             "如果您在此处看到驱动器中的文件路径，则不允许上载这些文件。如果你看到哈希值，那么这些文件是不允许下载的。 " +
                             "让与您配对的朋友通过其他方式向你发送有问题的mod、自己获取mod或去纠缠mod创建者允许其通过月海发送。",
            ImGuiColors.DalamudGrey);

        if (ImGui.BeginTable("TransfersTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(
                $"哈希/文件名");
            ImGui.TableSetupColumn($"被禁止");

            ImGui.TableHeadersRow();

            foreach (var item in _fileTransferOrchestrator.ForbiddenTransfers)
            {
                ImGui.TableNextColumn();
                if (item is UploadFileTransfer transfer)
                {
                    ImGui.Text(transfer.LocalFile);
                }
                else
                {
                    ImGui.Text(item.Hash);
                }
                ImGui.TableNextColumn();
                ImGui.Text(item.ForbiddenBy);
            }
            ImGui.EndTable();
        }
    }

    public void LoadProxyConfig()
    {

        this.useManualProxy = _configService.Current.UseManualProxy;
        this.proxyProtocol = _configService.Current.ProxyProtocol;
        this.proxyHost = _configService.Current.ProxyHost;
        this.proxyPort = _configService.Current.ProxyPort;
        this.proxyProtocolIndex = Array.IndexOf(this.proxyProtocols, this.proxyProtocol);
        if (this.proxyProtocolIndex == -1)
            this.proxyProtocolIndex = 0;
    }
    private void DrawCurrentTransfers()
    {
        _lastTab = "Transfers";
        UiSharedService.FontText("代理设置", _uiShared.UidFont);
        LoadProxyConfig();
        ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudRed, "设置 Mare 所使用的网络代理,会影响到文件同步的连接,保存后重启插件生效");
        if (ImGui.Checkbox("手动配置代理", ref this.useManualProxy))
        {
            _configService.Current.UseManualProxy = this.useManualProxy;
            _configService.Save();
        }
        if (this.useManualProxy)
        {
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudGrey, "在更改下方选项时，请确保你知道你在做什么，否则不要随便更改。");
            ImGui.Text("协议");
            ImGui.SameLine();
            if (ImGui.Combo("##proxyProtocol", ref this.proxyProtocolIndex, this.proxyProtocols, this.proxyProtocols.Length))
            {
                this.proxyProtocol = this.proxyProtocols[this.proxyProtocolIndex];
                _configService.Current.ProxyProtocol = this.proxyProtocol;
                _configService.Save();
            }
            ImGui.Text("地址");
            ImGui.SameLine();
            if (ImGui.InputText("##proxyHost", ref this.proxyHost, 100))
            {
                _configService.Current.ProxyHost = this.proxyHost;
                _configService.Save();
            }
            ImGui.Text("端口");
            ImGui.SameLine();
            if (ImGui.InputInt("##proxyPort", ref this.proxyPort))
            {
                _configService.Current.ProxyPort = this.proxyPort;
                _configService.Save();
            }
        }

        if (ImGui.Button("测试GitHub连接"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    this.proxyStatus = "测试中";
                    var handler = new HttpClientHandler();
                    if (this.useManualProxy)
                    {
                        handler.UseProxy = true;
                        handler.Proxy = new WebProxy($"{this.proxyProtocol}://{this.proxyHost}:{this.proxyPort}", true);
                    }
                    else
                    {
                        handler.UseProxy = false;
                    }
                    var httpClient = new HttpClient(handler);
                    httpClient.Timeout = TimeSpan.FromSeconds(3);
                    _ = await httpClient.GetStringAsync("https://raw.githubusercontent.com/ottercorp/dalamud-distrib/main/version");
                    this.proxyStatus = "有效";
                }
                catch (Exception)
                {
                    this.proxyStatus = "无效";
                }
            });
        }

        var proxyStatusColor = ImGuiColors.DalamudWhite;
        switch (this.proxyStatus)
        {
            case "测试中":
                proxyStatusColor = ImGuiColors.DalamudYellow;
                break;
            case "有效":
                proxyStatusColor = ImGuiColors.ParsedGreen;
                break;
            case "无效":
                proxyStatusColor = ImGuiColors.DalamudRed;
                break;
            default: break;
        }

        ImGui.TextColored(proxyStatusColor, $"代理测试结果: {this.proxyStatus}");

        ImGui.Separator();
        UiSharedService.FontText("Transfer Settings", _uiShared.UidFont);

        int maxParallelDownloads = _configService.Current.ParallelDownloads;
        bool useAlternativeUpload = _configService.Current.UseAlternativeFileUpload;
        if (ImGui.SliderInt("最大并行下载量", ref maxParallelDownloads, 1, 10))
        {
            _configService.Current.ParallelDownloads = maxParallelDownloads;
            _configService.Save();
        }

        if (ImGui.Checkbox("使用其他上传方法", ref useAlternativeUpload))
        {
            _configService.Current.UseAlternativeFileUpload = useAlternativeUpload;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("尝试一次性上传文件，而不是流式上传。通常不需要启用。如果您有上传问题，那么请使用这个功能。");

        ImGui.Separator();
        UiSharedService.FontText("传输UI", _uiShared.UidFont);

        bool showTransferWindow = _configService.Current.ShowTransferWindow;
        if (ImGui.Checkbox("显示单独的传输窗口", ref showTransferWindow))
        {
            _configService.Current.ShowTransferWindow = showTransferWindow;
            _configService.Save();
        }
        UiSharedService.DrawHelpText($"下载窗口将显示未完成下载的当前进度。{Environment.NewLine}{Environment.NewLine}" +
            $"W/Q/P/D代表什么？{Environment.NewLine}W = 等待下载（请参阅最大并行下载量）{Environment.NewLine}" +
            $"Q = 在服务器上排队，等待队列就绪信号{Environment.NewLine}" +
            $"P = 正在处理下载（即下载中）{Environment.NewLine}" +
            $"D = 解压缩下载");
        if (!_configService.Current.ShowTransferWindow) ImGui.BeginDisabled();
        ImGui.Indent();
        bool editTransferWindowPosition = _uiShared.EditTrackerPosition;
        if (ImGui.Checkbox("编辑传输窗口位置", ref editTransferWindowPosition))
        {
            _uiShared.EditTrackerPosition = editTransferWindowPosition;
        }
        ImGui.Unindent();
        if (!_configService.Current.ShowTransferWindow) ImGui.EndDisabled();

        bool showTransferBars = _configService.Current.ShowTransferBars;
        if (ImGui.Checkbox("在玩家下方显示的传输条", ref showTransferBars))
        {
            _configService.Current.ShowTransferBars = showTransferBars;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在下载过程中在您下载的玩家脚下呈现进度条。");

        if (!showTransferBars) ImGui.BeginDisabled();
        ImGui.Indent();
        bool transferBarShowText = _configService.Current.TransferBarsShowText;
        if (ImGui.Checkbox("显示下载文本", ref transferBarShowText))
        {
            _configService.Current.TransferBarsShowText = transferBarShowText;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("在传输条中显示下载文本（下载的MiB大小）");
        int transferBarWidth = _configService.Current.TransferBarsWidth;
        if (ImGui.SliderInt("传输条宽度", ref transferBarWidth, 10, 500))
        {
            _configService.Current.TransferBarsWidth = transferBarWidth;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("显示的传输条的宽度（永远不会小于显示的文本的宽度）");
        int transferBarHeight = _configService.Current.TransferBarsHeight;
        if (ImGui.SliderInt("传输条高度", ref transferBarHeight, 2, 50))
        {
            _configService.Current.TransferBarsHeight = transferBarHeight;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("显示的传输条的高度（永远不会低于显示的文本）");
        bool showUploading = _configService.Current.ShowUploading;
        if (ImGui.Checkbox("在当前正在上传的玩家下方显示“上传”文本", ref showUploading))
        {
            _configService.Current.ShowUploading = showUploading;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在正在上传数据的玩家脚下呈现一个“上传”文本。");

        ImGui.Unindent();
        if (!showUploading) ImGui.BeginDisabled();
        ImGui.Indent();
        bool showUploadingBigText = _configService.Current.ShowUploadingBigText;
        if (ImGui.Checkbox("大字体“上传”文本", ref showUploadingBigText))
        {
            _configService.Current.ShowUploadingBigText = showUploadingBigText;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将以更大的字体呈现“上传”文本。");

        ImGui.Unindent();

        if (!showUploading) ImGui.EndDisabled();
        if (!showTransferBars) ImGui.EndDisabled();

        ImGui.Separator();
        UiSharedService.FontText("当前传输", _uiShared.UidFont);

        if (ImGui.BeginTabBar("TransfersTabBar"))
        {
            if (ApiController.ServerState is ServerState.Connected && ImGui.BeginTabItem("传输"))
            {
                ImGui.TextUnformatted("上传");
                if (ImGui.BeginTable("UploadsTable", 3))
                {
                    ImGui.TableSetupColumn("文件");
                    ImGui.TableSetupColumn("已上传");
                    ImGui.TableSetupColumn("大小");
                    ImGui.TableHeadersRow();
                    foreach (var transfer in _fileTransferManager.CurrentUploads.ToArray())
                    {
                        var color = UiSharedService.UploadColor((transfer.Transferred, transfer.Total));
                        ImGui.PushStyleColor(ImGuiCol.Text, color);
                        ImGui.TableNextColumn();
                        ImGui.Text(transfer.Hash);
                        ImGui.TableNextColumn();
                        ImGui.Text(UiSharedService.ByteToString(transfer.Transferred));
                        ImGui.TableNextColumn();
                        ImGui.Text(UiSharedService.ByteToString(transfer.Total));
                        ImGui.PopStyleColor();
                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }
                ImGui.Separator();
                ImGui.TextUnformatted("下载");
                if (ImGui.BeginTable("DownloadsTable", 4))
                {
                    ImGui.TableSetupColumn("用户");
                    ImGui.TableSetupColumn("服务器");
                    ImGui.TableSetupColumn("文件");
                    ImGui.TableSetupColumn("下载");
                    ImGui.TableHeadersRow();

                    foreach (var transfer in _currentDownloads.ToArray())
                    {
                        var userName = transfer.Key.Name;
                        foreach (var entry in transfer.Value)
                        {
                            var color = UiSharedService.UploadColor((entry.Value.TransferredBytes, entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            ImGui.Text(userName);
                            ImGui.TableNextColumn();
                            ImGui.Text(entry.Key);
                            ImGui.PushStyleColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.Text(entry.Value.TransferredFiles + "/" + entry.Value.TotalFiles);
                            ImGui.TableNextColumn();
                            ImGui.Text(UiSharedService.ByteToString(entry.Value.TransferredBytes) + "/" + UiSharedService.ByteToString(entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            ImGui.PopStyleColor();
                            ImGui.TableNextRow();
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("被阻止的传输"))
            {
                DrawBlockedTransfers();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawDebug()
    {
        _lastTab = "Debug";

        UiSharedService.FontText("调试", _uiShared.UidFont);
#if DEBUG
        if (LastCreatedCharacterData != null && ImGui.TreeNode("Last created character data"))
        {
            foreach (var l in JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }).Split('\n'))
            {
                ImGui.Text($"{l}");
            }

            ImGui.TreePop();
        }
#endif
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Copy, "[DEBUG] 将上次创建的角色数据复制到剪贴板"))
        {
            if (LastCreatedCharacterData != null)
            {
                ImGui.SetClipboardText(JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }));
            }
            else
            {
                ImGui.SetClipboardText("ERROR: 没有创建角色数据，无法复制。");
            }
        }
        UiSharedService.AttachToolTip("在报告被服务器拒绝的mod时使用此选项。");

        _uiShared.DrawCombo("日志等级", Enum.GetValues<LogLevel>(), (l) => l.ToString(), (l) =>
        {
            _configService.Current.LogLevel = l;
            _configService.Save();
        }, _configService.Current.LogLevel);

        bool logPerformance = _configService.Current.LogPerformance;
        if (ImGui.Checkbox("日志性能计数器", ref logPerformance))
        {
            _configService.Current.LogPerformance = logPerformance;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("启用此功能可能会对性能产生（轻微）影响。不建议长时间启用此功能。");

        if (!logPerformance) ImGui.BeginDisabled();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.StickyNote, "将性能统计信息打印到 /xllog"))
        {
            _performanceCollector.PrintPerformanceStats();
        }
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.StickyNote, "将性能统计信息（最近60秒）打印到 /xllog"))
        {
            _performanceCollector.PrintPerformanceStats(60);
        }
        if (!logPerformance) ImGui.EndDisabled();
    }

    private void DrawFileStorageSettings()
    {
        _lastTab = "FileCache";

        UiSharedService.FontText("导出MCDF", _uiShared.UidFont);

        UiSharedService.TextWrapped("此功能允许您将角色导出到MCDF文件中，并手动将其发送给其他人。MCDF文件只能在集体动作期间通过月海同步器导入。 " +
            "请注意，他人可以自制一个非官方的导出工具来提取其中包含的数据，存在这种可能。");

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("我已了解，导出我的角色数据并将其发送给其他人会不可避免地泄露我当前的角色外观。与我共享数据的人可以不受限制地与其他人共享我的数据。");

        if (_readExport)
        {
            ImGui.Indent();

            if (!_mareCharaFileManager.CurrentlyWorking)
            {
                ImGui.InputTextWithHint("导出描述器", "此描述将在加载数据时显示", ref _exportDescription, 255);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Save, "导出角色为MCDF文件"))
                {
                    string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                        ? "export.mcdf"
                        : string.Join('_', $"{_exportDescription}.mcdf".Split(Path.GetInvalidFileNameChars()));
                    _uiShared.FileDialogManager.SaveFileDialog("导出角色数据文件", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                    {
                        if (!success) return;

                        _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                        _configService.Save();

                        _ = Task.Run(() =>
                        {
                            try
                            {
                                _mareCharaFileManager.SaveMareCharaFile(LastCreatedCharacterData, _exportDescription, path);
                                _exportDescription = string.Empty;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogCritical(ex, "保存数据时出错");
                            }
                        });
                    }, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
                }
                UiSharedService.ColorTextWrapped("注意：为了获得最佳效果，请确保您拥有想要共享的所有内容以及正确的角色外观，" +
                    " 并在导出之前重新绘制角色。", ImGuiColors.DalamudYellow);
            }
            else
            {
                UiSharedService.ColorTextWrapped("正在导出", ImGuiColors.DalamudYellow);
            }

            ImGui.Unindent();
        }
        bool openInGpose = _configService.Current.OpenGposeImportOnGposeStart;
        if (ImGui.Checkbox("当GPose加载时打开MCDF导入窗口", ref openInGpose))
        {
            _configService.Current.OpenGposeImportOnGposeStart = openInGpose;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在加载到Gpose时自动打开导入菜单。如果未选中，您可以使用“/mare gpose”手动打开菜单");

        ImGui.Separator();

        UiSharedService.FontText("存储", _uiShared.UidFont);

        UiSharedService.TextWrapped("月海将永久存储配对用户所下载的文件。这是为了提高加载性能并减少下载量。" +
            "是否清除文件将通过设置的最大存储大小数值进行自我管理。请酌情地设置存储大小。无需手动清除存储文件。");

        _uiShared.DrawFileScanState();
        _uiShared.DrawTimeSpanBetweenScansSetting();
        _uiShared.DrawCacheDirectorySetting();
        ImGui.Text($"当前使用的本地存储： {UiSharedService.ByteToString(_uiShared.FileCacheSize)}");
        bool isLinux = Util.IsLinux();
        if (isLinux) ImGui.BeginDisabled();
        bool useFileCompactor = _configService.Current.UseCompactor;
        if (ImGui.Checkbox("使用文件系统压缩", ref useFileCompactor))
        {
            _configService.Current.UseCompactor = useFileCompactor;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("文件系统压缩可以大幅减少文件在硬盘上占用的空间。在性能较低的CPU上可能会产生轻微的负荷。" + Environment.NewLine
            + "建议保持启用状态以节省空间。");
        ImGui.SameLine();
        if (!_fileCompactor.MassCompactRunning)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.FileArchive, "压缩存储中的所有文件"))
            {
                _ = Task.Run(() => _fileCompactor.CompactStorage(true));
            }
            UiSharedService.AttachToolTip("这将对您当前本地存储的所有月海同步文件进行压缩。" + Environment.NewLine
                + "如果您保持启用文件系统压缩，则不需要手动运行此操作。");
            ImGui.SameLine();
            if (UiSharedService.IconTextButton(FontAwesomeIcon.File, "解压缩存储中的所有文件"))
            {
                _ = Task.Run(() => _fileCompactor.CompactStorage(false));
            }
            UiSharedService.AttachToolTip("这将对当前本地存储的所有月海同步文件进行解压缩。");
        }
        else
        {
            UiSharedService.ColorText($"文件压缩程序当前正在运行 ({_fileCompactor.Progress})", ImGuiColors.DalamudYellow);
        }
        if (isLinux)
        {
            ImGui.EndDisabled();
            ImGui.Text("文件系统压缩仅在Windows系统上可用。");
        }

        ImGui.Dummy(new Vector2(10, 10));
        ImGui.Text("要清除本地存储，请接受以下免责声明：");
        ImGui.Indent();
        ImGui.Checkbox("##readClearCache", ref _readClearCache);
        ImGui.SameLine();
        UiSharedService.TextWrapped("我已了解： " + Environment.NewLine + "- 通过清除本地存储，我不得不重新下载所有数据，从而使连接服务的文件服务器承受了额外的压力。"
            + Environment.NewLine + "- 这不是试图解决同步问题的步骤。"
            + Environment.NewLine + "- 在文件服务器负载繁重的情况下，这可能会使无法获取其他玩家数据的情况变得更糟。");
        if (!_readClearCache)
            ImGui.BeginDisabled();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "清除本地存储") && UiSharedService.CtrlPressed() && _readClearCache)
        {
            _ = Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(_configService.Current.CacheFolder))
                {
                    File.Delete(file);
                }

                _uiShared.RecalculateFileCacheSize();
            });
        }
        UiSharedService.AttachToolTip("您通常不需要这样做。为了解决同步问题，您也不应该这样做。" + Environment.NewLine
            + "这将仅删除下载的所有玩家同步的数据，并要求您重新下载所有的内容。" + Environment.NewLine
            + "月海的存储是自动清除的，存储空间不会超过您设置的限制。" + Environment.NewLine
            + "如果你仍然认为你需要这样做，按住CTRL键的同时点击这个按钮。");
        if (!_readClearCache)
            ImGui.EndDisabled();
        ImGui.Unindent();
    }

    private void DrawGeneral()
    {
        if (!string.Equals(_lastTab, "General", StringComparison.OrdinalIgnoreCase))
        {
            _notesSuccessfullyApplied = null;
        }

        _lastTab = "General";
        UiSharedService.FontText("备注", _uiShared.UidFont);
        if (UiSharedService.IconTextButton(FontAwesomeIcon.StickyNote, "将所有用户备注导出到剪贴板"))
        {
            ImGui.SetClipboardText(UiSharedService.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (UiSharedService.IconTextButton(FontAwesomeIcon.FileImport, "从剪贴板导入备注"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("覆盖现有备注", ref _overwriteExistingLabels);
        UiSharedService.DrawHelpText("如果选择此选项，则导入的备注将覆盖对应UID的所有现存备注。");
        if (_notesSuccessfullyApplied.HasValue && _notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("成功导入用户备注", ImGuiColors.HealerGreen);
        }
        else if (_notesSuccessfullyApplied.HasValue && !_notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("尝试从剪贴板导入备注失败，检查格式并重试。", ImGuiColors.DalamudRed);
        }

        var openPopupOnAddition = _configService.Current.OpenPopupOnAdd;

        if (ImGui.Checkbox("添加用户时打开备注菜单", ref openPopupOnAddition))
        {
            _configService.Current.OpenPopupOnAdd = openPopupOnAddition;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将打开一个弹窗，方便您在成功添加独立配对用户时为其设置备注。");

        ImGui.Separator();
        UiSharedService.FontText("UI", _uiShared.UidFont);
        var showNameInsteadOfNotes = _configService.Current.ShowCharacterNameInsteadOfNotesForVisible;
        var showVisibleSeparate = _configService.Current.ShowVisibleUsersSeparately;
        var showOfflineSeparate = _configService.Current.ShowOfflineUsersSeparately;
        var showProfiles = _configService.Current.ProfilesShow;
        var showNsfwProfiles = _configService.Current.ProfilesAllowNsfw;
        var profileDelay = _configService.Current.ProfileDelay;
        var profileOnRight = _configService.Current.ProfilePopoutRight;
        var enableRightClickMenu = _configService.Current.EnableRightClickMenus;
        var enableDtrEntry = _configService.Current.EnableDtrEntry;
        var preferNotesInsteadOfName = _configService.Current.PreferNotesOverNamesForVisible;

        if (ImGui.Checkbox("启用游戏右键菜单", ref enableRightClickMenu))
        {
            _configService.Current.EnableRightClickMenus = enableRightClickMenu;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在配对玩家的游戏UI中添加与月海相关的右键菜单项。");

        if (ImGui.Checkbox("在服务器信息栏中显示状态和可见配对角色数", ref enableDtrEntry))
        {
            _configService.Current.EnableDtrEntry = enableDtrEntry;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在服务器信息栏中添加月海连接状态和可见配对角色数。\n您可以通过Dalamud设置对此进行进一步配置。");

        if (ImGui.Checkbox("显示单独的“可见”组", ref showVisibleSeparate))
        {
            _configService.Current.ShowVisibleUsersSeparately = showVisibleSeparate;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在主界面一个特殊“可见”组中显示所有当前可见的用户。");

        if (ImGui.Checkbox("显示单独的离线组", ref showOfflineSeparate))
        {
            _configService.Current.ShowOfflineUsersSeparately = showOfflineSeparate;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("这将在主界面中一个特殊“离线”组中显示所有当前离线的用户。");

        if (ImGui.Checkbox("显示可见玩家的玩家名称", ref showNameInsteadOfNotes))
        {
            _configService.Current.ShowCharacterNameInsteadOfNotesForVisible = showNameInsteadOfNotes;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("当角色可见时，这将显示角色名称，而不是自定义备注");

        ImGui.Indent();
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.BeginDisabled();
        if (ImGui.Checkbox("我更喜欢显示玩家备注而不是玩家名称", ref preferNotesInsteadOfName))
        {
            _configService.Current.PreferNotesOverNamesForVisible = preferNotesInsteadOfName;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("如果你为玩家设置了一个备注，它将显示出来，而不是玩家的名字");
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.EndDisabled();
        ImGui.Unindent();

        if (ImGui.Checkbox("在鼠标悬停时显示月海档案", ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesShow = showProfiles;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("鼠标悬停一段时间后显示该用户自己设置的月海档案");
        ImGui.Indent();
        if (!showProfiles) ImGui.BeginDisabled();
        if (ImGui.Checkbox("在右侧弹出个人档案", ref profileOnRight))
        {
            _configService.Current.ProfilePopoutRight = profileOnRight;
            _configService.Save();
            Mediator.Publish(new CompactUiChange(Vector2.Zero, Vector2.Zero));
        }
        UiSharedService.DrawHelpText("将在主界面的右侧显示档案");
        if (ImGui.Checkbox("显示标记为NSFW的档案", ref showNsfwProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesAllowNsfw = showNsfwProfiles;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("将显示启用NSFW标记的档案文件");
        if (ImGui.SliderFloat("悬停延迟", ref profileDelay, 1, 10))
        {
            _configService.Current.ProfileDelay = profileDelay;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("鼠标悬停多久才显示档案（秒）");
        if (!showProfiles) ImGui.EndDisabled();
        ImGui.Unindent();

        ImGui.Separator();

        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;
        UiSharedService.FontText("通知", _uiShared.UidFont);

        _uiShared.DrawCombo("显示 [信息]##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        UiSharedService.DrawHelpText("显示“信息”通知的位置"
                      + Environment.NewLine + "'Nowhere' 不会显示任何信息通知"
                      + Environment.NewLine + "'Chat' 将在聊天频道中打印信息通知"
                      + Environment.NewLine + "'Toast' 将在右下角显示提示框"
                      + Environment.NewLine + "'Both' 将在聊天频道以及提示框中同时显示");

        _uiShared.DrawCombo("显示 [警告]##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.WarningNotification = i;
            _configService.Save();
        }, _configService.Current.WarningNotification);
        UiSharedService.DrawHelpText("显示“警告”通知的位置。"
                              + Environment.NewLine + "'Nowhere' 不会显示任何警告通知"
                              + Environment.NewLine + "'Chat' 将在聊天中打印警告通知"
                              + Environment.NewLine + "'Toast' 将在右下角显示提示框"
                              + Environment.NewLine + "'Both' 将在聊天频道以及提示框中同时显示");

        _uiShared.DrawCombo("显示 [错误]##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.ErrorNotification = i;
            _configService.Save();
        }, _configService.Current.ErrorNotification);
        UiSharedService.DrawHelpText("显示“错误”通知的位置。"
                              + Environment.NewLine + "'Nowhere' 不会显示任何错误"
                              + Environment.NewLine + "'Chat' 将在聊天中打印警告错误通知"
                              + Environment.NewLine + "'Toast' 将在右下角显示提示框"
                              + Environment.NewLine + "'Both' 将在聊天频道以及提示框中同时显示");

        if (ImGui.Checkbox("禁用可选插件警告", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("启用此选项将不会显示任何丢失可选插件的“警告”消息。");
        if (ImGui.Checkbox("启用上线通知", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("启用此选项将在配对用户上线时在右下角显示一个小通知（类型：信息）。");

        if (!onlineNotifs) ImGui.BeginDisabled();
        if (ImGui.Checkbox("仅针对独立配对通知", ref onlineNotifsPairsOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("启用此选项将仅显示独立配对用户的上线通知（类型：信息）。");
        if (ImGui.Checkbox("仅针对单独备注通知", ref onlineNotifsNamedOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("启用此选项将仅显示您设置了单独备注配对用户的上线通知（类型：信息）。");
        if (!onlineNotifs) ImGui.EndDisabled();
    }

    private void DrawServerConfiguration()
    {
        _lastTab = "服务设置";
        if (ApiController.ServerAlive)
        {
            UiSharedService.FontText("服务操作", _uiShared.UidFont);

            if (ImGui.Button("删除我的所有文件"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("是否删除所有文件？");
            }

            UiSharedService.DrawHelpText("完全删除您上传到该服务上的所有文件。");

            if (ImGui.BeginPopupModal("是否删除所有文件？", ref _deleteFilesPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "您上传到该服务上的所有文件都将被删除。\n此操作无法撤消。");
                ImGui.Text("确定要继续吗？");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                 ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("删除所有内容", new Vector2(buttonSize, 0)))
                {
                    Task.Run(_fileTransferManager.DeleteAllFiles);
                    _deleteFilesPopupModalShown = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("取消##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteFilesPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("删除帐户"))
            {
                _deleteAccountPopupModalShown = true;
                ImGui.OpenPopup("删除您的帐户？");
            }

            UiSharedService.DrawHelpText("完全删除您的帐户和所有上传到该服务的文件。");

            if (ImGui.BeginPopupModal("删除您的帐户？", ref _deleteAccountPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "您的帐户以及服务上的所有相关文件和数据都将被删除。");
                UiSharedService.TextWrapped("您的UID将从所有配对列表中删除。");
                ImGui.Text("确定要继续吗？");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                  ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("删除帐户", new Vector2(buttonSize, 0)))
                {
                    Task.Run(ApiController.UserDelete);
                    _deleteAccountPopupModalShown = false;
                    Mediator.Publish(new SwitchToIntroUiMessage());
                }

                ImGui.SameLine();

                if (ImGui.Button("取消##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.Separator();
        }

        UiSharedService.FontText("服务和角色设置", _uiShared.UidFont);

        var idx = _uiShared.DrawServiceSelection();

        ImGui.Dummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiSharedService.ColorTextWrapped("要将任何修改应用到当前服务，您需要重新连接到该服务。", ImGuiColors.DalamudYellow);
        }

        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("角色管理"))
            {
                if (selectedServer.SecretKeys.Any())
                {
                    UiSharedService.ColorTextWrapped("此处列出的角色将使用下面提供的设置自动连接到选定的月海服务。" +
                        " 请确保输入正确的角色名称或使用底部的“添加当前角色”按钮。", ImGuiColors.DalamudYellow);
                    int i = 0;
                    foreach (var item in selectedServer.Authentications.ToList())
                    {
                        UiSharedService.DrawWithID("selectedChara" + i, () =>
                        {
                            var worldIdx = (ushort)item.WorldId;
                            var data = _uiShared.WorldData.OrderBy(u => u.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
                            if (!data.TryGetValue(worldIdx, out string? worldPreview))
                            {
                                worldPreview = data.First().Value;
                            }

                            var secretKeyIdx = item.SecretKeyIdx;
                            var keys = selectedServer.SecretKeys;
                            if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
                            {
                                secretKey = new();
                            }
                            var friendlyName = secretKey.FriendlyName;

                            if (ImGui.TreeNode($"chara", $"角色：{item.CharacterName}, 世界：{worldPreview}，密钥：{friendlyName}"))
                            {
                                var charaName = item.CharacterName;
                                if (ImGui.InputText("角色名称", ref charaName, 64))
                                {
                                    item.CharacterName = charaName;
                                    _serverConfigurationManager.Save();
                                }

                                _uiShared.DrawCombo("世界##" + item.CharacterName + i, data, (w) => w.Value,
                                    (w) =>
                                    {
                                        if (item.WorldId != w.Key)
                                        {
                                            item.WorldId = w.Key;
                                            _serverConfigurationManager.Save();
                                        }
                                    }, EqualityComparer<KeyValuePair<ushort, string>>.Default.Equals(data.FirstOrDefault(f => f.Key == worldIdx), default) ? data.First() : data.First(f => f.Key == worldIdx));

                                _uiShared.DrawCombo("密钥##" + item.CharacterName + i, keys, (w) => w.Value.FriendlyName,
                                    (w) =>
                                    {
                                        if (w.Key != item.SecretKeyIdx)
                                        {
                                            item.SecretKeyIdx = w.Key;
                                            _serverConfigurationManager.Save();
                                        }
                                    }, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault(f => f.Key == item.SecretKeyIdx), default) ? keys.First() : keys.First(f => f.Key == item.SecretKeyIdx));

                                if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除角色") && UiSharedService.CtrlPressed())
                                    _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                                UiSharedService.AttachToolTip("按住CTRL键可删除此条目。");

                                ImGui.TreePop();
                            }
                        });

                        i++;
                    }

                    ImGui.Separator();
                    if (!selectedServer.Authentications.Any(c => string.Equals(c.CharacterName, _uiShared.PlayerName, StringComparison.Ordinal)
                        && c.WorldId == _uiShared.WorldId))
                    {
                        if (UiSharedService.IconTextButton(FontAwesomeIcon.User, "添加当前角色"))
                        {
                            _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                        }
                        ImGui.SameLine();
                    }

                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Plus, "添加新角色"))
                    {
                        _serverConfigurationManager.AddEmptyCharacterToServer(idx);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("先添加密钥，再添加角色。", ImGuiColors.DalamudYellow);
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("密钥管理"))
            {
                foreach (var item in selectedServer.SecretKeys.ToList())
                {
                    UiSharedService.DrawWithID("key" + item.Key, () =>
                    {
                        var friendlyName = item.Value.FriendlyName;
                        if (ImGui.InputText("密钥显示名称", ref friendlyName, 255))
                        {
                            item.Value.FriendlyName = friendlyName;
                            _serverConfigurationManager.Save();
                        }
                        var key = item.Value.Key;
                        if (ImGui.InputText("Secret Key", ref key, 64))
                        {
                            item.Value.Key = key;
                            _serverConfigurationManager.Save();
                        }
                        if (!selectedServer.Authentications.Any(p => p.SecretKeyIdx == item.Key))
                        {
                            if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除密钥") && UiSharedService.CtrlPressed())
                            {
                                selectedServer.SecretKeys.Remove(item.Key);
                                _serverConfigurationManager.Save();
                            }
                            UiSharedService.AttachToolTip("按住CTRL键可删除此密钥项");
                        }
                        else
                        {
                            UiSharedService.ColorTextWrapped("此密钥正在使用，无法删除", ImGuiColors.DalamudYellow);
                        }
                    });

                    if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
                        ImGui.Separator();
                }

                ImGui.Separator();
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Plus, "添加新密钥"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                    {
                        FriendlyName = "新密钥",
                    });
                    _serverConfigurationManager.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("服务设置"))
            {
                var serverName = selectedServer.ServerName;
                var serverUri = selectedServer.ServerUri;
                var isMain = string.Equals(serverName, ApiController.MainServer, StringComparison.OrdinalIgnoreCase);
                var flags = isMain ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;

                if (ImGui.InputText("服务URI", ref serverUri, 255, flags))
                {
                    selectedServer.ServerUri = serverUri;
                }
                if (isMain)
                {
                    UiSharedService.DrawHelpText("无法编辑主服务的URI。");
                }

                if (ImGui.InputText("服务名称", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    UiSharedService.DrawHelpText("无法编辑主服务的名称。");
                }

                if (!isMain && selectedServer != _serverConfigurationManager.CurrentServer)
                {
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除服务") && UiSharedService.CtrlPressed())
                    {
                        _serverConfigurationManager.DeleteServer(selectedServer);
                    }
                    UiSharedService.DrawHelpText("按住CTRL键可删除此服务");
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawSettingsContent()
    {
        _uiShared.PrintServerState();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("社区和支持（国服的）：");
        ImGui.SameLine();
        if (ImGui.Button("月海同步器/Mare Synchronos Discord"))
        {
            Util.OpenLink("https://discord.gg/3dwsdrShST");
        }
        ImGui.Separator();
        if (ImGui.BeginTabBar("mainTabBar"))
        {
            if (ImGui.BeginTabItem("常规设置"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("导出&存储"))
            {
                DrawFileStorageSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("传输设置"))
            {
                DrawCurrentTransfers();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("服务设置"))
            {
                DrawServerConfiguration();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("调试"))
            {
                DrawDebug();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
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
