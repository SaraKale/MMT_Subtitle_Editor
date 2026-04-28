<h1 align="center">MMT Subtitle Editor</h1>

<p align="center">
  <img src="image/MMTicon.png" align="middle" width = "150"/>
    <br /><br />
</p>

![](image/MMT_SC.jpg)

<p align="center">
语言：<a href="HelpDoc_EN.md">English</a> | <a href="HelpDoc_SC.md">简体中文</a> | <a href="HelpDoc_TC.md">繁體中文</a> 
 | <a href="HelpDoc_JP.md">日本語</a> 

## 介绍

基于 そぼろ 作者 MikuMikuTelop 工具修改，MMT Subtitle Editor 是一个用于编辑、预览和导出MMD专用的MMTScript字幕脚本图形化工具。方便编辑创建字幕，实现简单化操作，参数可视化，歌词搜索下载、批量导出等。

## 功能特点

- **脚本编辑器**：支持语法高亮的 MMTScript 脚本编辑
- **实时预览**：编辑脚本时实时预览字幕效果
- **字幕列表**：可视化字幕时间轴管理
- **属性面板**：图形化界面快速设置字幕属性
- **多语言支持**：支持简体中文、繁体中文、英文、日文
- **在线歌词搜索**：支持从网易云音乐、QQ音乐搜索并导入歌词
- **预设管理**：保存和加载字幕样式预设
- **批量导出**：一键导出所有字幕为 PNG 图片
- **时间码转换**：支持时间码与帧数互相换算
- **动画效果**：支持淡入淡出、随机模糊、滑动、缩放、擦除、百叶窗等动画效果
- **多编码支持**：自动检测并正确读取 UTF-8、Shift-JIS、GBK 等编码文件

## 系统要求

- **操作系统**：
  - Windows 10/11 以上系统
  - macOS (10.15+)
  - Linux (Ubuntu, Debian, Fedora, Arch 等)
- **运行时**：需要安装 .NET 8.0 Runtime
  - 64位：https://dotnet.microsoft.com/zh-cn/download/dotnet/thank-you/runtime-desktop-8.0.26-windows-x64-installer
  - 32位：https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.26-windows-x86-installer
- **分辨率**：建议 1920×1080 或更高

## 使用方法

- 双击运行 `MMT Subtitle Editor.exe` 程序即可。
- 在菜单栏“Language/语言/言語”可以切换你熟悉的语言。目前有：英语、简体中文、繁体中文、日语，如需要其他语言请告知我，我会添加上。
- 导入或创建 `.txt/.mmt` 脚本文件
- 点击“预览”按钮播放查看字幕效果。
- 调整字幕属性面板中的参数，如字体、颜色、大小、位置、对齐方式等。
- 点击“保存”按钮将字幕样式保存为预设。
- 点击“生成文件”将生成 `MikuMikuTelop.x`、`MikuMikuTelop.fx`、`PostTelop.x`、`PostTelop.fx`四个文件和图片序列。
- 导入`MikuMikuTelop.x` 到 MikuMikudance 进行播放！附件顺序需要放置到附件最底部。需要有音频或帧数才能播放。
- PostTelop.x 不能单独使用，它是作为可以和其他MME受影响使用。通过隐藏PostTelop.x，可以暂时解除后效果化。

## 工具菜单使用

### 时间换算

![](image/TimeConversion_SC.jpg)

这是用于时间码与 30fps 帧数互相换算。比如在时间输入`00:01:00.000`，帧数框会显示`1800F`。很方便计算字幕时长。

### 批量导出

![](image/BatchExport_SC.jpg)

这是用于批量将 `.txt/.mmt` 文件导出，将会导出对应的同名文件夹，每个文件夹内有图片序列和`.x/.fx`文件。
直接拖动文件到拖放区域即可。

### 在线歌词搜索

![](image/OnlineLyrics_SC.jpg)

这是用于从网易云音乐、QQ音乐搜索并导入歌词。
首先选择来源、模式、格式后，输入链接、歌曲ID，点击搜索按钮，将会读取到歌词，有些歌曲会提示可能没有歌词。
输入关键字后，点击搜索按钮，将会弹出搜索结果，选择对应的歌曲后点击“确定”按钮即可将歌词导入到歌词面板中。
MMT时间码格式可以选择“时间码”或“秒数”。
“保存Lrc歌词”按钮可以将当前歌词保存为 `.LRC` 文件。
“插入到脚本”按钮可以将当前歌词插入到脚本编辑器中。

歌曲链接/歌曲ID示例：
网易云示例链接：https://music.163.com/#/song?id=1847207199
网易云歌曲ID：1847207199
QQ音乐示例链接：https://y.qq.com/n/ryqq/songDetail/000sjEUD2wMqNt
QQ音乐歌曲ID：000sjEUD2wMqNt

### 选项

![](image/option_SC.jpg)

这是用于设置预览画布选项。
可以设置画布颜色或背景图片。

## MMTScript 语法参考

MMTScript 是为 MMD & MME 字幕特效生成工具「MikuMikuTelop」设计的简易字幕描述语言。

### 基本格式

```
@显示时间,显示时长
字幕文本
```

### 时间命令

| 格式 | 说明 |
|------|------|
| `@3,5` | 从3秒开始显示5秒 |
| `@100F,10` | 从100帧开始显示10秒 |
| `@10` | 从10秒开始，时长自动计算 |
| `@+2,5` | 在前一个字幕后2秒开始 |
| `@*0,5` | 与前一个字幕同时开始 |
| `@32~36` | 从32秒显示到36秒 |
| `@00:00:10.000 - 00:00:12.000` | 指定时间范围。<font color="green">*新增功能</font> |

### 系统命令

#### 字体设置

| 命令 | 参数 | 说明 |
|------|------|------|
| `:font` | 字体名 | 指定字体名称 |
| `:size` | 数值 | 字体大小（相对于屏幕高度一半的百分比） |
| `:fontcolor` | R,G,B | 字体颜色（0-255） |
| `:bold` | - | 开启粗体 |
| `:bold_off` | - | 关闭粗体 |
| `:italic` | - | 开启斜体 |
| `:italic_off` | - | 关闭斜体 |
| `:underline` | - | 开启下划线 |
| `:underline_off` | - | 关闭下划线 |
| `:strikeout` | - | 开启删除线 |
| `:strikeout_off` | - | 关闭删除线 |

#### 位置设置

| 命令 | 参数 | 说明 |
|------|------|------|
| `:pos` | X,Y | 水平和垂直位置（-100到100） |
| `:pos_x` | X | 仅水平位置 |
| `:pos_y` | Y | 仅垂直位置 |
| `:pos_nextline` | - | 移动到下一行 |
| `:pos_next` | - | 移动到延续位置 |
| `:align_left` | - | 左对齐 |
| `:align_center` | - | 居中对齐 |
| `:align_right` | - | 右对齐 |
| `:valign_top` | - | 顶部对齐 |
| `:valign_center` | - | 垂直居中 |
| `:valign_bottom` | - | 底部对齐 |

#### 描边和阴影

| 命令 | 参数 | 说明 |
|------|------|------|
| `:edge` | 粗细 | 描边粗细（相对于字体大小的百分比） |
| `:edgecolor` | R,G,B | 描边颜色 |
| `:edge_round` | - | 圆角描边 |
| `:edge_round_off` | - | 关闭圆角描边 |
| `:shadow` | 距离 | 阴影距离（相对于字体大小的百分比） |
| `:shadowcolor` | R,G,B | 阴影颜色 |
| `:shadowblur` | 半径 | 阴影模糊半径 |

#### 画面设置

| 命令 | 参数 | 说明 |
|------|------|------|
| `:screen` | 宽,高 | 画面尺寸 |
| `:screen_height` | 高度 | 画面高度 |
| `:margin` | X,Y | 边距（相对于字体大小的百分比） |
| `:backcolor` | R,G,B,A | 背景颜色 |

#### 效果设置

| 命令 | 参数 | 说明 |
|------|------|------|
| `:alpha` | 透明度 | 不透明度（0-1.0） |
| `:fade` | 时间 | 淡入淡出时间（秒） |
| `:autotime` | 时间 | 每字符自动时长（秒） |
| `:timebase` | 时间 | 时间基准 |

#### 变换设置

| 命令 | 参数 | 说明 |
|------|------|------|
| `:scalex` | 比例 | 水平缩放 <font color="green">*新增功能</font> |
| `:scaley` | 比例 | 垂直缩放  <font color="green">*新增功能</font> |
| `:rotate` | 角度 | 旋转角度 <font color="green">*新增功能</font> |
| `:spacing` | 间距 | 字符间距 <font color="green">*新增功能</font> |
| `:pos_z` | 深度 | Z轴位置 <font color="green">*新增功能</font> |

#### 动画效果

以下功能是新增的，可以通过这些命令为字幕添加各种动画效果：

| 命令 | 参数 | 说明 |
|------|------|------|
| `:anim fade` | 类型 | 动画类型-淡入淡出 <font color="green">*新增功能</font> |
| `:anim random_blur` | 类型 | 动画类型-随机模糊 <font color="green">*新增功能</font> |
| `:anim slide_up` | 类型 | 动画类型-上滑进入 <font color="green">*新增功能</font> |
| `:anim scale_pop` | 类型 | 动画类型-缩放弹出 <font color="green">*新增功能</font> |
| `:anim wipe` | 类型 | 动画类型-擦除 <font color="green">*新增功能</font> |
| `:anim blinds` | 类型 | 动画类型-百叶窗 <font color="green">*新增功能</font> |
| `:animtime` | 时间 | 动画时间（秒）<font color="green">*新增功能</font> |
| `:animdir` | 方向 | 擦除方向（ltr/rtl）<font color="green">*新增功能</font> |
| `:animblinds` | 数量 | 百叶窗条数 <font color="green">*新增功能</font> |
| `:animblur` | 强度 | 模糊强度 <font color="green">*新增功能</font> |

#### 特殊功能

| 命令 | 参数 | 说明 |
|------|------|------|
| `:tate` | - | 竖排文字 |
| `:tate_off` | - | 关闭竖排 |
| `:billboard` | - | 公告牌模式 |
| `:billboard_off` | - | 关闭公告牌 |
| `:set` | 名称 | 保存当前格式为预设 |
| `:get` | 名称 | 调用保存的格式（default 重置所有） |
| `:skip` | - | 跳过下一个文本命令 |
| `:end` | - | 脚本结束标记 |

### 注释

```
// 这是注释行
```

### 示例脚本

```
// 基本设置
:screen 1280,720
:font MS Gothic
:size 48
:fontcolor 255,255,255
:edge 7
:edgecolor 0,0,0
:align_center

// 第一句字幕
@0,3
欢迎观看

// 第二句字幕
@+1,3
这是示例字幕
可以填写多行字幕

// 使用预设
:set style1

// 调用预设
:get style1

// 结束
:end
```

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| Ctrl+N | 新建文件 |
| Ctrl+O | 打开文件 |
| Ctrl+S | 保存文件 |
| Ctrl+Shift+S | 另存为 |
| F5 | 刷新预览 |
| Ctrl+E | 导出 PNG |
| Ctrl+P | 预设管理 |

## 使用规约

- 可自由分享本程序，不能商用。
- 对于产生的任何问题作者不承担责任。

## 许可证

本项目基于 MIT 许可证开源。

## 致谢

- [MikuMikuTelop](https://www.nicovideo.jp/watch/sm15635052) - 原始 MMTScript 规格设计者：そぼろ
- [Avalonia](https://avaloniaui.net/) - 跨平台 UI 框架
- [SukiUI](https://github.com/kikipoulet/SukiUI) - 现代化 UI 主题
- [163MusicLyrics](https://github.com/jitwxs/163MusicLyrics) - 在线歌词参考项目            
  
## AI辅助

- GLM 5.1
- GPT-5.4
