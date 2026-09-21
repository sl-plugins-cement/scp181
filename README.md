# SCP-181（幸运儿）

SCP:SL 角色插件：开局在服务器随机挑选一名玩家扮演 **SCP-181**（D 级人员），拥有「超高免伤 + 物品复制 + 概率开门」等逆天被动，目标是尽力在设施里苟活下去。

- 框架：EXILED / LabAPI
- HUD：HintServiceMeow (HSM)，不可用时自动回退原生 `ShowHint`
- 数据来源：[SCP-181 - 幸运儿](https://scp-wiki-cn.wikidot.mer.run/scp-181)

## 功能一览

### 选人
- 开局玩家数 **> 5** 时随机挑选一名玩家；优先 D 级，其次观察者/科学家，**绝不让 SCP / 设施保安(MTF) / 混沌** 扮演。
- 指令 `scp181 set <玩家名/ID>` 手动指派，`scp181 set` 无参可查看用法。

### 被动技能
1. **物品复制（10%）**：拾取物品有 `CopyChance=0.1` 概率复制一份到背包；复制成功时在屏幕下方提示 `[SCP-181] 运气不错! 物品数量+1`。
2. **50% 免伤**：任何攻击有 `DodgeChance=0.5` 概率完全失效，攻击者屏幕中心偏下显示 `[N]对方是SCP-181,免疫了你的伤害嘻嘻(^_^)`（5 秒倒计时）。
3. **概率开门（30%）**：直接开启权限门（排除 SCP-079 的门与被 079 锁定的门）以及 SCP 物品柜。
4. **撤离保留**：撤离变身为九尾狐或混沌后仍保留全部被动；角色介绍标题配色相应变化（九尾=蓝 `#4DA6FF`，混沌=深绿 `#1E6B3A`）。
5. **绝境生还**：受致命伤害时消耗 `SurviveChances=1` 次机会以 1 血存活并获得短暂免伤；次数用完即无。
6. **伤害减免表**：`DamageReductionTable` 可配置任意伤害来源的减免比例（子弹默认只留 `0.1`）。
7. **SCP 限伤 + debuff 免疫**：所有 SCP 对 181 单次伤害封顶 10；持续免疫 SCP 的 debuff（049 心脏骤停 / 106 腐蚀流血 / 939 毒 / 缠绕 / 脑震荡等）；凹陷入口下巴(SIP) 口袋空间首次必有一次逃生机会。

### 死亡广播
- 死亡时 Cassie TTS 朗读 `SCP-181 HAS BEEN CONTAINED SUCCESSFULLY`。
- 全体公告：`[SCP181]已被重新收容，收容大蛇[玩家昵称]`（显示**击杀者**的名字）。

## 配置

主要配置项（均可在 EXILED 配置文件中调整）：

| 选项 | 默认 | 说明 |
|------|------|------|
| `CopyChance` | `0.1` | 复制物品概率（0-1） |
| `DodgeChance` | `0.5` | 任意攻击免伤概率 |
| `DamageReductionTable` | `Firearm=0.1` | 伤害来源→保留伤害比例 |
| `UnlockChance` | `0.3` | 权限门/SCP 柜开门概率 |
| `SurviveChances` | `1` | 绝境生还可用次数 |
| `SurviveImmunitySeconds` | `1.5` | 生还后免伤秒数 |
| `ScpColor` / `NtfColor` / `ChaosColor` | 橙/蓝/深绿 | 角色介绍标题配色 |
| `RoleIntroY` / `DodgeMsgY` / `SurviveMsgY` | 900/800/780 | HSM 屏幕 Y 坐标 |
| `CassieTransmission` / `DeathAnnounce` | … | 死亡广播文案 |

## 已知 Bug

- **SCP-173（花生）攻击 SCP-181 时异常**：会出现「一掐就死」或「完全没有伤害」两种现象。
  - 可能原因：SCP-173 的**瞬间拧脖处死**机制可能绕过 `Hurting` 伤害事件，导致限伤 10 的逻辑无法可靠覆盖；同时减伤效果与伤害上限在部分情况下互相干扰。
  - 当前仅通过 `Hurting` 事件拦截，无法完整覆盖该情况，**待后续修复**。

## 构建与部署

1. `dotnet build -c Release`（或 IDE 发布）。
2. 将 `bin/Release/Scp181.dll` 放入服务器 EXILED 的 `plugins` 目录。
3. 重启服务器，按需在 `EXILED/Configs` 中调整配置。