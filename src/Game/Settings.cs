using BepInEx.Configuration;
using UnityEngine;

namespace MortalHelper.Game
{
    internal sealed class Settings
    {
        public readonly ConfigEntry<bool> Enabled, Hints, ShowEffects, ShowConditions, ShowBranches;
        public readonly ConfigEntry<float> Opacity, BadgeX, BadgeY;
        public readonly ConfigEntry<bool> DuelHealth, DuelStamina, DuelDodge, DuelDamage;
        public readonly ConfigEntry<bool> GroupHealth, GroupMana, GroupCooldown, GroupFreeze, GroupDown;
        public readonly ConfigEntry<bool> UnlimitedActions, MaxAffection, SkillFree, ForgeFree, FastSkill, FastForge;
        public readonly ConfigEntry<string> Point;
        public readonly ConfigEntry<int> Mode;
        public readonly ConfigEntry<KeyCode> Toggle;
        public readonly ConfigEntry<float> X, Y, Width, Height;
        public readonly ConfigEntry<float> Speed;
        public readonly ConfigEntry<int> SlotCount;
        public readonly ConfigEntry<bool> SortRecent;
        internal readonly ConfigFile Config;

        public Settings(ConfigFile config)
        {
            Config = config;
            Enabled = config.Bind("General", "Enabled", true, "启用本插件辅助；不会撤销已应用的修改。");
            Toggle = config.Bind("General", "PanelKey", KeyCode.Tab, "展开或收起面板。");
            Hints = config.Bind("Story", "EffectHints", false, "选项悬停提示总开关；关闭时保留两个子项偏好。");
            ShowEffects = config.Bind("Story", "ShowEffects", false, "悬停选项时显示可靠的选择效果，包含剧透。");
            ShowConditions = config.Bind("Story", "ShowConditions", false, "悬停选项时显示已确认的原生选择条件，包含剧透。");
            ShowBranches = config.Bind("Story", "ShowBranches", false, "在选项上标记〔已走〕〔未记录〕，记录保存在配置目录的 cerveth.mortal.helper.choices.txt。");
            Point = config.Bind("Story", "BasePoint", "50", "指定点数模式下的基础点数；按每次判定范围校验。");
            Mode = config.Bind("Story", "Mode", 0, new ConfigDescription("0 自然掷骰；1 指定点数；2 直接选项。", new AcceptableValueRange<int>(0, 2)));
            DuelHealth = config.Bind("Duel", "KeepHealth", false, "单挑主角生命不因普通扣减降低。");
            DuelStamina = config.Bind("Duel", "KeepStamina", false, "单挑主角气力不因普通扣减降低。");
            DuelDodge = config.Bind("Duel", "MaximumDodge", false, "单挑主角闪避率设为最高；保留必中机制。");
            DuelDamage = config.Bind("Duel", "LethalDamage", false, "对敌方有效伤害提升至当前生命；保留剧情保底。");
            GroupHealth = config.Bind("GroupBattle", "KeepHealth", false, "群战主角生命不因普通伤害降低；仍可能硬直。");
            GroupMana = config.Bind("GroupBattle", "FullMana", false, "群战主角气力维持满值。");
            GroupCooldown = config.Bind("GroupBattle", "InstantCooldown", false, "群战技能栏不受冷却限制；保留原生施放条件。");
            GroupFreeze = config.Bind("GroupBattle", "FreezeTime", false, "群战剩余时间维持最大值；靠倒计时归零获胜的关卡需关闭。");
            GroupDown = config.Bind("GroupBattle", "DownOnHit", false, "群战敌方受到有效伤害即倒地；保留闪避与剧情无敌。");
            UnlimitedActions = config.Bind("Progression", "UnlimitedActions", false, "日常行动点至少为 1 且不减；日期不会因行动用完自动推进。");
            MaxAffection = config.Bind("Progression", "MaxAffection", false, "已登场角色维持各自好感上限；不解锁未登场角色。");
            SkillFree = config.Bind("Progression", "SkillFree", false, "练功不消耗武学点数；保留武功修炼条件。");
            ForgeFree = config.Bind("Progression", "ForgeFree", false, "锻造炼丹不扣点数与金钱；不解锁研发条件。");
            FastSkill = config.Bind("Progression", "FastSkill", false, "每次练功完成当前一级的剩余进度。");
            FastForge = config.Bind("Progression", "FastForge", false, "每次锻造炼丹完成当前一级的剩余进度。");
            X = config.Bind("Window", "X", 36f, "窗口左边距。");
            Y = config.Bind("Window", "Y", 64f, "窗口上边距。");
            Width = config.Bind("Window", "Width", 640f, "窗口宽度。");
            Height = config.Bind("Window", "Height", 580f, "窗口高度。");
            Opacity = config.Bind("Window", "Opacity", 0.85f,
                new ConfigDescription("面板背景透明度，范围 40%～100%。", new AcceptableValueRange<float>(0.4f, 1f)));
            BadgeX = config.Bind("Window", "BadgeX", 12f, "收起徽章左上角 X 坐标。");
            BadgeY = config.Bind("Window", "BadgeY", 12f, "收起徽章左上角 Y 坐标。");
            SlotCount = config.Bind("Save", "SlotCount", 0,
                new ConfigDescription("存档格数 2～999；0 表示使用游戏默认。重新打开存档/读档界面后生效。", new AcceptableValueRange<int>(0, 999)));
            SortRecent = config.Bind("Save", "SortRecent", false, "读档列表按最近存档时间排序；游戏中按 F3 切换。");
            Speed = config.Bind("Edits", "GameSpeed", 1f,
                new ConfigDescription("游戏速度倍率，1 为默认。原生暂停与慢动作由游戏自己管理。",
                    new AcceptableValueRange<float>(0.25f, 5f)));
        }
    }
}
