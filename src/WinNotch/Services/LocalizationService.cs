using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.ProductHardening;

namespace WinNotch.Services;

public sealed class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Chinese =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Settings"] = "设置",
            ["Features"] = "功能",
            ["Save"] = "保存",
            ["Cancel"] = "取消",
            ["Close"] = "关闭",
            ["Delete"] = "删除",
            ["New"] = "新建",
            ["Refresh"] = "刷新",
            ["Available"] = "可用",
            ["Disabled"] = "已禁用",
            ["Unsupported"] = "不支持",
            ["Command Bar"] = "命令栏",
            ["Quick Panel"] = "快捷面板",
            ["Scratchpad"] = "便笺",
            ["Window Switcher"] = "窗口切换器",
            ["System Monitor"] = "系统监控",
            ["Audio Control"] = "音频控制",
            ["Capture"] = "截图",
            ["Image Tools"] = "图像工具",
            ["Input Enhancements"] = "输入增强",
            ["Display, Energy & Hardware"] = "显示、节能与硬件",
            ["Application Management"] = "应用管理",
            ["Permissions"] = "权限",
            ["Permissions & diagnostics"] = "权限与诊断",
            ["Permissions, Backup & Diagnostics"] = "权限、备份与诊断",
            ["Diagnostics"] = "诊断",
            ["Export settings"] = "导出设置",
            ["Import settings"] = "导入设置",
            ["Open settings"] = "打开系统设置",
            ["Request access"] = "请求权限",
            ["On"] = "开",
            ["Off"] = "关",
            ["Start"] = "开始",
            ["Stop"] = "停止",
            ["Copy"] = "复制",
            ["Reset"] = "重置",
            ["Pin"] = "钉住"
            ,["Action"] = "动作"
            ,["Activate"] = "激活"
            ,["Adapt to Windows theme"] = "跟随 Windows 主题"
            ,["Add folder"] = "添加文件夹"
            ,["Add messaging folder…"] = "添加消息下载文件夹…"
            ,["AI agents"] = "AI 智能体"
            ,["Alert settings"] = "告警设置"
            ,["Allow"] = "允许"
            ,["App routing"] = "应用路由"
            ,["Application & Package Management"] = "应用与包管理"
            ,["Apply Configuration"] = "应用配置"
            ,["Arguments"] = "参数"
            ,["Audio visualizer"] = "音频可视化"
            ,["Automatically clean tracking URLs"] = "自动清理跟踪网址"
            ,["Back"] = "返回"
            ,["Battery"] = "电池"
            ,["Battery health"] = "电池健康度"
            ,["Battery indicator"] = "电池指示器"
            ,["Blur region during interval"] = "在时间段内模糊区域"
            ,["Bottom left"] = "左下"
            ,["Bottom right"] = "右下"
            ,["Brightness HUD"] = "亮度浮层"
            ,["Browse"] = "浏览"
            ,["Browse Plugins"] = "浏览插件"
            ,["Calendar panel"] = "日历面板"
            ,["Capture files"] = "捕获文件"
            ,["Capture images"] = "捕获图像"
            ,["Capture, OCR & Color"] = "截图、OCR 与取色"
            ,["Center ⅓"] = "中间三分之一"
            ,["Check for updates"] = "检查更新"
            ,["Choose file"] = "选择文件"
            ,["Choose folder"] = "选择文件夹"
            ,["Choose images…"] = "选择图像…"
            ,["Cleanup"] = "清理"
            ,["Clear"] = "清空"
            ,["Clipboard"] = "剪贴板"
            ,["Close window"] = "关闭窗口"
            ,["Color"] = "颜色"
            ,["Command Bar Settings"] = "命令栏设置"
            ,["Configure Command Bar"] = "配置命令栏"
            ,["Configure Radial Menu"] = "配置径向菜单"
            ,["Content"] = "内容"
            ,["Convert, compress & watermark images"] = "转换、压缩并添加图像水印"
            ,["Convert…"] = "转换…"
            ,["Copy text, images or files to build history."] = "复制文本、图像或文件以建立历史记录。"
            ,["CPU alert"] = "CPU 告警"
            ,["CPU history"] = "CPU 历史"
            ,["Crop"] = "裁剪"
            ,["Current desktop only"] = "仅当前虚拟桌面"
            ,["Current display"] = "当前显示器"
            ,["Dark"] = "深色"
            ,["Devices"] = "设备"
            ,["Discover WinGet upgrades"] = "发现 WinGet 升级"
            ,["Disk rate / used"] = "磁盘速率 / 已用"
            ,["Display"] = "显示器"
            ,["Download live activity"] = "下载实时活动"
            ,["Downloads"] = "下载"
            ,["Draw"] = "绘制"
            ,["Drop files here"] = "将文件拖到这里"
            ,["Executable"] = "可执行文件"
            ,["Export GIF"] = "导出 GIF"
            ,["Export Markdown"] = "导出 Markdown"
            ,["Export MP4"] = "导出 MP4"
            ,["Focus"] = "焦点"
            ,["Focus delay milliseconds"] = "焦点延迟（毫秒）"
            ,["Focus follows mouse after delay"] = "延迟后焦点跟随鼠标"
            ,["Force terminate…"] = "强制终止…"
            ,["Frames per second"] = "每秒帧数"
            ,["GPU alert (estimated when available)"] = "GPU 告警（可用时为估算值）"
            ,["Height"] = "高度"
            ,["History limit"] = "历史记录上限"
            ,["Hover behavior"] = "悬停行为"
            ,["Ignored process names, separated by commas"] = "忽略的进程名，以逗号分隔"
            ,["Installed apps"] = "已安装应用"
            ,["Installer migration"] = "安装器迁移"
            ,["Island on every screen"] = "每个屏幕显示灵动岛"
            ,["JPEG quality"] = "JPEG 质量"
            ,["Keep local crash reports (maximum 20)"] = "保留本地崩溃报告（最多 20 个）"
            ,["Label"] = "标签"
            ,["Language"] = "语言"
            ,["Left ½"] = "左半屏"
            ,["Left ⅓"] = "左侧三分之一"
            ,["Light"] = "浅色"
            ,["Lock"] = "锁定"
            ,["Long hover"] = "长悬停"
            ,["Manage plugins"] = "管理插件"
            ,["Markdown preview"] = "Markdown 预览"
            ,["Maximize"] = "最大化"
            ,["Maximum dimension (0 = keep size)"] = "最大尺寸（0 表示保持）"
            ,["Memory alert"] = "内存告警"
            ,["Memory history"] = "内存历史"
            ,["Messaging retention days"] = "消息下载保留天数"
            ,["Microphone"] = "麦克风"
            ,["Mirror FPS"] = "镜像帧率"
            ,["Monitor sustained performance-budget violations"] = "监控持续性能预算违规"
            ,["Move down"] = "下移"
            ,["Move up"] = "上移"
            ,["Music controls"] = "音乐控制"
            ,["Mute"] = "静音"
            ,["Name"] = "名称"
            ,["Network ↓ / ↑"] = "网络下载 / 上传"
            ,["Next"] = "下一首"
            ,["Next monitor"] = "下一显示器"
            ,["No files selected"] = "未选择文件"
            ,["No folder selected"] = "未选择文件夹"
            ,["No local agent sessions found"] = "未发现本地智能体会话"
            ,["No music playing"] = "没有正在播放的音乐"
            ,["No upcoming events"] = "没有即将到来的日程"
            ,["Non-destructive edit plan"] = "非破坏性编辑方案"
            ,["Notifications"] = "通知"
            ,["Online lyrics"] = "在线歌词"
            ,["Open crash report folder"] = "打开崩溃报告文件夹"
            ,["Open Folder"] = "打开文件夹"
            ,["Output format"] = "输出格式"
            ,["Peek + Click"] = "预览 + 点击"
            ,["Pick color"] = "拾取颜色"
            ,["Play/Pause"] = "播放/暂停"
            ,["Plugin Manager"] = "插件管理"
            ,["Pointer-centered radial menu"] = "以指针为中心的径向菜单"
            ,["Ports & processes"] = "端口与进程"
            ,["Power"] = "功耗"
            ,["Previous"] = "上一首"
            ,["Previous monitor"] = "上一显示器"
            ,["Process"] = "进程"
            ,["Product Hardening"] = "产品硬化"
            ,["Project folder"] = "工程文件夹"
            ,["Quit"] = "退出"
            ,["Quit app…"] = "退出应用…"
            ,["Radial Menu Settings"] = "径向菜单设置"
            ,["Record display"] = "录制显示器"
            ,["Record region…"] = "录制区域…"
            ,["Recording & Media Tools"] = "录屏与媒体工具"
            ,["Recording Editor"] = "录制编辑器"
            ,["Redact"] = "脱敏"
            ,["Refresh hardware"] = "刷新硬件"
            ,["Refresh installed apps"] = "刷新已安装应用"
            ,["Refresh Library"] = "刷新库"
            ,["Refresh TCP owners"] = "刷新端口所有者"
            ,["Refresh windows"] = "刷新窗口"
            ,["Region"] = "区域"
            ,["Remove"] = "移除"
            ,["Remove legacy Inno installation…"] = "移除旧版 Inno 安装…"
            ,["Repeat"] = "循环"
            ,["Replacement"] = "替换内容"
            ,["Request close"] = "请求关闭"
            ,["Reserve space at top"] = "在顶部预留空间"
            ,["Restore"] = "还原"
            ,["Restore defaults"] = "恢复默认"
            ,["Reverse horizontal"] = "反转水平滚动"
            ,["Reverse vertical"] = "反转垂直滚动"
            ,["Review and recycle selected…"] = "审查并回收所选文件…"
            ,["Review and upgrade selected…"] = "审查并升级所选项…"
            ,["Review selected uninstaller…"] = "审查所选卸载程序…"
            ,["Right ½"] = "右半屏"
            ,["Right ⅓"] = "右侧三分之一"
            ,["Run installer…"] = "运行安装器…"
            ,["Scan without deleting"] = "扫描但不删除"
            ,["Scripts"] = "脚本"
            ,["Search clipboard history"] = "搜索剪贴板历史"
            ,["Selected window"] = "所选窗口"
            ,["Show shadow"] = "显示阴影"
            ,["Snippets"] = "片段"
            ,["Sound settings"] = "声音设置"
            ,["Start on boot"] = "开机启动"
            ,["Stop process analysis"] = "停止进程分析"
            ,["System audio"] = "系统音频"
            ,["System HUD"] = "系统浮层"
            ,["Temporary minimum age days"] = "临时文件最小天数"
            ,["Theme"] = "主题"
            ,["Trigger"] = "触发词"
            ,["Uninstall"] = "卸载"
            ,["Updates"] = "更新"
            ,["Volume HUD"] = "音量浮层"
            ,["Watermark (optional)"] = "水印（可选）"
            ,["Webcam mirror"] = "摄像头镜像"
            ,["Window target"] = "窗口目标"
            ,["Zoom selected interval"] = "缩放所选时间段"
        };

    public AppLanguage Language { get; private set; }

    public void Apply(AppLanguage language)
    {
        Language = language;
        CultureInfo culture = language switch
        {
            AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
            AppLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-CN"),
            _ => CultureInfo.CurrentUICulture
        };
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public string Translate(string text)
        => Language == AppLanguage.SimplifiedChinese && Chinese.TryGetValue(text, out string? value)
            ? value : text;

    public void TranslateWindow(Window window)
    {
        if (Language != AppLanguage.SimplifiedChinese) return;
        window.Title = Translate(window.Title);
        TranslateTree(window);
    }

    private void TranslateTree(DependencyObject parent)
    {
        if (parent is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
            text.Text = Translate(text.Text);
        if (parent is ContentControl content && content.Content is string value)
            content.Content = Translate(value);
        if (parent is HeaderedContentControl header && header.Header is string headerValue)
            header.Header = Translate(headerValue);
        if (parent is FrameworkElement element && element.ToolTip is string tooltip)
            element.ToolTip = Translate(tooltip);
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++) TranslateTree(VisualTreeHelper.GetChild(parent, index));
    }
}
