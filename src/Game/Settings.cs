using BepInEx.Configuration;
using UnityEngine;

namespace MortalHelper.Game
{
    internal sealed class Settings
    {
        public readonly ConfigEntry<bool> ExperimentalRecovery;
        public readonly ConfigEntry<bool> Enabled, Hints, ShowEffects, ShowConditions, ShowBranches, DiagnoseScript;
        public readonly ConfigEntry<float> Opacity, BadgeX, BadgeY;
        public readonly ConfigEntry<bool> DuelHealth, DuelStamina, DuelDodge, DuelDamage, DuelZeroDamage, DuelAlwaysHit, DuelFirstStrike;
        public readonly ConfigEntry<bool> GroupHealth, GroupAllyHealth, GroupMana, GroupCooldown, GroupFreeze, GroupDown;
        public readonly ConfigEntry<bool> UnlimitedActions, MaxAffection, SkillFree, ForgeFree, FastSkill, FastForge, PracticeLimit, CheckpointLog;
        public readonly ConfigEntry<string> Point;
        public readonly ConfigEntry<KeyCode> Toggle;
        public readonly ConfigEntry<float> X, Y, Width, Height;
        public readonly ConfigEntry<float> Speed;
        public readonly ConfigEntry<int> SlotCount;
        public readonly ConfigEntry<bool> SortRecent, AnytimeSave;
        public readonly ConfigEntry<bool> TraditionalChinese;
#if !NO_PROBE
        public readonly ConfigEntry<bool> SelfTest;
#endif
    public readonly ConfigEntry<bool> AutoTour;
    public readonly ConfigEntry<bool> PreviewEditable;
        internal readonly ConfigFile Config;

        public Settings(ConfigFile config)
        {
            Config = config;
            Enabled = config.Bind("General", "Enabled", true, "启用本插件辅助；不会撤销已应用的修改。");
            ExperimentalRecovery = config.Bind("Recovery", "ExperimentalEnabled", false, "高级测试：显示并启用精确恢复入口，默认隐藏。实验恢复尚未通过完整游戏重放验收，仅限备份后的测试进度；随机状态仍可能导致严格校验失败。");
            Toggle = config.Bind("General", "PanelKey", KeyCode.Tab, "展开或收起面板。");
            Hints = config.Bind("Story", "EffectHints", false, "选项悬停提示总开关；关闭时保留两个子项偏好。");
            ShowEffects = config.Bind("Story", "ShowEffects", false, "悬停选项时显示可靠的选择效果，包含剧透。");
            ShowConditions = config.Bind("Story", "ShowConditions", false, "悬停选项时显示选项解锁条件与已识别的后续剧情，包含剧透。");
            DiagnoseScript = config.Bind("Diagnostics", "DiagnoseScript", false, "诊断：切换剧本时在状态行显示脚本名与可解析分支数（无面板入口）。");
            ShowBranches = config.Bind("Story", "ShowBranches", false, "在选项上标记〔已走〕〔未记录〕，记录保存在配置目录的 cerveth.mortal.helper.choices.txt。");
            Point = config.Bind("Story", "BasePoint", "", "指定点数模式下的基础点数；按每次判定范围校验。");
            DuelHealth = config.Bind("Duel", "KeepHealth", false, "单挑主角生命不因普通扣减降低。");
            DuelStamina = config.Bind("Duel", "KeepStamina", false, "单挑主角气力不因普通扣减降低。");
            DuelDodge = config.Bind("Duel", "MaximumDodge", false, "单挑主角闪避率设为最高；保留必中机制。");
            DuelDamage = config.Bind("Duel", "LethalDamage", false, "对敌方有效伤害提升至当前生命；保留剧情保底。");
            DuelZeroDamage = config.Bind("Duel", "ZeroDamage", false, "敌方受到的伤害归零（含持续伤害）；与一击必杀互斥。");
            DuelAlwaysHit = config.Bind("Duel", "AlwaysHit", false, "敌方基础闪避失效，主角攻击不会落空；一击必杀已自动包含。");
            DuelFirstStrike = config.Bind("Duel", "FirstStrike", false, "行动对比必定主角成功、敌方失败；一击必杀已自动包含。");
            GroupHealth = config.Bind("GroupBattle", "KeepHealth", false, "群战主角生命不因普通伤害降低；仍可能硬直。");
            GroupAllyHealth = config.Bind("GroupBattle", "KeepAllyHealth", false, "群战友军生命不因扣血降低；不影响主角、敌军与中立单位，剧情退场保留。");
            GroupMana = config.Bind("GroupBattle", "FullMana", false, "群战主角气力维持满值。");
            GroupCooldown = config.Bind("GroupBattle", "InstantCooldown", false, "群战技能栏不受冷却限制；保留原生施放条件。");
            GroupFreeze = config.Bind("GroupBattle", "FreezeTime", false, "冻结群战剩余时间；保留战斗事件进度，靠倒计时归零获胜的关卡需关闭。");
            GroupDown = config.Bind("GroupBattle", "DownOnHit", false, "群战敌方受到有效伤害即倒地；保留闪避与剧情无敌。");
            UnlimitedActions = config.Bind("Progression", "UnlimitedActions", false, "日常行动点至少为 1 且不减；日期不会因行动用完自动推进。");
            MaxAffection = config.Bind("Progression", "MaxAffection", false, "已登场角色维持各自好感上限；不解锁未登场角色。");
            SkillFree = config.Bind("Progression", "SkillFree", false, "练功不消耗武学点数；保留武功修炼条件。");
            ForgeFree = config.Bind("Progression", "ForgeFree", false, "锻造炼丹不扣点数与金钱；不解锁研发条件。");
            FastSkill = config.Bind("Progression", "FastSkill", false, "每次练功完成当前一级的剩余进度。");
            FastForge = config.Bind("Progression", "FastForge", false, "每次锻造炼丹完成当前一级的剩余进度。");
            PracticeLimit = config.Bind("Edits", "PracticeLimit", false, "解除修炼点数消耗上限；仍按现有武学点数扣减。");
            CheckpointLog = config.Bind("Story", "CheckpointLog", false, "在提示区显示变化的剧情标记及其数值。");
            X = config.Bind("Window", "X", 36f, "窗口左边距。");
            Y = config.Bind("Window", "Y", 64f, "窗口上边距。");
            Width = config.Bind("Window", "Width", 640f, "窗口宽度。");
            Height = config.Bind("Window", "Height", 600f, "窗口高度。");
            Opacity = config.Bind("Window", "Opacity", 0.85f,
                new ConfigDescription("面板背景透明度，范围 40%～100%。", new AcceptableValueRange<float>(0.4f, 1f)));
            BadgeX = config.Bind("Window", "BadgeX", 12f, "收起徽章左上角 X 坐标。");
            BadgeY = config.Bind("Window", "BadgeY", 12f, "收起徽章左上角 Y 坐标。");
            SlotCount = config.Bind("Save", "SlotCount", 0,
                new ConfigDescription("存档格数 2～999；0 表示使用游戏默认。重新打开存档/读档界面后生效。", new AcceptableValueRange<int>(0, 999)));
            SortRecent = config.Bind("Save", "SortRecent", false, "读档列表按最近存档时间排序；游戏中按 F3 切换。");
            AnytimeSave = config.Bind("Save", "AnytimeSave", false, "允许在剧情对话、选项、骰子稳定等待页或单挑行动点使用原生编号槽存档；实验功能，默认关闭。");
            TraditionalChinese = config.Bind("Window", "TraditionalChinese", false, "面板使用繁體中文；关闭时使用简体中文。");
#if !NO_PROBE
            SelfTest = config.Bind("Diagnostics", "SelfTest", false, "只读检测：记录当前场景与 API 状态，不读档、不改进度或配置。");
#endif
        AutoTour = config.Bind("Diagnostics", "AutoTour", false, "自检巡视：打开面板后每 1.6 秒切一页并截图到配置目录 mh\\tour-*.png，用于实机校验布局。");
        PreviewEditable = config.Bind("Diagnostics", "PreviewEditable", false, "只读预览：把数值/物品/好感行按可编辑宽度绘制但不写入，供截图核对布局。");
            // Enforce exclusivity for both UI changes and live config reloads.
            DuelDamage.SettingChanged += (sender, args) => { if (DuelDamage.Value) DuelZeroDamage.Value = false; };
            DuelZeroDamage.SettingChanged += (sender, args) => { if (DuelZeroDamage.Value) DuelDamage.Value = false; };
            if (DuelDamage.Value && DuelZeroDamage.Value) DuelZeroDamage.Value = false;
            Speed = config.Bind("Edits", "GameSpeed", 1f,
                new ConfigDescription("游戏速度倍率，1 为默认。原生暂停与慢动作由游戏自己管理。",
                    new AcceptableValueRange<float>(0.25f, 5f)));
        }
    }
}
