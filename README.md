# 弹弹桌宠（Tantan）v0.5.0

Windows 10 / 11 桌宠，使用透明 GIF 作为角色，支持系统音频律动、按压形变、自由旋转和屏幕边缘反弹。原生 .NET 8 / WPF 实现，Windows x64 免安装版自带运行环境。

[GitHub 仓库](https://github.com/Mr-Goodname/tantan-desktop-pet-next) · [下载发布版](https://github.com/Mr-Goodname/tantan-desktop-pet-next/releases/latest)

v0.5.0 支持在同一个程序中创建多个角色，每个角色独立调整素材、大小、运动、音效和对话。角色缩放会同步缩放气泡和文字；另有气泡整体大小、字体粗细和随机推进速度选项。气泡按完整描边计算边界，过长内容会整体缩小到屏幕范围内。

默认人物素材作者为哔哩哔哩 **@疚刃**。用户已取得作者授权，可在注明作者的情况下公开发布本项目；程序内的素材与关于页面均保留署名。第三方人物素材不适用项目代码的 MIT 许可。

v0.4.2 使用 Tantan 英文名。捏捏音效增大播放缓冲，新的点击会截断上一次声音，避免连续点击积压音效；编辑预设和自定义对话只更新对话池，自动保存交给后台处理。预设默认内容采用用户在运行中程序编辑的 29 行短句。

v0.4.1 修复了气泡后台预热后，首次显示长台词时仍沿用旧窗口裁剪范围的问题。预热改用独立图形，首次显示后由窗口重新完成布局。

## 启动与操作

下载并解压[发布页](https://github.com/Mr-Goodname/tantan-desktop-pet-next/releases/latest)中的免安装包，打开 **弹弹桌宠.exe**。请保留旁边的 `Assets` 文件夹。首次启动显示设置；之后默认显示桌宠和托盘图标。

- **鼠标按下**：角色压扁，按住时维持压扁。
- **鼠标松开**：角色弹性回弹。
- **按住拖动**：调整位置。启用漂浮运动时，松手后按最近的拖动速度继续运动。
- **右键角色**：打开设置、切换音频律动和漂浮运动、预览随机对话、隐藏或退出。
- **双击托盘图标**：打开设置；隐藏后可从托盘恢复。

设置即时生效并自动保存。升级时继续使用原有设置，自定义素材和音效保留。

## 多个角色

设置上方的“创建角色”复制当前角色的设置并创建新实例；“当前角色”下拉框切换要编辑的角色。右键和托盘菜单也可创建角色、打开各自设置、隐藏或恢复显示。最多支持 12 个实例，同一素材共享冻结图像缓存，系统音频只捕获一次。

每个实例独立保存名称、素材、大小、物理运动、音效和对话设置。移动角色之间会按可见轮廓碰撞反弹，固定角色和正在拖动的角色作为障碍保持当前位置。高速移动采用连续碰撞检测。“删除此角色”只移除实例，不删除导入素材；至少保留一个角色。“退出程序”退出全部角色，下次启动恢复角色列表和设置。

默认人物素材来源为哔哩哔哩 **@疚刃**，程序的素材与关于页面均注明此来源。

仓库和发布附件不附带 Ya1 / Ya2 两段音效，使用程序合成音效；本地完整版本保留两段文件，也可导入自己的 WAV / MP3。

## v0.4 更新

### 对话字体和内容

在“素材与对话”中选择电脑已安装的字体、字体粗细，调节字号并预览。默认字体为 **Microsoft YaHei UI**，基准字号为 **13.5**，基准范围 **10–32**；实际字号随角色大小及 50%–200% 的气泡比例同步变化，字号滑块显示当前缩放后的值。粗细支持九档（100–900）。气泡图形、描边和文字整体缩放，长句超过屏幕空间时整体缩小显示。

“预置对话”和“自定义对话”分别编辑，均为每行一句，显示时从两者随机选择。预置内容可以修改、删除，也可以单独恢复默认；恢复预置不会清空自定义对话。

本版预置对话使用用户在运行中程序编辑的 29 行内容，包括《朋友的酒》短句、“欧耶耶~”“哦！”“耶！”和“Happy！Lucky！Smile！Yeah!”。自定义对话继续保留。

### 手动检查更新

设置中的“检查更新”按钮仅在点击时连接 GitHub，读取最新正式 Release 并打开[发布页](https://github.com/Mr-Goodname/tantan-desktop-pet-next/releases)下载。若使用私有仓库版本，程序会提示登录 GitHub 查看；程序不保存 GitHub 令牌。

程序不在后台定时检查，不自动下载、安装或覆盖文件。音频、素材和自定义对话不会随更新检查上传。

## 音频与按压

系统播放音频通过默认播放设备的 WASAPI loopback 分析，只在内存中计算能量，不读取麦克风，不录制或保存音频。切换耳机或扬声器后会自动重新连接。音频律动可以开关，灵敏度可调。

按压弹性幅度支持 0–3 倍，设为 0 时只播放音效。可选择合成音效或自定义 WAV / MP3。声音提前解码缓存，输出设备在后台准备并复用，鼠标事件不直接打开或关闭声音驱动。

本地完整版本的橡胶鸭音效在按下时播放 Ya1、松开时播放 Ya2；单次快速点击按顺序播放。再次按下会截断上一轮音效，只保留这次点击，松开音效接在本次按下音效后面。仓库发布附件不分发这两个文件，缺少配对文件时自动使用合成音效。

自定义音效最大 20 MB、10 秒，导入时解码并复制到本地 `Sounds` 目录，之后不依赖原文件。

## 漂浮运动与显示

漂浮运动支持当前屏幕范围内的平面自由旋转和边界反弹，可启用或关闭移动、旋转速度衰减。随机推进可以手动触发，也可以每隔 3–120 秒自动触发。启用随机速度后，每次推进在 25 px/s 至所选速度上限之间选择；关闭时使用所选固定速度。关闭漂浮后立即停住并恢复正向，仍可音频律动并作为其他角色的碰撞障碍。

拖动可以跨屏，松手后在当前屏幕内运动。速度滑块调整随机推进速度；甩出速度根据最近 90 毫秒的鼠标轨迹计算。

碰撞根据当前 GIF 帧的可见像素轮廓计算，包含旋转、音频律动和按压形变，支持横纵 DPI 不一致的图片。v0.3 的 WPF 渲染验证中，默认素材各帧及测试变形与碰撞边界的最大偏差约 1.03 逻辑像素。

可选择置顶、普通窗口层级，以及其他应用全屏时自动隐藏。隐藏期间暂停动画、漂浮运动和音频捕获。桌宠未显示时，可以从托盘恢复显示或关闭自动隐藏。

## 角色素材与泡泡

角色大小支持 72–480 逻辑像素，按系统 DPI 缩放。可导入 GIF、透明 PNG、JPG 或 BMP，也可恢复默认角色。导入文件保存在本地 `Characters` 目录。

素材上限为 32 MB、4096 像素、180 帧；导入后最大边为 384 像素，动画像素缓存约 40 MB 上限。默认动画缓存约 15 MB。支持 GIF 帧时间、透明度和局部帧合成。

随机对话采用 WPF 绘制的蓝边思考泡泡，跟随桌宠并在屏幕边缘调整位置。显示间隔可调，也可关闭随机对话。

## 资源与本地数据

默认目标 60 帧，可选择 20 帧省电或 30 帧均衡。GIF 帧与运动刷新独立；碰撞轮廓在加载时缓存，运行时不逐像素扫描。免安装包采用不压缩程序集的单文件打包方式。

此前 v0.3 在本机 16 逻辑核心、150% 缩放、220 像素的短时检查中，旋转、系统音频律动并每秒连续按压 4 次时，实际刷新约 58 帧，工作集约 157 MB，CPU 约整机 1%。这不是 v0.4 的新测量；素材、系统负载和设置面板会影响占用。

数据保存在 `%LOCALAPPDATA%\BonbonPet`：

- `settings.json`：显示、音频、运动、对话字体，以及可编辑的预置和自定义对话。
- `profiles.json`：所有角色的独立设置、名称、素材路径和位置；第一次升级从原 `settings.json` 迁移一个角色，同时保留主角色的兼容设置文件。
- `Characters`：导入的角色素材。
- `Sounds`：导入的音效。

程序不会修改自启动或系统音量。重置时可以先退出程序，备份并删除这个数据目录。

## 构建与验证

需要 Windows 和 .NET 8 SDK。在项目目录执行：

```powershell
.\tools\build.ps1 build BonbonPet.csproj -c Release
.\tools\build.ps1 run --project Tests\BonbonPet.PhysicsTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Native\BonbonPet.NativeTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Outline\BonbonPet.OutlineTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Pointer\BonbonPet.PointerTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Sounds\BonbonPet.SoundTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Bubble\BonbonPet.BubbleTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Updates\BonbonPet.UpdateTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Profiles\BonbonPet.ProfileTests.csproj -c Release
.\tools\build.ps1 run --project Tests\Collisions\BonbonPet.CollisionTests.csproj -c Release
.\tools\build.ps1 run --project tools\SpriteProbe\SpriteProbe.csproj -c Release
.\tools\build.ps1 run --project Tests\Audio\AudioCheck.csproj -c Release -- artifacts\audio-result.json
```

UI 检查使用 `BonbonPet.exe --self-test <输出目录>`，生成结果和实际界面 PNG，不修改用户设置。性能检查使用 `--benchmark <输出目录>`，重复按压阶段播放低音量按压音效。音频集成检查短暂播放低音量测试音和一次音效，不改变系统音量；如果已有系统声音，`ToneIsolatedFromOtherAudio` 会为 false。

`tools\build.ps1` 针对本机缺少的可选 NuGet 缓存 targets 文件使用项目内的局部兼容副本，不修改系统 SDK。正常 SDK 可直接运行 `dotnet build`。

## 许可与素材来源

本项目原创代码按 [MIT 许可](LICENSE) 提供。此许可不覆盖默认 GIF、MP3、由素材生成的图片和图标，以及用户提供的第三方歌词；这些内容的来源及许可范围见 [第三方说明](THIRD_PARTY_NOTICES.md) 和 [素材记录](Assets/Sources.txt)。

第三方音频库为 [NAudio 2.2.1](https://github.com/naudio/NAudio)，按 MIT 许可提供。免安装包同时包含 .NET 运行环境，其上游许可和第三方通知保存在 `licenses` 目录。
