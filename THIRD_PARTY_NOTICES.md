# 第三方许可与素材说明

本项目的 MIT 许可适用于原创代码和文档，不授予第三方素材或歌词的权利。以下记录依赖及用户指定的参考来源。

## NAudio 2.2.1

来源：[NAudio](https://github.com/naudio/NAudio)。NuGet 包 `NAudio` 及其 `NAudio.Core`、`NAudio.Wasapi`、`NAudio.WinMM`、`NAudio.WinForms`、`NAudio.Midi`、`NAudio.Asio` 组件版本为 2.2.1。

完整许可文本保存在 [licenses/NAudio-LICENSE.txt](licenses/NAudio-LICENSE.txt)，由构建所用的 `NAudio 2.2.1/license.txt` 原样复制。包说明中的作者为 Mark Heath & Contributors；许可文件的版权行为 `Copyright 2020 Mark Heath`。

## .NET 运行环境

Windows x64 免安装版包含 Microsoft .NET 8 的 .NETCore / WindowsDesktop 运行环境。其项目许可为 MIT；运行环境还包含按其他许可提供的第三方组件。

来源：[.NET runtime](https://github.com/dotnet/runtime) 和 [WindowsDesktop](https://github.com/dotnet/windowsdesktop)。构建所用 .NET 8.0.27 运行包的上游 MIT 许可和完整第三方通知原样保存在：

- [licenses/NET-LICENSE.txt](licenses/NET-LICENSE.txt)
- [licenses/NET-THIRD-PARTY-NOTICES.txt](licenses/NET-THIRD-PARTY-NOTICES.txt)

版权行为 `Copyright (c) .NET Foundation and Contributors`。

## 角色 GIF、图片及图标

用户已确认取得默认人物作者的授权，可以公开发布本项目，条件为注明作者是哔哩哔哩 **@疚刃**。仓库及发布附件中的相关素材保留此署名，程序的素材与关于页面均注明作者。

默认 `Assets/default-original.gif` 为用户在本次开发请求中提供的透明 GIF。`Assets/default/frame-*.png` 为其动画帧缓存；`Assets/preview.png` 和相关角色图标属于素材或衍生文件。

默认人物素材作者为哔哩哔哩 **@疚刃**，授权说明依据用户确认记录。该授权用于公开发布本项目；用户提供、处理或打包素材并不使其适用本项目的 MIT 许可。本说明不对这些素材声明原创版权或向下游授予新的再许可。

## Ya1.mp3 / Ya2.mp3 与泡泡参考

用户指定了 [DeepSeek-Balance-Whale-Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget) 的本地安装版本 `dsh-whale-widget 0.4.2`，用于参考泡泡外观、短句形式以及按下 / 松开音效。

本地完整版本使用的 `Assets/Sounds/Ya1.mp3` 和 `Ya2.mp3` 来自该用户指定项目。上游代码为 MIT，版权行为 `Copyright (c) 2026 MeteorNOX`。其 [PROVENANCE.md](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget/blob/main/PROVENANCE.md) 明确将 `assets/**` 和 `icon.png` 排除在代码 MIT 许可之外，称素材随插件原样提供、仅用于运行该插件，并不授予再许可。该代码许可不能用来推定两个 MP3 的再分发权。

这两个 MP3 仅在本地完整版本保留，不包含在本项目的 GitHub 源码和发布附件中。发布版本在缺少配对文件时使用程序合成音效，用户也可以导入自己的 WAV / MP3。

本桌宠的泡泡由 WPF 矢量图形绘制，没有使用参考图中的人物，也没有复制上游的脚本实现。此处记录参考来源，不对上游美术或音效作新的授权声明。

## 对话文字

本版预置对话保留用户在运行中程序编辑的 29 行内容，其中包含《朋友的酒》短句及用户指定的简短感叹词。用户此前提供了歌词文字及[《朋友的酒》歌词页面（李晓杰）](https://www.5nd.com/gecilrc/316097.htm)作为参考。歌词版权属于原权利人，不适用本项目 MIT 许可。

用户自行编辑或添加的预置、自定义对话保存在本地设置中，其权利状态取决于具体内容。
