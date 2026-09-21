using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using MEC;
using PlayerRoles;
using Scp181.Visuals;

namespace Scp181
{
    /// <summary>
    /// SCP-181 身份管理。
    /// 记录当前 SCP-181 玩家、其撤立场(配色)与绝境生还后的免伤窗口。
    /// </summary>
    public static class Scp181Manager
    {
        /// <summary>当前 SCP-181 玩家 Id -> 撤立场（D=默认）。</summary>
        private static readonly Dictionary<int, Scp181Team> Active = new Dictionary<int, Scp181Team>();
        /// <summary>绝境生还后的免伤截止时间（DateTime ticks）。</summary>
        private static readonly Dictionary<int, long> ImmortalUntil = new Dictionary<int, long>();
        /// <summary>绝境生还剩余次数玩家 Id -> 剩余次数（用完即清零）。</summary>
        private static readonly Dictionary<int, int> SurviveLeft = new Dictionary<int, int>();

        private static Config Config => MainClass.Instance.Config;

        public static bool IsScp181(Player p) => p != null && Active.ContainsKey(p.Id);

        public static Scp181Team? GetTeam(Player p)
            => Active.TryGetValue(p.Id, out var t) ? (Scp181Team?)t : null;

        /// <summary>受击时是否处于绝境生还后的免伤窗口。</summary>
        public static bool InSurviveImmunity(Player p)
            => p != null && ImmortalUntil.TryGetValue(p.Id, out var until) && System.DateTime.UtcNow.Ticks < until;

        /// <summary>绝境生还后开启短暂免伤窗口。</summary>
        public static void GrantSurviveImmunity(Player p, float seconds)
            => ImmortalUntil[p.Id] = System.DateTime.UtcNow.AddSeconds(seconds).Ticks;

        /// <summary>绝境生还：若还有剩余次数则消耗一次并返回 true（用一次少一次）。</summary>
        public static bool TryUseSurvive(Player p)
        {
            if (p == null) return false;
            if (!SurviveLeft.TryGetValue(p.Id, out int left) || left <= 0)
                return false;
            SurviveLeft[p.Id] = left - 1;
            return true;
        }

        /// <summary>把指定存活玩家设置为 SCP-181（设为 D 级模型 + 挂身份 + 显示介绍）。</summary>
        public static void Assign(Player p)
        {
            if (p == null || !p.IsConnected) return;

            // 若已是 SCP-181（例如重复指令/撤离后），仅刷新介绍
            if (!IsScp181(p))
            {
                Active[p.Id] = Scp181Team.D;
                SurviveLeft[p.Id] = Config.SurviveChances; // 赋身时重设绝境生还次数
                Config cfg = Config;
                try
                {
                    p.ReferenceHub.roleManager.ServerSetRole(RoleTypeId.ClassD, RoleChangeReason.None);
                    p.MaxHealth = 100;
                    p.Health = 100;
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[Scp181] Assign 设置 D 级角色失败: {ex.Message}");
                }
            }
            else
            {
                Active[p.Id] = Scp181Team.D;
            }

            Scp181Hints.RemoveRoleIntro(p);
            Scp181Hints.ShowRoleIntro(p, Scp181Team.D);
            ApplyReductionEffects(p);
            Timing.RunCoroutine(GuardDebuffs(p)); // 常驻清除 SCP debuff
            if (Config.Debug)
                Log.Info($"[Scp181] 玩家 {p.Nickname} ({p.UserId}) 已成为 SCP-181");
        }

        /// <summary>给 SCP-181 施加 50 级 躯体减伤 + 普攻减伤（换角会清除效果，撤离后需重新施加）。</summary>
        public static void ApplyReductionEffects(Player p)
        {
            if (p == null) return;
            try
            {
                p.EnableEffect(EffectType.BodyshotReduction, 50, 3600f);
                p.EnableEffect(EffectType.DamageReduction, 50, 3600f);
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] 施加减伤效果失败: {ex.Message}");
            }
        }

        /// <summary>清除 SCP 施加的各类 debuff（049 心脏骤停、106 腐蚀/流血、939 毒、贡喉等）。</summary>
        public static void ClearScpDebuffs(Player p)
        {
            if (p == null) return;
            try
            {
                p.DisableEffect(EffectType.CardiacArrest);
                p.DisableEffect(EffectType.Corroding);
                p.DisableEffect(EffectType.Bleeding);
                p.DisableEffect(EffectType.Poisoned);
                p.DisableEffect(EffectType.Ensnared);
                p.DisableEffect(EffectType.Concussed);
                p.DisableEffect(EffectType.Hemorrhage);
                p.DisableEffect(EffectType.Burned);
            }
            catch { }
        }

        /// <summary>对 SCP-181 常驻清除 debuff 的守卫协程（0.5s 轮询，身份移除即停止）。</summary>
        public static IEnumerator<float> GuardDebuffs(Player p)
        {
            while (p != null && p.IsConnected && IsScp181(p))
            {
                ClearScpDebuffs(p);
                yield return Timing.WaitForSeconds(0.5f);
            }
        }

        /// <summary>移除玩家 SCP-181 身份（死亡撤离失败/换角/离开）。</summary>
        public static void Remove(Player p)
        {
            if (p == null) return;
            Active.Remove(p.Id);
            ImmortalUntil.Remove(p.Id);
            SurviveLeft.Remove(p.Id);
            Scp181Hints.RemoveRoleIntro(p);
        }

        /// <summary>
        /// 开局自动选人：先 D 级，无 D 则在 观察者(Tutorial)/科学家 中选；
        /// 绝不让 SCP / 设施保安(MTF) 与 混沌 成为 SCP-181。
        /// </summary>
        public static void TrySelectRoundStart()
        {
            if (!Config.AutoSelectOnRoundStart) return;
            int aliveCount = Player.List.Count(x => x.IsConnected && x.IsAlive);
            if (aliveCount <= Config.MinPlayers) return;

            var dPool = Player.List.Where(x => x.IsConnected && x.IsAlive && x.Role.Type == RoleTypeId.ClassD).ToList();
            Player target = null;

            if (dPool.Count > 0)
                target = dPool[UnityEngine.Random.Range(0, dPool.Count)];
            else
            {
                var fallback = Player.List.Where(x => x.IsConnected && x.IsAlive &&
                    (x.Role.Type == RoleTypeId.Tutorial || x.Role.Type == RoleTypeId.Scientist)).ToList();
                if (fallback.Count > 0)
                    target = fallback[UnityEngine.Random.Range(0, fallback.Count)];
            }

            if (target == null)
            {
                Log.Info("[Scp181] 开局没有可选的 D 级/观察者/Scientist，本轮不生成 SCP-181");
                return;
            }

            Assign(target);
        }

        public static void Clear()
        {
            foreach (var pid in Active.Keys.ToList())
            {
                var p = Player.Get(pid);
                if (p != null) Scp181Hints.RemoveRoleIntro(p);
            }
            Active.Clear();
            ImmortalUntil.Clear();
        }
    }
}