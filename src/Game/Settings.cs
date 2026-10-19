using BepInEx.Configuration;
using UnityEngine;

namespace MortalHelper.Game
{
    internal sealed class Settings
    {
        public readonly ConfigEntry<bool> Enabled, Dice, Hints;
        public readonly ConfigEntry<bool> DuelHealth, DuelStamina, DuelDodge, DuelDamage;
        public readonly ConfigEntry<bool> GroupHealth, GroupMana, GroupCooldown, GroupFreeze, GroupDown;
        public readonly ConfigEntry<string> Point;
        public readonly ConfigEntry<KeyCode> Toggle;
        public readonly ConfigEntry<float> X, Y, Width, Height;
        public readonly ConfigEntry<float> Speed;

        public Settings(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "启用本插件辅助；不会撤销已应用的修改。");
            Toggle = config.Bind("General", "PanelKey", KeyCode.Tab, "展开或收起面板。");
            Dice = config.Bind("Story", "DiceControls", true, "在原生判定页显示点数输入和直接选项。");
            Hints = config.Bind("Story", "EffectHints", false, "显示可确认的脚本效果（包含剧透）。");
            Point = config.Bind("Story", "BasePoint", "", "空白为自然掷骰；整数按每次范围校验。");
            DuelHealth = config.Bind("Duel", "KeepHealth", false, "单挑主角生命不因普通扣减降低。");
            DuelStamina = config.Bind("Duel", "KeepStamina", false, "单挑主角气力不因普通扣减降低。");
            DuelDodge = config.Bind("Duel", "MaximumDodge", false, "单挑主角闪避率设为最高；保留必中机制。");
            DuelDamage = config.Bind("Duel", "LethalDamage", false, "对敌方有效伤害提升至当前生命；保留剧情保底。");
            GroupHealth = config.Bind("GroupBattle", "KeepHealth", false, "群战主角生命不因普通伤害降低；仍可能硬直。");
            GroupMana = config.Bind("GroupBattle", "FullMana", false, "群战主角气力维持满值。");
            GroupCooldown = config.Bind("GroupBattle", "InstantCooldown", false, "群战技能栏不受冷却限制；保留原生施放条件。");
            GroupFreeze = config.Bind("GroupBattle", "FreezeTime", false, "群战剩余时间维持最大值；靠倒计时归零获胜的关卡需关闭。");
            GroupDown = config.Bind("GroupBattle", "DownOnHit", false, "群战敌方受到有效伤害即倒地；保留闪避与剧情无敌。");
            X = config.Bind("Window", "X", 36f, "窗口左边距。");
            Y = config.Bind("Window", "Y", 64f, "窗口上边距。");
            Width = config.Bind("Window", "Width", 640f, "窗口宽度。");
            Height = config.Bind("Window", "Height", 580f, "窗口高度。");
            Speed = config.Bind("Edits", "GameSpeed", 1f,
                new ConfigDescription("游戏速度倍率，1 为默认。原生暂停与慢动作由游戏自己管理。",
                    new AcceptableValueRange<float>(0.25f, 5f)));
        }
    }
}
