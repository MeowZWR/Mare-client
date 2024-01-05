using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class GposeUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareCharaFileManager _mareCharaFileManager;

    public GposeUi(ILogger<GposeUi> logger, MareCharaFileManager mareCharaFileManager,
        DalamudUtilService dalamudUtil, FileDialogManager fileDialogManager, MareConfigService configService,
        MareMediator mediator) : base(logger, mediator, "月海同步器集体动作导入窗口###MareSynchronosGposeUI")
    {
        _mareCharaFileManager = mareCharaFileManager;
        _dalamudUtil = dalamudUtil;
        _fileDialogManager = fileDialogManager;
        _configService = configService;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => StartGpose());
        Mediator.Subscribe<GposeEndMessage>(this, (_) => EndGpose());
        IsOpen = _dalamudUtil.IsInGpose;
        this.SizeConstraints = new()
        {
            MinimumSize = new(200, 200),
            MaximumSize = new(400, 400)
        };
    }

    public override void Draw()
    {
        if (!_dalamudUtil.IsInGpose) IsOpen = false;

        if (!_mareCharaFileManager.CurrentlyWorking)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "加载MCDF"))
            {
                _fileDialogManager.OpenFileDialog("选择MCDF文件", ".mcdf", (success, paths) =>
                {
                    if (!success) return;
                    if (paths.FirstOrDefault() is not string path) return;

                    _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                    _configService.Save();

                    Task.Run(() => _mareCharaFileManager.LoadMareCharaFile(path));
                }, 1, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
            }
            UiSharedService.AttachToolTip("将其应用于当前选定的集体动作角色");
            if (_mareCharaFileManager.LoadedCharaFile != null)
            {
                UiSharedService.TextWrapped("已加载文件：" + _mareCharaFileManager.LoadedCharaFile.FilePath);
                UiSharedService.TextWrapped("文件描述：" + _mareCharaFileManager.LoadedCharaFile.CharaFileData.Description);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Check, "应用加载的MCDF"))
                {
                    Task.Run(async () => await _mareCharaFileManager.ApplyMareCharaFile(_dalamudUtil.GposeTargetGameObject).ConfigureAwait(false));
                }
                UiSharedService.AttachToolTip("将其应用于当前选定的集体动作角色");
                UiSharedService.ColorTextWrapped("警告：重新绘制或更改角色将恢复所有应用的mod。", ImGuiColors.DalamudYellow);
            }
        }
        else
        {
            UiSharedService.ColorTextWrapped("正在加载角色...", ImGuiColors.DalamudYellow);
        }
        UiSharedService.TextWrapped("提示：您可以在插件设置中禁用此窗口在进入集体动作时自动打开，使用命令“/Mare gpose”可以手动打开此窗口。");
    }

    private void EndGpose()
    {
        IsOpen = false;
        _mareCharaFileManager.ClearMareCharaFile();
    }

    private void StartGpose()
    {
        IsOpen = _configService.Current.OpenGposeImportOnGposeStart;
    }
}