using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using LabApi.Features.Wrappers;
using Log = LabApi.Features.Console.Logger;

namespace Scp181.Commands
{
    /// <summary>
    /// Remote Admin management command. Requires the PlayersManagement permission, so an operator
    /// with unrelated RA access cannot hand the role out.
    ///   scp181 set {name/id} - make the given player SCP-181 without replacing anyone else
    ///   scp181 clear         - release all SCP-181 players
    ///   scp181 status        - list all current SCP-181 players
    /// </summary>
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class Scp181Command : ICommand
    {
        public string Command => "scp181";

        public string[] Aliases => Array.Empty<string>();

        public string Description => "SCP-181 角色管理指令";

        public string[] Usage => new[] { "set {玩家名/ID}", "clear", "status" };

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (MainClass.Instance == null)
            {
                response = "SCP-181 插件未启用";
                return false;
            }

            if (!sender.CheckPermission(PlayerPermissions.PlayersManagement, out response))
                return false;

            try
            {
                List<string> args = arguments.Array?.Skip(arguments.Offset).Take(arguments.Count).ToList() ?? new List<string>();
                if (args.Count == 0)
                {
                    response = "用法：" + string.Join(" / ", Usage);
                    return false;
                }

                switch (args[0].ToLowerInvariant())
                {
                    case "set":
                        return Set(args.Skip(1).ToList(), out response);

                    case "clear":
                        Scp181Manager.Clear();
                        response = "已清除所有 SCP-181";
                        return true;

                    case "status":
                        response = Status();
                        return true;

                    default:
                        response = "未知子指令：" + args[0];
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181] Command failed: {ex}");
                response = "执行异常：" + ex.Message;
                return false;
            }
        }

        private static bool Set(List<string> ids, out string response)
        {
            if (ids.Count == 0)
            {
                response = "用法：scp181 set {玩家名/ID}";
                return false;
            }

            string query = string.Join(" ", ids).TrimStart('@');
            Player? target = int.TryParse(query, out int id)
                ? Player.Get(id)
                : Player.Get(query) ?? Player.GetByNickname(query);
            if (target == null || target.IsDestroyed)
            {
                response = $"找不到玩家：{string.Join(" ", ids)}";
                return false;
            }

            if (!target.IsAlive)
            {
                response = $"玩家 {target.Nickname} 当前不是存活状态";
                return false;
            }

            if (!Scp181Manager.TryAssign(target))
            {
                response = "无法指派：目标已有增援特殊身份、增援分配未就绪，或角色转换被阻止。";
                return false;
            }
            response = $"已将玩家 {target.Nickname} 设置为 SCP-181";
            return true;
        }

        private static string Status()
        {
            List<Player> current = Player.List.Where(Scp181Manager.IsScp181).OrderBy(p => p.PlayerId).ToList();
            return current.Count == 0
                ? "当前没有 SCP-181"
                : $"当前 SCP-181（{current.Count} 人）：\n" + string.Join("\n",
                    current.Select(p => $"{p.Nickname} (ID: {p.PlayerId}, {p.UserId})"));
        }
    }
}
