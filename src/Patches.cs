using HarmonyLib;

namespace FreeAim
{
    // PlayerInput::Update 是游戏唯一一处用鼠标驱动炮塔瞄准的地方，每帧只跑一次；它的后置补丁是本 mod
    // 唯一能写在这段代码之后的位置，FireControlSystem 之后（LateUpdate 里 AimablePlatform）才会读到。
    [HarmonyPatch(typeof(GHPC.Player.PlayerInput), "Update")]
    internal static class PlayerInput_Update
    {
        static void Postfix(GHPC.Player.PlayerInput __instance)
        {
            FreeAimMod.ApplyAim(__instance);
        }
    }
}
