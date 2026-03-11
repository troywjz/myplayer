# MyPlayer

一个基于 `C# + WPF + libmpv` 的轻量级 Windows 本地音视频播放器。

## 中文

### 项目简介
MyPlayer 是一个运行在 Windows PC 上的轻量本地播放器，支持常用的音频和视频播放控制。

### 当前功能
- 打开本地音频/视频文件
- 拖拽文件播放
- 播放 / 暂停
- 进度显示与点击/拖动跳转
- 快进 / 快退 10 秒
- 倍速切换
- 音量调节与静音
- 全屏切换
- 键盘快捷键
- 播放结束停留末帧，再播放从头开始
- 记住音量、倍速、静音和窗口大小

### 技术栈
- `C#`
- `WPF`
- `libmpv`
- `.NET 8`

### 运行与发布
- 开发运行：用 Visual Studio 或 `dotnet build` / `dotnet run`
- 发布目录：`artifacts/publish/win-x64`
- 给别的电脑使用时，请复制整个发布目录，不要只复制单个 `exe`

### 快捷键
- `Space`：播放 / 暂停
- `Left / Right`：快退 / 快进 10 秒
- `Up / Down`：音量加减
- `M`：静音切换
- `F`：全屏切换
- `Esc`：退出全屏
- `1~6`：从低到高切换 6 档倍速
- `Ctrl+O`：打开文件

## English

### Overview
MyPlayer is a lightweight local media player for Windows, built with `C# + WPF + libmpv`.

### Features
- Open local audio and video files
- Drag and drop to play
- Play / pause
- Progress display with click and drag seeking
- 10-second rewind / forward
- Playback speed switching
- Volume control and mute
- Fullscreen toggle
- Keyboard shortcuts
- Stay on the last frame at EOF, restart from the beginning on replay
- Persist volume, speed, mute state, and window size

### Tech Stack
- `C#`
- `WPF`
- `libmpv`
- `.NET 8`

### Run and Distribution
- Development: use Visual Studio or `dotnet build` / `dotnet run`
- Published output: `artifacts/publish/win-x64`
- To run on another PC, copy the whole publish folder, not only the `exe`

### Shortcuts
- `Space`: Play / Pause
- `Left / Right`: Seek backward / forward 10 seconds
- `Up / Down`: Volume up / down
- `M`: Toggle mute
- `F`: Toggle fullscreen
- `Esc`: Exit fullscreen
- `1~6`: Switch among six playback speeds from low to high
- `Ctrl+O`: Open file
