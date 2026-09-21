using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using PlayerRoles;
using Scp181.Visuals;
using ServerHandler = Exiled.Events.Handlers.Server;
using PlayerHandler = Exiled.Events.Handlers.Player;

namespace Scp181.Events
{
    public class Scp181Events
    {
        private static Config Config => MainClass.Instance.Config;

        private static readonly HashSet<DoorType> Scp079Doors = new HashSet<DoorType>
        {
            DoorType.Scp079First,
            DoorType.Scp079Second,
            DoorType.Scp079Armory
        };

        private static readonly HashSet<RoleTypeId> NtfRoles = new HashSet<RoleTypeId>
        {
            RoleTypeId.NtfPrivate, RoleTypeId.NtfSergeant,
            RoleTypeId.NtfCaptain, RoleTypeId.NtfSpecialist
        };

        private static readonly HashSet<RoleTypeId> ChaosRoles = new HashSet<RoleTypeId>
        {
            RoleTypeId.ChaosConscript, RoleTypeId.ChaosRifleman,
            RoleTypeId.ChaosMarauder, RoleTypeId.ChaosRepressor
        };

        /// <summary>所有 SCP 阵营角色（对 181 限伤 10）。</summary>
        private static readonly HashSet<RoleTypeId> ScpRoles = new HashSet<RoleTypeId>
        {
            RoleTypeId.Scp049, RoleTypeId.Scp079, RoleTypeId.Scp096,
            RoleTypeId.Scp106, RoleTypeId.Scp173, RoleTypeId.Scp3114,
            RoleTypeId.Scp939, RoleTypeId.Scp0492
        };

        /// <summary>已在 SCP-106 口袋空间获得 1 次逃生机会的玩家。</summary>
        private static readonly HashSet<int> PocketExitUsed = new HashSet<int>();

        private static void ResetFlags()
        {
            PocketExitUsed.Clear();
        }

        public void RegisterEvents()
        {
            ServerHandler.RoundStarted += OnRoundStarted;
            ServerHandler.RoundEnded += OnRoundEnded;
            ServerHandler.RestartingRound += OnRestartingRound;
            PlayerHandler.ChangingRole += OnChangingRole;
            PlayerHandler.Hurting += OnHurting;
            PlayerHandler.Dying += OnDying;
            PlayerHandler.Died += OnDied;
            PlayerHandler.Left += OnLeft;
            PlayerHandler.PickingUpItem += OnPickingUpItem;
            PlayerHandler.InteractingDoor += OnInteractingDoor;
            PlayerHandler.InteractingLocker += OnInteractingLocker;
        }

        public void UnregisterEvents()
        {
            ServerHandler.RoundStarted -= OnRoundStarted;
            ServerHandler.RoundEnded -= OnRoundEnded;
            ServerHandler.RestartingRound -= OnRestartingRound;
            PlayerHandler.ChangingRole -= OnChangingRole;
            PlayerHandler.Hurting -= OnHurting;
            PlayerHandler.Dying -= OnDying;
            PlayerHandler.Died -= OnDied;
            PlayerHandler.Left -= OnLeft;
            PlayerHandler.PickingUpItem -= OnPickingUpItem;
            PlayerHandler.InteractingDoor -= OnInteractingDoor;
            PlayerHandler.InteractingLocker -= OnInteractingLocker;
        }

        // ================= 开局/清理 =================

        private void OnRoundStarted()
        {
            ResetFlags();
            // 稍等确保玩家已生成且角色已分配
            MEC.Timing.CallDelayed(1f, () =>
            {
                try { Scp181Manager.TrySelectRoundStart(); }
                catch (Exception ex) { Log.Error($"[Scp181] 开局选人失败: {ex.Message}"); }
            });
        }

        private void OnRoundEnded(RoundEndedEventArgs ev) { Scp181Manager.Clear(); ResetFlags(); }
        private void OnRestartingRound() { Scp181Manager.Clear(); ResetFlags(); }

        // ================= 撤离保留 / 死亡清除 =================

        private void OnChangingRole(ChangingRoleEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;

            RoleTypeId newRole = ev.NewRole;

            // 撤离为九尾狐 / 混沌：保留身份与全部被动，仅切换角色介绍配色
            if (NtfRoles.Contains(newRole) || ChaosRoles.Contains(newRole))
            {
                Scp181Team team = NtfRoles.Contains(newRole) ? Scp181Team.Ntf : Scp181Team.Chaos;
                Scp181Hints.RemoveRoleIntro(ev.Player);
                Scp181Hints.ShowRoleIntro(ev.Player, team);
                Scp181Manager.ApplyReductionEffects(ev.Player); // 撤离后重新施加 10 级减伤
            }
            // 死亡换角到 Spectator 时不在此刻移除身份，
            // 以保证 OnDied 仍能识别并发送死亡广播；OnDied 结束时会统一清理。
        }

        // ================= 被动②：任何来源 50% 免伤 + 绝境生还 =================

        private void OnHurting(HurtingEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;
            if (ev.Player == null || !ev.Player.IsAlive) return;

            Player victim = ev.Player;

            // 绝境生还后的短暂无敌窗口：完全无效
            if (Scp181Manager.InSurviveImmunity(victim))
            {
                ev.IsAllowed = false;
                Scp181Manager.ClearScpDebuffs(victim);
                return;
            }

            bool hasAttacker = ev.Attacker != null && ev.Attacker != victim;
            RoleTypeId? attackerRole = hasAttacker ? ev.Attacker.Role.Type : (RoleTypeId?)null;

            // 按伤害来源查减免表（Firearm=子弹，或按 SCP 角色名）：0=完全无效，<1 按比例减免
            float multiplier = 1f;
            if (ev.DamageHandler != null)
            {
                string keyword = KindOf(ev.DamageHandler);
                if (Config.DamageReductionTable != null && Config.DamageReductionTable.TryGetValue(keyword, out float m))
                    multiplier = m;
                else if (attackerRole.HasValue && Config.DamageReductionTable.TryGetValue(attackerRole.Value.ToString(), out float m2))
                    multiplier = m2;
            }
            if (multiplier <= 0f)
            {
                ev.IsAllowed = false;
                Scp181Manager.ClearScpDebuffs(victim);
                return;
            }
            if (multiplier < 1f) ev.Amount *= multiplier;

            // 所有 SCP 对 181 的限伤仅为 10（无论是否触发免伤）
            if (attackerRole.HasValue && ScpRoles.Contains(attackerRole.Value) && ev.Amount > 10f)
                ev.Amount = 10f;

            // 持续伤害(DOT 流血/腐蚀)：SCP-181 不承受此类伤害
            if (IsDotDamage(ev.DamageHandler))
            {
                Scp181Manager.ClearScpDebuffs(victim);
                ev.IsAllowed = false;
                return;
            }

            // 50% 免伤（任何来源）
            if (UnityEngine.Random.value < Config.DodgeChance)
            {
                ev.IsAllowed = false;
                Scp181Manager.ClearScpDebuffs(victim);
                if (hasAttacker)
                    Scp181Hints.ShowDodgeMsg(ev.Attacker);
                return;
            }

            // 免伤未触发：若本次伤害会致命，则消耗一次绝境生还（用完即无）
            if (ev.Amount >= victim.Health && Scp181Manager.TryUseSurvive(victim))
            {
                ev.IsAllowed = false;
                victim.Health = 1f;
                Scp181Manager.GrantSurviveImmunity(victim, Config.SurviveImmunitySeconds);
                Scp181Hints.ShowSurviveMsg(victim);
            }
        }

        /// <summary>是否 SCP-106 口袋空间致命伤害（按运行时类型名匹配，避免依赖内部类型）。</summary>
        private static bool IsPocketDamage(PlayerStatsSystem.DamageHandlerBase handler)
            => handler != null && handler.GetType().Name.Contains("PocketDimension");

        /// <summary>是否流血/腐蚀类持续伤害。</summary>
        private static bool IsDotDamage(PlayerStatsSystem.DamageHandlerBase handler)
            => handler != null &&
               (handler.GetType().Name.Contains("Bleeding") || handler.GetType().Name.Contains("Corroding"));

        /// <summary>伤害来源关键词（Firearm=枪械，其余返回处理类型名）。</summary>
        private static string KindOf(PlayerStatsSystem.DamageHandlerBase handler)
        {
            string name = handler.GetType().Name;
            return name.Contains("Firearm") ? "Firearm" : name;
        }

        // ================= SCP-106 口袋空间：181 必有一次出去的机会 =================

        private void OnDying(DyingEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;

            // 仅在口袋空间致死且尚未用过逃生机会时，给一次活着出去的机会
            if (IsPocketDamage(ev.DamageHandler) && !PocketExitUsed.Contains(ev.Player.Id))
            {
                PocketExitUsed.Add(ev.Player.Id);
                ev.IsAllowed = false;
                Scp181Manager.ClearScpDebuffs(ev.Player);
                EscapePocket(ev.Player);
            }
        }

        private static void EscapePocket(Player p)
        {
            try
            {
                var zoneRooms = Room.List
                    .Where(r => r != null && r.GameObject != null && r.Zone != ZoneType.Surface)
                    .ToList();
                Room target = zoneRooms.Count > 0
                    ? zoneRooms[UnityEngine.Random.Range(0, zoneRooms.Count)]
                    : Room.List.FirstOrDefault();
                if (target != null)
                    p.Teleport(target.GameObject.transform.position + new UnityEngine.Vector3(0f, 0.5f, 0f));
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] 口袋逃生传送失败: {ex.Message}");
            }
        }

        // ================= 死亡广播 =================

        private void OnDied(DiedEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;

            try
            {
                // 公告里显示的是“杀死 181 的那个人”的名字
                string killerName = ev.Attacker != null ? ev.Attacker.Nickname : "未知";
                if (Config.DiedCassieEnable)
                    Exiled.API.Features.Cassie.MessageTranslated(Config.CassieTransmission, Config.CassieSubtitles, false, true, true);

                string announce = Config.DeathAnnounce.Replace("{name}", killerName);
                Map.Broadcast((ushort)Config.DeathAnnounceSeconds, announce);
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] 死亡广播失败: {ex.Message}");
            }

            // 死亡后清除身份与临时提示（OverDied 已在 OnDied 中统一清理）
            Scp181Hints.ClearTransient(ev.Player);
            Scp181Manager.Remove(ev.Player);
        }

        private void OnLeft(LeftEventArgs ev)
        {
            Scp181Hints.ClearTransient(ev.Player);
            Scp181Manager.Remove(ev.Player);
        }

        // ================= 被动①：拾取物品 30% 复制一份 =================

        private void OnPickingUpItem(PickingUpItemEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;
            if (Config.Debug)
                Log.Info($"[Scp181] {ev.Player.Nickname} 拾取了 {ev.Pickup.Info.ItemId}");

            if (UnityEngine.Random.value >= Config.CopyChance) return;

            try
            {
                ItemType type = ev.Pickup.Info.ItemId;
                // 只复制普通物品；背包满时 AddItem 会失败，静默忽略
                ev.Player.AddItem(type);
                Scp181Hints.ShowCopyMsg(ev.Player); // 复制成功时在屏幕下方提示
            }
            catch { }
        }

        // ================= 被动③：30% 开启权限门（排除 079 门/被锁门）=================

        private void OnInteractingDoor(InteractingDoorEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;
            if (ev.Door == null) return;

            // 排除 SCP-079 的门 或 已被 079 锁定的门
            if (Scp079Doors.Contains(ev.Door.Type) || ev.Door.IsLocked)
                return;

            if (UnityEngine.Random.value < Config.UnlockChance)
            {
                ev.IsAllowed = true;
                if (Config.Debug)
                    Log.Info($"[Scp181] {ev.Player.Nickname} 幸运地开启了门 {ev.Door.Name}");
            }
        }

        // ================= 被动③b：30% 开启 SCP 物品柜 =================

        private void OnInteractingLocker(InteractingLockerEventArgs ev)
        {
            if (!Scp181Manager.IsScp181(ev.Player)) return;

            if (UnityEngine.Random.value < Config.UnlockChance)
                ev.IsAllowed = true;
        }
    }
}