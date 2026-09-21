using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using Exiled.API.Features;

namespace Scp181.Commands
{
    /// <summary>
    /// SCP-181 管理指令：
    /// scp181 set {玩家名/ID} — 将指定玩家设为 SCP-181（先解除现任 SCP-181）。
    /// scp181 clear             — 清除当前 SCP-181。
    /// scp181 status            — 查看当前 SCP-181。
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
            response = "执行完成";
            try
            {
                var args = arguments.Array?.Skip(arguments.Offset).Take(arguments.Count).ToList() ?? new List<string>();
                if (args.Count == 0)
                {
                    response = "用法：" + string.Join(" / ", Usage);
                    return false;
                }

                switch (args[0].ToLower())
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
                response = "执行异常：" + ex.Message;
                return false;
            }
        }

        private bool Set(List<string> ids, out string response)
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

            // 先解除现任 SCP-181（若为同一人则不动）
            var current = Player.List.FirstOrDefault(x => Scp181Manager.IsScp181(x));
            if (current != null && current.Id != target.Id)
                Scp181Manager.Remove(current);

            Scp181Manager.Assign(target);
            response = $"已将玩家 {target.Nickname} 设置为 SCP-181";
            return true;
        }

        private string Status()
        {
            var current = Player.List.FirstOrDefault(x => Scp181Manager.IsScp181(x));
            if (current == null)
                return "当前没有 SCP-181";
            return $"当前 SCP-181: {current.Nickname} ({current.UserId})";
        }
    }
}