using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin;
using Dalamud.Utility;
using ImGuiNET;
using ImGuiScene;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class UiSharedService : DisposableMediatorSubscriberBase
{
    public static readonly ImGuiWindowFlags PopupWindowFlags = ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoScrollbar |
                                           ImGuiWindowFlags.NoScrollWithMouse;

    public readonly FileDialogManager FileDialogManager;

    private const string _notesEnd = "##MARE_SYNCHRONOS_USER_NOTES_END##";

    private const string _notesStart = "##MARE_SYNCHRONOS_USER_NOTES_START##";

    private readonly ApiController _apiController;

    private readonly PeriodicFileScanner _cacheScanner;

    private readonly MareConfigService _configService;

    private readonly DalamudUtilService _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly Dalamud.Localization _localization;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, object> _selectedComboItems = new(StringComparer.Ordinal);
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private bool _cacheDirectoryHasOtherFilesThanCache = false;

    private bool _cacheDirectoryIsValidPath = true;

    private bool _customizePlusExists = false;

    private string _customServerName = "";

    private string _customServerUri = "";

    private bool _glamourerExists = false;

    private bool _heelsExists = false;

    private bool _honorificExists = false;
    private bool _isDirectoryWritable = false;

    private bool _isPenumbraDirectory = false;

    private bool _palettePlusExists = false;
    private bool _penumbraExists = false;

    private int _serverSelectionIndex = -1;

    public UiSharedService(ILogger<UiSharedService> logger, IpcManager ipcManager, ApiController apiController,
        PeriodicFileScanner cacheScanner, FileDialogManager fileDialogManager,
        MareConfigService configService, DalamudUtilService dalamudUtil, DalamudPluginInterface pluginInterface, Dalamud.Localization localization,
        ServerConfigurationManager serverManager, MareMediator mediator) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _apiController = apiController;
        _cacheScanner = cacheScanner;
        FileDialogManager = fileDialogManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _pluginInterface = pluginInterface;
        _localization = localization;
        _serverConfigurationManager = serverManager;

        _localization.SetupWithLangCode("en");

        _isDirectoryWritable = IsDirectoryWritable(_configService.Current.CacheFolder);

        _pluginInterface.UiBuilder.BuildFonts += BuildFont;
        _pluginInterface.UiBuilder.RebuildFonts();

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) =>
        {
            _penumbraExists = _ipcManager.CheckPenumbraApi();
            _glamourerExists = _ipcManager.CheckGlamourerApi();
            _customizePlusExists = _ipcManager.CheckCustomizePlusApi();
            _heelsExists = _ipcManager.CheckHeelsApi();
            _palettePlusExists = _ipcManager.CheckPalettePlusApi();
            _honorificExists = _ipcManager.CheckHonorificApi();
        });
    }

    public ApiController ApiController => _apiController;

    public bool EditTrackerPosition { get; set; }

    public long FileCacheSize => _cacheScanner.FileCacheSize;

    public bool HasValidPenumbraModPath => !(_ipcManager.PenumbraModDirectory ?? string.Empty).IsNullOrEmpty() && Directory.Exists(_ipcManager.PenumbraModDirectory);

    public bool IsInGpose => _dalamudUtil.IsInCutscene;

    public string PlayerName => _dalamudUtil.GetPlayerName();

    public ImFontPtr UidFont { get; private set; }

    public bool UidFontBuilt { get; private set; }

    public Dictionary<ushort, string> WorldData => _dalamudUtil.WorldData.Value;

    public uint WorldId => _dalamudUtil.GetWorldId();

    public static void AttachToolTip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    public static string ByteToString(long bytes, bool addSuffix = true)
    {
        string[] suffix = { "B", "KiB", "MiB", "GiB", "TiB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return addSuffix ? $"{dblSByte:0.00} {suffix[i]}" : $"{dblSByte:0.00}";
    }

    public static void CenterNextWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    public static uint Color(byte r, byte g, byte b, byte a)
    { uint ret = a; ret <<= 8; ret += b; ret <<= 8; ret += g; ret <<= 8; ret += r; return ret; }

    public static uint Color(Vector4 color)
    {
        uint ret = (byte)(color.W * 255);
        ret <<= 8;
        ret += (byte)(color.Z * 255);
        ret <<= 8;
        ret += (byte)(color.Y * 255);
        ret <<= 8;
        ret += (byte)(color.X * 255);
        return ret;
    }

    public static void ColorText(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void ColorTextWrapped(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static bool CtrlPressed() => (GetKeyState(0xA2) & 0x8000) != 0 || (GetKeyState(0xA3) & 0x8000) != 0;

    public static void DrawHelpText(string helpText)
    {
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.8f);
        ImGui.TextDisabled(FontAwesomeIcon.Question.ToIconString());
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(helpText);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static void DrawOutlinedFont(string text, Vector4 fontColor, Vector4 outlineColor, int thickness)
    {
        var original = ImGui.GetCursorPos();

        ImGui.PushStyleColor(ImGuiCol.Text, outlineColor);
        ImGui.SetCursorPos(original with { Y = original.Y - thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { X = original.X - thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { Y = original.Y + thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { X = original.X + thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y - thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y + thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y + thickness });
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y - thickness });
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, fontColor);
        ImGui.SetCursorPos(original);
        ImGui.TextUnformatted(text);
        ImGui.SetCursorPos(original);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void DrawOutlinedFont(ImDrawListPtr drawList, string text, Vector2 textPos, uint fontColor, uint outlineColor, int thickness)
    {
        drawList.AddText(textPos with { Y = textPos.Y - thickness },
            outlineColor, text);
        drawList.AddText(textPos with { X = textPos.X - thickness },
            outlineColor, text);
        drawList.AddText(textPos with { Y = textPos.Y + thickness },
            outlineColor, text);
        drawList.AddText(textPos with { X = textPos.X + thickness },
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y - thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y + thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y + thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y - thickness),
            outlineColor, text);

        drawList.AddText(textPos, fontColor, text);
        drawList.AddText(textPos, fontColor, text);
    }

    public static void DrawWithID(string id, Action drawSubSection)
    {
        ImGui.PushID(id);
        drawSubSection.Invoke();
        ImGui.PopID();
    }

    public static void FontText(string text, ImFontPtr font)
    {
        ImGui.PushFont(font);
        ImGui.TextUnformatted(text);
        ImGui.PopFont();
    }

    public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

    public static Vector4 GetCpuLoadColor(double input) => input < 50 ? ImGuiColors.ParsedGreen :
        input < 90 ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed;

    public static Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var buttonSize = ImGuiHelpers.GetButtonSize(icon.ToIconString());
        ImGui.PopFont();
        return buttonSize;
    }

    public static Vector2 GetIconSize(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(icon.ToIconString());
        ImGui.PopFont();
        return iconSize;
    }

    public static string GetNotes(List<Pair> pairs)
    {
        StringBuilder sb = new();
        sb.AppendLine(_notesStart);
        foreach (var entry in pairs)
        {
            var note = entry.GetNote();
            if (note.IsNullOrEmpty()) continue;

            sb.Append(entry.UserData.UID).Append(":\"").Append(entry.GetNote()).AppendLine("\"");
        }
        sb.AppendLine(_notesEnd);

        return sb.ToString();
    }

    public static float GetWindowContentRegionWidth()
    {
        return ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
    }

    public static bool IconTextButton(FontAwesomeIcon icon, string text, float? width = null)
    {
        var buttonClicked = false;

        var iconSize = GetIconSize(icon);
        var textSize = ImGui.CalcTextSize(text);
        var padding = ImGui.GetStyle().FramePadding;
        var spacing = ImGui.GetStyle().ItemSpacing;

        Vector2 buttonSize;
        var buttonSizeY = (iconSize.Y > textSize.Y ? iconSize.Y : textSize.Y) + padding.Y * 2;

        if (width == null)
        {
            var buttonSizeX = iconSize.X + textSize.X + padding.X * 2 + spacing.X;
            buttonSize = new Vector2(buttonSizeX, buttonSizeY);
        }
        else
        {
            buttonSize = new Vector2(width.Value, buttonSizeY);
        }

        if (ImGui.Button("###" + icon.ToIconString() + text, buttonSize))
        {
            buttonClicked = true;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - buttonSize.X - padding.X);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text(text);

        return buttonClicked;
    }

    public static bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
    {
        try
        {
            using FileStream fs = File.Create(
                       Path.Combine(
                           dirPath,
                           Path.GetRandomFileName()
                       ),
                       1,
                       FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            if (throwIfFails)
                throw;

            return false;
        }
    }

    public static void OutlineTextWrapped(string text, Vector4 textcolor, Vector4 outlineColor, float dist = 3)
    {
        var cursorPos = ImGui.GetCursorPos();
        ColorTextWrapped(text, outlineColor);
        ImGui.SetCursorPos(new(cursorPos.X, cursorPos.Y + dist));
        ColorTextWrapped(text, outlineColor);
        ImGui.SetCursorPos(new(cursorPos.X + dist, cursorPos.Y));
        ColorTextWrapped(text, outlineColor);
        ImGui.SetCursorPos(new(cursorPos.X + dist, cursorPos.Y + dist));
        ColorTextWrapped(text, outlineColor);

        ImGui.SetCursorPos(new(cursorPos.X + dist / 2, cursorPos.Y + dist / 2));
        ColorTextWrapped(text, textcolor);
        ImGui.SetCursorPos(new(cursorPos.X + dist / 2, cursorPos.Y + dist / 2));
        ColorTextWrapped(text, textcolor);
    }

    public static void SetScaledWindowSize(float width, bool centerWindow = true)
    {
        var newLineHeight = ImGui.GetCursorPosY();
        ImGui.NewLine();
        newLineHeight = ImGui.GetCursorPosY() - newLineHeight;
        var y = ImGui.GetCursorPos().Y + ImGui.GetWindowContentRegionMin().Y - newLineHeight * 2 - ImGui.GetStyle().ItemSpacing.Y;

        SetScaledWindowSize(width, y, centerWindow, scaledHeight: true);
    }

    public static void SetScaledWindowSize(float width, float height, bool centerWindow = true, bool scaledHeight = false)
    {
        ImGui.SameLine();
        var x = width * ImGuiHelpers.GlobalScale;
        var y = scaledHeight ? height : height * ImGuiHelpers.GlobalScale;

        if (centerWindow)
        {
            CenterWindow(x, y);
        }

        ImGui.SetWindowSize(new Vector2(x, y));
    }

    public static bool ShiftPressed() => (GetKeyState(0xA1) & 0x8000) != 0 || (GetKeyState(0xA0) & 0x8000) != 0;

    public static void TextWrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static Vector4 UploadColor((long, long) data) => data.Item1 == 0 ? ImGuiColors.DalamudGrey :
        data.Item1 == data.Item2 ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudYellow;

    public bool ApplyNotesFromClipboard(string notes, bool overwrite)
    {
        var splitNotes = notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
        var splitNotesStart = splitNotes.FirstOrDefault();
        var splitNotesEnd = splitNotes.LastOrDefault();
        if (!string.Equals(splitNotesStart, _notesStart, StringComparison.Ordinal) || !string.Equals(splitNotesEnd, _notesEnd, StringComparison.Ordinal))
        {
            return false;
        }

        splitNotes.RemoveAll(n => string.Equals(n, _notesStart, StringComparison.Ordinal) || string.Equals(n, _notesEnd, StringComparison.Ordinal));

        foreach (var note in splitNotes)
        {
            try
            {
                var splittedEntry = note.Split(":", 2, StringSplitOptions.RemoveEmptyEntries);
                var uid = splittedEntry[0];
                var comment = splittedEntry[1].Trim('"');
                if (_serverConfigurationManager.GetNoteForUid(uid) != null && !overwrite) continue;
                _serverConfigurationManager.SetNoteForUid(uid, comment);
            }
            catch
            {
                Logger.LogWarning("Could not parse {note}", note);
            }
        }

        _serverConfigurationManager.SaveNotes();

        return true;
    }

    public void BigText(string text)
    {
        if (UidFontBuilt) ImGui.PushFont(UidFont);
        ImGui.TextUnformatted(text);
        if (UidFontBuilt) ImGui.PopFont();
    }

    public void DrawCacheDirectorySetting()
    {
        ColorTextWrapped("注意：存储文件夹应位于新建的空白文件夹中，并且路径靠近根目录越短越好（比如C:\\MareStorage）。路径不要有中文。不要将路径指定到游戏文件夹。不要将路径指定到Penumbra文件夹。", ImGuiColors.DalamudYellow);
        var cacheDirectory = _configService.Current.CacheFolder;
        ImGui.InputText("存储文件夹##cache", ref cacheDirectory, 255, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        string folderIcon = FontAwesomeIcon.Folder.ToIconString();
        if (ImGui.Button(folderIcon + "##chooseCacheFolder"))
        {
            FileDialogManager.OpenFolderDialog("选择星海同步器存储文件夹", (success, path) =>
            {
                if (!success) return;

                _isPenumbraDirectory = string.Equals(path.ToLowerInvariant(), _ipcManager.PenumbraModDirectory?.ToLowerInvariant(), StringComparison.Ordinal);
                _isDirectoryWritable = IsDirectoryWritable(path);
                _cacheDirectoryHasOtherFilesThanCache = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Any(f => Path.GetFileNameWithoutExtension(f).Length != 40);
                _cacheDirectoryIsValidPath = PathRegex().IsMatch(path);

                if (!string.IsNullOrEmpty(path)
                    && Directory.Exists(path)
                    && _isDirectoryWritable
                    && !_isPenumbraDirectory
                    && !_cacheDirectoryHasOtherFilesThanCache
                    && _cacheDirectoryIsValidPath)
                {
                    _configService.Current.CacheFolder = path;
                    _configService.Save();
                    _cacheScanner.StartScan();
                }
            });
        }
        ImGui.PopFont();

        if (_isPenumbraDirectory)
        {
            ColorTextWrapped("不要将存储路径直接指向Penumbra文件夹。如果一定要指向这里，在其中创建一个子文件夹。", ImGuiColors.DalamudRed);
        }
        else if (!_isDirectoryWritable)
        {
            ColorTextWrapped("您选择的文件夹不存在或无法写入。请提供一个有效的路径。", ImGuiColors.DalamudRed);
        }
        else if (_cacheDirectoryHasOtherFilesThanCache)
        {
            ColorTextWrapped("您选择的文件夹中有与月海同步器无关的文件。仅使用空目录或以前的Mare存储目录。", ImGuiColors.DalamudRed);
        }
        else if (!_cacheDirectoryIsValidPath)
        {
            ColorTextWrapped("您选择的文件夹路径包含FF14无法读取的非法字符。" +
                             "请仅使用拉丁字母（A-Z）、下划线（_）、短划线（-）和阿拉伯数字（0-9）。", ImGuiColors.DalamudRed);
        }

        float maxCacheSize = (float)_configService.Current.MaxLocalCacheInGiB;
        if (ImGui.SliderFloat("最大存储大小（GiB）", ref maxCacheSize, 1f, 200f, "%.2f GiB"))
        {
            _configService.Current.MaxLocalCacheInGiB = maxCacheSize;
            _configService.Save();
        }
        DrawHelpText("存储由月海同步器自动管理。一旦达到设置的容量，它将通过删除最旧的未使用文件自动清除。\n您通常不需要自己清理。");
    }

    public T? DrawCombo<T>(string comboName, IEnumerable<T> comboItems, Func<T, string> toName,
        Action<T>? onSelected = null, T? initialSelectedItem = default)
    {
        if (!comboItems.Any()) return default;

        if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
        {
            if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
            {
                selectedItem = initialSelectedItem;
                _selectedComboItems[comboName] = selectedItem!;
                if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
                    onSelected?.Invoke(initialSelectedItem);
            }
            else
            {
                selectedItem = comboItems.First();
                _selectedComboItems[comboName] = selectedItem!;
            }
        }

        if (ImGui.BeginCombo(comboName, toName((T)selectedItem!)))
        {
            foreach (var item in comboItems)
            {
                bool isSelected = EqualityComparer<T>.Default.Equals(item, (T)selectedItem);
                if (ImGui.Selectable(toName(item), isSelected))
                {
                    _selectedComboItems[comboName] = item!;
                    onSelected?.Invoke(item!);
                }
            }

            ImGui.EndCombo();
        }

        return (T)_selectedComboItems[comboName];
    }

    public void DrawFileScanState()
    {
        ImGui.Text("文件扫描状态");
        ImGui.SameLine();
        if (_cacheScanner.IsScanRunning)
        {
            ImGui.Text("扫描进行中");
            ImGui.Text("当前进度：");
            ImGui.SameLine();
            ImGui.Text(_cacheScanner.TotalFiles == 1
                ? "正在计算文件数量"
                : $"从存储中处理 {_cacheScanner.CurrentFileProgress}/{_cacheScanner.TotalFilesStorage}(已扫描{_cacheScanner.TotalFiles})");
            AttachToolTip("注意：存储的文件可能比扫描的文件多，这是因为扫描器通常会忽略这些文件，" +
                "但游戏会加载这些文件并在你的角色上使用它们，所以它们会被添加到本地存储中。");
        }
        else if (_configService.Current.FileScanPaused)
        {
            ImGui.Text("文件扫描已暂停");
            ImGui.SameLine();
            if (ImGui.Button("强制重新扫描##forcedrescan"))
            {
                _cacheScanner.InvokeScan(forced: true);
            }
        }
        else if (_cacheScanner.HaltScanLocks.Any(f => f.Value > 0))
        {
            ImGui.Text("Halted (" + string.Join(", ", _cacheScanner.HaltScanLocks.Where(f => f.Value > 0).Select(locker => locker.Key + ": " + locker.Value + " halt requests")) + ")");
            ImGui.SameLine();
            if (ImGui.Button("重置暂停需求##clearlocks"))
            {
                _cacheScanner.ResetLocks();
            }
        }
        else
        {
            ImGui.Text("下一次扫描于 " + _cacheScanner.TimeUntilNextScan);
        }
    }

    public bool DrawOtherPluginState()
    {
        var penumbraColor = _penumbraExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var glamourerColor = _glamourerExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var heelsColor = _heelsExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var customizeColor = _customizePlusExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var paletteColor = _palettePlusExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var honorificColor = _honorificExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        ImGui.Text("Penumbra:");
        ImGui.SameLine();
        ImGui.TextColored(penumbraColor, _penumbraExists ? "可用" : "不可用");
        ImGui.SameLine();
        ImGui.Text("Glamourer:");
        ImGui.SameLine();
        ImGui.TextColored(glamourerColor, _glamourerExists ? "可用" : "不可用");
        ImGui.Text("可选插件");
        ImGui.SameLine();
        ImGui.Text("SimpleHeels:");
        ImGui.SameLine();
        ImGui.TextColored(heelsColor, _heelsExists ? "可用" : "不可用");
        ImGui.SameLine();
        ImGui.Text("Customize+:");
        ImGui.SameLine();
        ImGui.TextColored(customizeColor, _customizePlusExists ? "可用" : "不可用");
        ImGui.SameLine();
        ImGui.Text("Palette+:");
        ImGui.SameLine();
        ImGui.TextColored(paletteColor, _palettePlusExists ? "可用" : "不可用");
        ImGui.SameLine();
        ImGui.Text("Honorific:");
        ImGui.SameLine();
        ImGui.TextColored(honorificColor, _honorificExists ? "可用" : "不可用");

        if (!_penumbraExists || !_glamourerExists)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "您需要同时安装Penumbra和Glamourer，并更新到它们的最新版本才能使用月海同步器。");
            return false;
        }

        return true;
    }

    public int DrawServiceSelection(bool selectOnChange = false)
    {
        string[] comboEntries = _serverConfigurationManager.GetServerNames();

        if (_serverSelectionIndex == -1)
        {
            _serverSelectionIndex = Array.IndexOf(_serverConfigurationManager.GetServerApiUrls(), _serverConfigurationManager.CurrentApiUrl);
        }
        if (_serverSelectionIndex == -1 || _serverSelectionIndex >= comboEntries.Length)
        {
            _serverSelectionIndex = 0;
        }
        for (int i = 0; i < comboEntries.Length; i++)
        {
            if (string.Equals(_serverConfigurationManager.CurrentServer?.ServerName, comboEntries[i], StringComparison.OrdinalIgnoreCase))
                comboEntries[i] += " [Current]";
        }
        if (ImGui.BeginCombo("选择服务", comboEntries[_serverSelectionIndex]))
        {
            for (int i = 0; i < comboEntries.Length; i++)
            {
                bool isSelected = _serverSelectionIndex == i;
                if (ImGui.Selectable(comboEntries[i], isSelected))
                {
                    _serverSelectionIndex = i;
                    if (selectOnChange)
                    {
                        _serverConfigurationManager.SelectServer(i);
                    }
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (_serverConfigurationManager.GetSecretKey(_serverSelectionIndex) != null)
        {
            ImGui.SameLine();
            var text = "Connect";
            if (_serverSelectionIndex == _serverConfigurationManager.CurrentServerIndex) text = "重新连接";
            if (IconTextButton(FontAwesomeIcon.Link, text))
            {
                _serverConfigurationManager.SelectServer(_serverSelectionIndex);
                _ = _apiController.CreateConnections();
            }
        }

        if (ImGui.TreeNode("添加自定义服务"))
        {
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("自定义服务URI", ref _customServerUri, 255);
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("自定义服务名称", ref _customServerName, 255);
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Plus, "添加自定义服务")
                && !string.IsNullOrEmpty(_customServerUri)
                && !string.IsNullOrEmpty(_customServerName))
            {
                _serverConfigurationManager.AddServer(new ServerStorage()
                {
                    ServerName = _customServerName,
                    ServerUri = _customServerUri,
                });
                _customServerName = string.Empty;
                _customServerUri = string.Empty;
                _configService.Save();
            }
            ImGui.TreePop();
        }

        return _serverSelectionIndex;
    }

    public void DrawTimeSpanBetweenScansSetting()
    {
        var timeSpan = _configService.Current.TimeSpanBetweenScansInSeconds;
        if (ImGui.SliderInt("定期扫描间隔秒数##timespan", ref timeSpan, 20, 60))
        {
            _configService.Current.TimeSpanBetweenScansInSeconds = timeSpan;
            _configService.Save();
        }
        DrawHelpText("这是两次文件扫描之间的间隔时间（以秒为单位）。增加它以减少系统负载。\n但在缓存或Penumbra模组文件夹中慢慢摸索文件也太过笨拙，因此设置过高可能会导致问题。");
        var isPaused = _configService.Current.FileScanPaused;
        if (ImGui.Checkbox("暂停定期文件扫描##filescanpause", ref isPaused))
        {
            _configService.Current.FileScanPaused = isPaused;
            _configService.Save();
        }
        DrawHelpText("这允许您停止对Penumbra和月海缓存目录的定期扫描。建议在打算移动月海缓存和Penumbra模组文件夹时启用。如果您永久启用此功能，请在将模组添加到Penumbra后运行强制重新扫描。");
    }

    public void LoadLocalization(string languageCode)
    {
        _localization.SetupWithLangCode(languageCode);
        Strings.ToS = new Strings.ToSStrings();
    }

    public void PrintServerState()
    {
        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.TextUnformatted("服务 " + _serverConfigurationManager.CurrentServer!.ServerName + ":");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, "可用");
            ImGui.SameLine();
            ImGui.TextUnformatted("(");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture));
            ImGui.SameLine();
            ImGui.Text("用户在线");
            ImGui.SameLine();
            ImGui.Text(")");
        }
    }

    public void RecalculateFileCacheSize()
    {
        _cacheScanner.InvokeScan(forced: true);
    }

    [LibraryImport("user32")]
    internal static partial short GetKeyState(int nVirtKey);

    internal ImFontPtr GetGameFontHandle()
    {
        return _pluginInterface.UiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.ChnAxis120)).ImFont;
    }

    internal TextureWrap LoadImage(byte[] imageData)
    {
        return _pluginInterface.UiBuilder.LoadImage(imageData);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _pluginInterface.UiBuilder.BuildFonts -= BuildFont;
    }

    private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript)]
    private static partial Regex PathRegex();

    private void BuildFont()
    {
        var fontFile = Path.Combine(_pluginInterface.DalamudAssetDirectory.FullName, "UIRes", "NotoSansCJKjp-Medium.otf");
        UidFontBuilt = false;

        if (File.Exists(fontFile))
        {
            try
            {
                UidFont = ImGui.GetIO().Fonts.AddFontFromFileTTF(fontFile, 35);
                UidFontBuilt = true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Font failed to load. {fontFile}", fontFile);
            }
        }
        else
        {
            Logger.LogDebug("Font doesn't exist. {fontFile}", fontFile);
        }
    }
}