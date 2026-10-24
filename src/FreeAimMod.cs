using System;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(FreeAim.FreeAimMod), "第一人称自由瞄准", "1.0.1", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace FreeAim
{
    // 第一人称（炮镜／车长潜望镜里）移动炮塔不再需要按住右键。
    //
    // 游戏自己的路径：PlayerInput::Update 读 Rewired 动作 "Smooth Aim"（默认鼠标右键），没按住就整块
    // 跳过鼠标瞄准；按住时把每帧的鼠标位移 ViewX/ViewY*0.005 累加进 _joystickOffset 并夹在 ±1，松开
    // 右键才归零。也就是说它是"位移积分成速度档位"的摇杆模型：动一下鼠标，炮塔会以那个速度一直转下
    // 去，这是 GHPC 第一人称瞄准发飘的根源。
    //
    // 本 mod 走游戏自己的最后一步（CrewBrainWeaponsModule::AdjustAimByRatio(角色, 水平, 垂直)，它按
    // 比例乘上火炮自身的最大方向转速、内部再乘 Time.deltaTime），但喂给它的是当帧的鼠标量而不是积分
    // 值：鼠标停，炮塔停；鼠标往上当帧就往上当。
    public sealed class FreeAimMod : MelonMod
    {
        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<bool> eEnabled, eInvertYaw, eInvertPitch, eDebugLog;
        static MelonPreferences_Entry<KeyCode> eToggle;
        static MelonPreferences_Entry<float> eSensitivity, eDeadzone;

        // 玩家坐在驾驶位／只是乘客时游戏根本不瞄准，这两条是 Update 里那块代码的上游门槛
        static readonly FieldInfo FullControlField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_hasFullVehicleControl");
        static readonly FieldInfo SeatField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_controlledPosition");

        static StreamWriter log;
        static string noticeText = "";
        static float noticeTimer;
        static GUIStyle noticeStyle;
        static float saveTimer;
        static float reportTimer;
        static float aimLogTimer;
        static int drivenFrames;

        public override void OnInitializeMelon()
        {
            cat = MelonPreferences.CreateCategory("FreeAim", "第一人称自由瞄准");
            eEnabled = cat.CreateEntry<bool>("Enabled", true, "Master switch. Turn it off and aiming behaves as before, where the sight only moves while the right mouse button is held");
            eToggle = cat.CreateEntry<KeyCode>("ToggleKey", KeyCode.F11, "Key that turns the feature on or off at any time; the current state is printed on screen, so a stray press is not a silent failure");
            eSensitivity = cat.CreateEntry<float>("Sensitivity", 0.15f, "Mouse to turret speed factor: each frame's mouse delta is multiplied by it and by the gun's own maximum traverse speed. The raw delta is only about 1, so 0.05 is nearly invisible; numpad + / - adjust it while playing and the value is saved");
            eDeadzone = cat.CreateEntry<float>("Deadzone", 0.05f, "Mouse deltas below this count as zero. It stops a resting mouse axis from drifting the sight; the threshold is measured in clicks, and vertical deltas are naturally smaller than horizontal ones when you pan slowly, so too large a value eats pitch");
            eInvertYaw = cat.CreateEntry<bool>("InvertYaw", false, "Flip the horizontal axis. Tick this if pushing left turns you right, no code change needed");
            eInvertPitch = cat.CreateEntry<bool>("InvertPitch", false, "Flip the pitch axis. Tick this if up and down are reversed; the game's own invert Y setting takes priority and this mod follows it");
            eDebugLog = cat.CreateEntry<bool>("DebugLog", false, "Write the per-frame decisions to Bin/Mods/FreeAim.debug.log; only turn this on when something is wrong");

            if (eDebugLog.Value) OpenLog();
            MelonLogger.Msg("第一人称自由瞄准 已加载（" + eToggle.Value + " 开关，"
                + (eEnabled.Value ? "现在是开的" : "现在是关的") + "）");
            Log("FreeAim initialised, enabled=" + eEnabled.Value + " key=" + eToggle.Value
                + " sensitivity=" + eSensitivity.Value.ToString("0.000"));
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(eToggle.Value))
            {
                eEnabled.Value = !eEnabled.Value;
                try { cat.SaveToFile(false); } catch (Exception e) { Log("saving threw: " + e.Message); }
                // 一次误按就会让瞄准手感变回按住右键，所以把状态打在屏幕上说清楚
                ShowNotice(eEnabled.Value
                    ? "第一人称自由瞄准 已开启（" + eToggle.Value + "）- 鼠标不用再按右键"
                    : "第一人称自由瞄准 已关闭 - 按 " + eToggle.Value + " 恢复");
                Log("feature " + (eEnabled.Value ? "on" : "off"));
            }

            TuneSensitivity();
            if (noticeTimer > 0f) noticeTimer -= Time.unscaledDeltaTime;
            if (eDebugLog.Value) Report();
        }

        // 由 Patches.cs 里 PlayerInput::Update 的后置补丁调用。必须排在游戏自己那一段之后：
        // FireControlSystem 在 WeaponAuthoritative 为真时会用炮塔实际指向重算瞄准向量，写早了会被覆盖。
        internal static void ApplyAim(GHPC.Player.PlayerInput pi)
        {
            if (pi == null) { lastGate = "no PlayerInput"; return; }
            if (!eEnabled.Value) { lastGate = "switched off"; return; }
            try
            {
                if (!ShouldDrive(pi)) return;

                float sx = GHPC.Utility.InputUtil.ViewX;
                float sy = GHPC.Utility.InputUtil.ViewY;
                // 游戏在把 ViewY 送进瞄准链之前先看设置里的 Y 轴反转
                if (GHPC.Utility.InputUtil.InvertYAxis) sy = -sy;
                if (eInvertYaw.Value) sx = -sx;
                if (eInvertPitch.Value) sy = -sy;

                float dz = Mathf.Max(0f, eDeadzone.Value);
                if (Mathf.Abs(sx) < dz) sx = 0f;
                if (Mathf.Abs(sy) < dz) sy = 0f;

                float k = Mathf.Clamp(eSensitivity.Value, 0.001f, 2f);
                // 水平取 ViewX、垂直取 -ViewY：这就是游戏自己两条调用路径（IL_13DF / IL_1434）共同的轴序
                float h = Mathf.Clamp(sx * k, -1f, 1f);
                float v = Mathf.Clamp(-sy * k, -1f, 1f);
                if (h == 0f && v == 0f) return;

                var brain = pi.CurrentPlayerCrewBrain;
                if (brain == null) return;
                var module = brain.WeaponsModule;
                if (module == null) return;
                var weapon = module.ActiveWeapon;
                if (weapon == null) return;

                module.AdjustAimByRatio(weapon.Role, h, v);
                drivenFrames++;
                if (eDebugLog.Value)
                {
                    // 每帧一行会把日志刷爆，只按半秒一次取样，够看出方向和对不对
                    aimLogTimer -= Time.unscaledDeltaTime;
                    if (aimLogTimer <= 0f)
                    {
                        aimLogTimer = 0.5f;
                        Log("aim " + sx.ToString("0.0") + "," + sy.ToString("0.0") + " -> "
                            + h.ToString("0.000") + "," + v.ToString("0.000") + " " + weapon.Role);
                    }
                }
            }
            catch (Exception e)
            {
                Log("drive threw: " + e.Message);
            }
        }

        // 逐条复刻游戏自己那段鼠标代码的门槛，一条不满足就完全不插手。
        // lastGate 记下最后一条挡路的判断，用户说「没效果」时它直接指出原因。
        static string lastGate = "-";

        static bool ShouldDrive(GHPC.Player.PlayerInput pi)
        {
            if (GHPC.AarController.InAar) { lastGate = "AAR"; return false; }
            if (!pi.PlayerCanControlGunnerNow) { lastGate = "not gunner now"; return false; }
            if (pi.IsExteriorMode) { lastGate = "exterior view"; return false; }
            if (pi.UsingBinoculars) { lastGate = "binoculars"; return false; }
            if (GHPC.Utility.InputUtil.IsJoystick) { lastGate = "gamepad is last device"; return false; }

            if (FullControlField != null && SeatField != null)
            {
                bool full = false;
                int seat = -1;
                try
                {
                    full = (bool)FullControlField.GetValue(pi);
                    seat = (int)SeatField.GetValue(pi);
                }
                catch { lastGate = "seat fields unreadable"; return false; }
                if (!full && seat != 2) { lastGate = "no vehicle control, seat " + seat; return false; }
            }

            var slot = GHPC.Camera.CameraSlot.ActiveInstance;
            // V_18：活动镜头槽位存在且不允许自由转动，鼠标才归瞄准炮塔（车长环、第三人称不算）
            if (slot == null) { lastGate = "no active camera slot"; return false; }
            if (slot.AllowFreeLook) { lastGate = "slot allows free look"; return false; }

            // 玩家真的按住了"Smooth Aim"时游戏自己在驱动，绝不能叠加
            bool held = SmoothAimHeld();
            if (GHPC.Utility.InputUtil.InvertPalmSwitch) held = !held;
            if (held) { lastGate = "palm switch held"; return false; }
            lastGate = "clear (" + slot.gameObject.name + ")";
            return true;
        }

        static bool SmoothAimHeld()
        {
            try
            {
                var player = GHPC.Utility.InputUtil.MainPlayer;
                if (player != null) return player.GetButton("Smooth Aim");
            }
            catch (Exception e) { Log("GetButton threw: " + e.Message); }
            return Input.GetMouseButton(1);
        }

        // ---------------------------------------------------------------- live tuning

        void TuneSensitivity()
        {
            float delta = 0f;
            if (Input.GetKeyDown(KeyCode.KeypadPlus)) delta = 1f;
            if (Input.GetKeyDown(KeyCode.KeypadMinus)) delta = -1f;
            if (delta == 0f) return;

            float next = eSensitivity.Value;
            next = delta > 0f ? next * 1.25f : next / 1.25f;
            next = Mathf.Clamp(next, 0.002f, 2f);
            if (Mathf.Abs(next - eSensitivity.Value) < 0.000001f) return;
            eSensitivity.Value = next;

            ShowNotice("瞄准灵敏度 " + next.ToString("0.000"));
            saveTimer -= Time.unscaledDeltaTime;
            if (saveTimer > 0f) return;
            saveTimer = 1f;
            Log("Sensitivity = " + next.ToString("0.000"));
            try { cat.SaveToFile(false); } catch (Exception e) { Log("saving threw: " + e.Message); }
        }

        void Report()
        {
            reportTimer -= Time.unscaledDeltaTime;
            if (reportTimer > 0f) return;
            reportTimer = 1f;
            Log("driven in the last second: " + drivenFrames + " frames  gate: " + lastGate);
            drivenFrames = 0;
        }

        // ---------------------------------------------------------------- notice

        static void ShowNotice(string text)
        {
            noticeText = text;
            noticeTimer = 2.5f;
        }

        public override void OnGUI()
        {
            if (noticeTimer <= 0f || string.IsNullOrEmpty(noticeText)) return;
            if (noticeStyle == null)
            {
                noticeStyle = new GUIStyle(GUI.skin.label);
                noticeStyle.fontSize = 16;
                noticeStyle.alignment = TextAnchor.MiddleCenter;
            }

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(noticeTimer * 1.5f));
            GUI.Label(new Rect(0f, Screen.height * 0.07f, Screen.width, 28f), noticeText, noticeStyle);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- logging

        static void OpenLog()
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
                Directory.CreateDirectory(dir);
                log = new StreamWriter(Path.Combine(dir, "FreeAim.debug.log"), true, new UTF8Encoding(false))
                { AutoFlush = true };
            }
            catch { log = null; }
        }

        internal static void Log(object message)
        {
            if (log == null) return;
            try { log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message); }
            catch { }
        }

        public override void OnApplicationQuit()
        {
            try { if (log != null) log.Close(); } catch { }
            log = null;
        }
    }
}
