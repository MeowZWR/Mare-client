﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using ImGuiNET;
using ImGuiScene;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiBuilder _uiBuilder;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private TextureWrap? _pfpTextureWrap;
    private string _profileDescription = string.Empty;
    private byte[] _profileImage = Array.Empty<byte>();
    private bool _showFileDialogError = false;
    private bool _wasOpen;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiBuilder uiBuilder, UiSharedService uiSharedService,
        FileDialogManager fileDialogManager, MareProfileManager mareProfileManager) : base(logger, mediator, "月海同步器档案编辑器###MareSynchronosEditProfileUI")
    {
        IsOpen = false;
        this.SizeConstraints = new()
        {
            MinimumSize = new(768, 512),
            MaximumSize = new(768, 2000)
        };
        _apiController = apiController;
        _uiBuilder = uiBuilder;
        _uiSharedService = uiSharedService;
        _fileDialogManager = fileDialogManager;
        _mareProfileManager = mareProfileManager;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData == null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
            }
        });
    }

    public override void Draw()
    {
        _uiSharedService.BigText("当前档案（保存在服务器上）");

        var profile = _mareProfileManager.GetMareProfile(new UserData(_apiController.UID));

        if (profile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(profile.Description, ImGuiColors.DalamudRed);
            return;
        }

        if (!_profileImage.SequenceEqual(profile.ImageData.Value))
        {
            _profileImage = profile.ImageData.Value;
            _pfpTextureWrap?.Dispose();
            _pfpTextureWrap = _uiBuilder.LoadImage(_profileImage);
        }

        if (!string.Equals(_profileDescription, profile.Description, StringComparison.OrdinalIgnoreCase))
        {
            _profileDescription = profile.Description;
            _descriptionText = _profileDescription;
        }

        if (_pfpTextureWrap != null)
        {
            ImGui.Image(_pfpTextureWrap.ImGuiHandle, ImGuiHelpers.ScaledVector2(_pfpTextureWrap.Width, _pfpTextureWrap.Height));
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.ChnAxis120)).ImFont);
        var descriptionTextSize = ImGui.CalcTextSize(profile.Description, 256f);
        var childFrame = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 256);
        if (descriptionTextSize.Y > childFrame.Y)
        {
            _adjustedForScollBarsOnlineProfile = true;
        }
        else
        {
            _adjustedForScollBarsOnlineProfile = false;
        }
        childFrame = childFrame with
        {
            X = childFrame.X + (_adjustedForScollBarsOnlineProfile ? ImGui.GetStyle().ScrollbarSize : 0),
        };
        if (ImGui.BeginChildFrame(101, childFrame))
        {
            UiSharedService.TextWrapped(profile.Description);
        }
        ImGui.EndChildFrame();
        ImGui.PopFont();

        var nsfw = profile.IsNSFW;
        ImGui.BeginDisabled();
        ImGui.Checkbox("是NSFW", ref nsfw);
        ImGui.EndDisabled();

        ImGui.Separator();
        _uiSharedService.BigText("月海档案的备注和规则");

        ImGui.TextWrapped($"- 所有与您配对且未暂停的用户都将能够看到您的月海档案图片和描述。{Environment.NewLine}" +
            $"- 其他用户可以举报您的月海档案违反规则。{Environment.NewLine}" +
            $"- !!!禁止：任何可被视为高度非法或淫秽的月海档案图片（兽交、任何可被视为与未成年人（包括拉拉菲尔族）发生性行为的东西等）。{Environment.NewLine}" +
            $"- !!!禁止：描述中任何可能被视为高度冒犯性的侮辱词汇。{Environment.NewLine}" +
            $"- 如果其他用户提供的举报有效，这可能会导致您的月海档案被永久禁用或您的月海帐户被无限期终止。{Environment.NewLine}" +
            $"- 插件的管理团队作出的关于您的月海档案是否合规的结论是不可争议的，并且永久禁用您的月海档案/帐户的决定也是不可争议的。{Environment.NewLine}" +
            $"- 如果您的月海档案图片或月海档案描述不适合在公共场合查看，请启用下面的NSFW开关。");
        ImGui.Separator();
        _uiSharedService.BigText("档案设置");

        if (UiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "上传新的月海档案图片"))
        {
            _fileDialogManager.OpenFileDialog("选择新的月海档案图片", ".png", (success, file) =>
            {
                if (!success) return;
                Task.Run(async () =>
                {
                    var fileContent = File.ReadAllBytes(file);
                    using MemoryStream ms = new(fileContent);
                    var format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
                    if (!format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
                    {
                        _showFileDialogError = true;
                        return;
                    }
                    using var image = Image.Load<Rgba32>(fileContent);

                    if (image.Width > 256 || image.Height > 256 || (fileContent.Length > 250 * 1024))
                    {
                        _showFileDialogError = true;
                        return;
                    }

                    _showFileDialogError = false;
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, Convert.ToBase64String(fileContent), null))
                        .ConfigureAwait(false);
                });
            });
        }
        UiSharedService.AttachToolTip("选择并上传新的月海档案图片");
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "清除上传的月海档案图片"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, "", null));
        }
        UiSharedService.AttachToolTip("清除您当前上传的月海档案图片");
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped("月海档案图片必须是PNG文件，最大高度和宽度为256px，大小不超过250KiB", ImGuiColors.DalamudRed);
        }
        var isNsfw = profile.IsNSFW;
        if (ImGui.Checkbox("档案是NSFW（不适合在公共场合查看）", ref isNsfw))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, isNsfw, null, null));
        }
        UiSharedService.DrawHelpText("如果您的月海档案描述或图片不适合在公共场合查看，请勾选");
        var widthTextBox = 400;
        var posX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted($"描述 {_descriptionText.Length}/1500");
        ImGui.SetCursorPosX(posX);
        ImGuiHelpers.ScaledRelativeSameLine(widthTextBox, ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted("预览（大致）");
        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.ChnAxis120)).ImFont);
        ImGui.InputTextMultiline("##description", ref _descriptionText, 1500, ImGuiHelpers.ScaledVector2(widthTextBox, 200));
        ImGui.PopFont();

        ImGui.SameLine();

        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.ChnAxis120)).ImFont);
        var descriptionTextSizeLocal = ImGui.CalcTextSize(_descriptionText, 256f);
        var childFrameLocal = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 200);
        if (descriptionTextSizeLocal.Y > childFrameLocal.Y)
        {
            _adjustedForScollBarsLocalProfile = true;
        }
        else
        {
            _adjustedForScollBarsLocalProfile = false;
        }
        childFrameLocal = childFrameLocal with
        {
            X = childFrameLocal.X + (_adjustedForScollBarsLocalProfile ? ImGui.GetStyle().ScrollbarSize : 0),
        };
        if (ImGui.BeginChildFrame(102, childFrameLocal))
        {
            UiSharedService.TextWrapped(_descriptionText);
        }
        ImGui.EndChildFrame();
        ImGui.PopFont();

        if (UiSharedService.IconTextButton(FontAwesomeIcon.Save, "保存描述"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, null, _descriptionText));
        }
        UiSharedService.AttachToolTip("设置档案描述文本");
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "清除描述"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, null, ""));
        }
        UiSharedService.AttachToolTip("清除档案件描述文本");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}