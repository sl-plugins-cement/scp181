using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using Exiled.API.Features;

namespace Scp181.Commands
{
    /// <summary>
    /// Remote Admin management command. Requires the PlayersManagement permission, so an operator
    /// with unrelated RA access cannot hand the role out.
    ///   scp181 set {name/id} - make the given player SCP-181 (the previous one is released first)
    ///   scp181 clear         - release the current SCP-181
    ///   scp181 status        - show who is currently SCP-181
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
                        response = "已清除当前 SCP-181";
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

            Player target = Player.Get(ids[0].TrimStart('@'));
            if (target == null || !target.IsConnected)
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
                response = "无法指派：目标已有增援特殊身份，或增援身份分配尚未就绪。";
                return false;
            }
            response = $"已将玩家 {target.Nickname} 设置为 SCP-181";
            return true;
        }

        private static string Status()
        {
            Player current = Player.List.FirstOrDefault(Scp181Manager.IsScp181);
            return current == null
                ? "当前没有 SCP-181"
                : $"当前 SCP-181: {current.Nickname} ({current.UserId})";
        }
    }
}
