# 第一人称自由瞄准

Gunner, HEAT, PC! 的 MelonLoader 模组。第一人称下不用按住右键也能转炮塔，鼠标停下来炮塔就停。

## 你会看到什么

原版要按住右键（Smooth Aim）才能动炮塔，而且它把每帧的鼠标量攒成一个速度档位：鼠标动一下，炮塔就按那个速度一直转下去，所以瞄准总像在漂。这个模组改成鼠标动多少、这一帧就转多少，鼠标一停炮塔立刻停。

适用于炮手的第一人称和炮镜，也适用于车长席上真的能控制炮塔的时候。外部视角、望远镜、车长周视环这些允许自由看的地方不接管，游戏原样。手柄是最近输入设备时也不接管。

按住右键时完全不插手，游戏自己那一套照旧，两者不会叠加。

`F11` 随时开关，屏幕上会打出当前状态；小键盘 `+` / `-` 边打边调灵敏度，调完自动存。

## 装

1. 先彻底退出游戏。游戏开着的时候 dll 是锁住的，覆盖不了。
2. 把 zip 解压到游戏根目录，就是放着 `GHPC.exe` 的那一层。解压出来应该看到 `Bin\Mods\FreeAim.dll`。
3. 开一次游戏。MelonLoader 会在 `Bin\UserData\MelonPreferences.cfg` 里写出 `[FreeAim]` 这一段。

需要 MelonLoader v0.7.2（net35 运行时），下载包里没有它。它不改游戏任何文件，删掉那个 dll 就算卸载。

## 能改的东西

| 名字 | 默认 | 作用 |
| --- | --- | --- |
| Enabled | true | 总开关，关掉就是原版手感 |
| ToggleKey | F11 | 任务中开关用的键 |
| Sensitivity | 0.15 | 鼠标量到炮塔转速的系数，小键盘 `+` / `-` 可以边打边调 |
| Deadzone | 0.05 | 小于这个值的鼠标量当 0，防鼠标自己飘 |
| InvertYaw | false | 左右反了就勾它 |
| InvertPitch | false | 上下反了就勾它 |
| DebugLog | false | 出问题再开，会写 `Bin/Mods/FreeAim.debug.log` |

## 先说清楚

只改「鼠标怎么转炮塔」这一步，主炮稳定、提前量、火控解算都没碰。游戏设置里的 Y 轴反转优先级更高，本模组跟着它一起翻。手柄不接管。

## 想自己编译

不用 .NET SDK，也不用 Unity 编辑器，一个 dll 而已，用任何 Roslyn `csc` 对着游戏自带的托管程序集编就行：

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh deploy
```

`deploy` 会把编出来的 dll 覆盖到 `<游戏目录>\Bin\Mods\`，所以先关游戏。不加 `deploy` 就只编到 `src/bin/`。

## In English

Lets you traverse the turret in first person without holding the right mouse button. The stock game integrates the mouse delta into a speed level, so the turret keeps turning after you stop moving the mouse; this mod feeds each frame's delta straight to the gun, so it stops when the mouse stops. It only takes over in the gunner's first-person view or when the commander really has turret control, skips exterior views, binoculars and free-look slots, and stays out of the way entirely while the right button is held. `F11` toggles it; numpad `+` / `-` tune sensitivity live.

MIT，与 Radian Simulations 无关。在当前 Steam 版上实测过，游戏更新有可能让它失效。
