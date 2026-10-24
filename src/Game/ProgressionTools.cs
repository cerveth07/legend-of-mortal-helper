using System;
using Mortal.Core;

namespace MortalHelper.Game
{
    // Progression helpers that can be expressed through the game's own stat
    // objects: keeping daily action points above zero and holding affection at
    // each character's own cap. Both only run in free roam, never during story
    // playback, and never write saves themselves.
    internal static class ProgressionTools
    {
        // Old panel's lover candidates, in the same order as the selection grid.
        internal static readonly int[] LoverIds = { 501, 0, 6, 8, 605, 609, 606, 607, 608, 12 };

        internal static void Maintain()
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !Plugin.CanModifyProgress) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Free") return;
            if (plugin.Options.UnlimitedActions.Value) KeepActionPoint();
            if (plugin.Options.MaxAffection.Value) HoldAffection();
        }

        private static void KeepActionPoint()
        {
            try
            {
                var stat = PlayerStatManagerData.Instance.Stats.Get(GameStatType.行動次數);
                if (stat != null && stat.Value < 1) stat.SetValue(1);
            }
            catch (Exception) { }
        }

        private static void HoldAffection()
        {
            try
            {
                foreach (var item in PlayerStatManagerData.Instance.Relationships.List)
                {
                    if (item == null || !item.Active) continue;
                    int maximum = GameAccess.Field<int>(item, "_max");
                    if (maximum > 0 && item.Value < maximum) item.SetValue(maximum);
                }
            }
            catch (Exception) { }
        }

        // The lover is stored as a numeric stat; the value is the character id from
        // RelationshipStatType. Not verified in game yet.
        internal static string SetLover(int index)
        {
            if (index < 0 || index >= LoverIds.Length) return "未选择角色。";
            if (!GameAccess.CanEditValues) return "请在自由活动时，关闭游戏内菜单后修改。";
            try
            {
                var stat = PlayerStatManagerData.Instance.Stats.Get(GameStatType.愛人);
                if (stat == null) return "当前周目没有「爱人」数据。";
                Plugin.Instance.Gate.NotifyMutation();
                stat.SetValue(LoverIds[index]);
                return "已设置心上人；不会补齐结缘事件，正常存档后保留。";
            }
            catch (Exception error) { return "设置失败：" + error.Message; }
        }
    }
}
