# 划译 · Windows 划词翻译

在桌面软件里选中一个词或一句话，译文就显示在鼠标旁边，不替换原文，也不必更换输入法。

## 下载与试用

1. 到 [v1.1.0 发布页](https://github.com/Joey-debu-g/huayi-selection-translator/releases/tag/v1.1.0) 下载 `HuaYi-1.1.0-Windows.zip`，解压后双击 `划译.exe`。适用于 Windows 10 / 11，使用 .NET Framework 4.8。
2. 在「划译 · 控制窗口」点击「开启自动翻译」，再点「试译这句英文」确认网络可用。每次启动时都默认暂停，无需寻找托盘图标。
3. 到支持文字选区的软件中拖选文字，或双击一个单词。最小化后，可点任务栏或再次打开程序找回控制窗口。

**仅在需要时翻译：** 切换到「仅手动翻译」，选中文字后按 **Ctrl + Alt + T**。某个软件不提供 Windows 可读取的选区时，可自行复制文字，再在控制窗口选择「翻译已复制文字…」，确认预览后才发送。

## 范围与隐私

- 默认中英互译，并根据假名或韩文字母识别日语、韩语。文字特征可能判断错误，可手动指定翻译方向。
- 使用 Windows UI Automation 读取文字选区，不含 OCR，不适用于扫描件或纯图片。每次限一段、不跨换行，最多 500 个 UTF-8 字节。
- 联网翻译由 MyMemory 提供，无需填写 API 密钥。**开启自动翻译后，符合条件的划选文字会自动发送给服务方，可能被长期保留。** 处理机密内容前请先暂停。程序不保存翻译历史，也不自动读取剪贴板。详见[服务条款](https://mymemory.translated.net/terms-and-conditions)。

## 从源码构建

在 Windows PowerShell 7 中进入仓库根目录：

```powershell
.\构建.ps1
```

输出为 `构建输出\正式\划译.exe`。使用系统自带的 .NET Framework C# 编译器，无 NuGet 依赖。离线检查：

```powershell
.\构建.ps1 -WithChecks
& '.\构建输出\正式\HuaYi.Checks.exe' offline
```

[完整使用说明](使用说明.txt) · [源码与测试说明](源码说明.txt)
