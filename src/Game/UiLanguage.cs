using System;

namespace MortalHelper.Game
{
    internal enum UiLang { Hans, Hant, Ja, Ko }

    // UI-only character pairs selected and reordered from OpenCC s2t/t2s data for this panel.
    // Game identifiers and player-authored data are never rewritten in storage.
    // Japanese and Korean are whole-string translations kept in UiText; anything that
    // has not been translated yet is shown in the original Chinese, never half-converted.
    internal static class UiLanguage
    {
        internal static UiLang Parse(string code)
        {
            if (string.Equals(code, "zh-Hant", StringComparison.OrdinalIgnoreCase)) return UiLang.Hant;
            if (string.Equals(code, "ja", StringComparison.OrdinalIgnoreCase)) return UiLang.Ja;
            if (string.Equals(code, "ko", StringComparison.OrdinalIgnoreCase)) return UiLang.Ko;
            return UiLang.Hans;
        }

        internal static string Code(UiLang language)
        {
            if (language == UiLang.Hant) return "zh-Hant";
            if (language == UiLang.Ja) return "ja";
            if (language == UiLang.Ko) return "ko";
            return "zh-Hans";
        }

        internal static string Translate(string text, UiLang language)
        {
            if (string.IsNullOrEmpty(text) || language == UiLang.Hans) return text;
            if (language == UiLang.Hant) return Convert(text, true);
            string translated = UiText.Lookup(text, language);
            return translated ?? text;
        }

        private const string SimplifiedToTraditionalSource = "万与两严个为书于云产仅从会伤体余侠侧兰关养内写军决冻减击则创删别剑剧务动励区单卫却历发变叶号后启响围图场声处备复奖学实宽对导将属师带并库应开强归当录态总恋悬戏战护择换据掷敌数斗断无旧时显暂机杀杂权条标栏档检气没测游满点炼爱状献现盖础确称筛简类级线练组经结绘给绝绪续维编缘群胜脚范药获萤补规视览计认记许设证识诊试该语误请读负贡败贵资赋赢软轻载较辅辑输边达过运还这进选郁里钱银销锁错键锻长门闪闭问闲间阅阳阴际随页项顺须预验齐龙洁纯样";
        private const string SimplifiedToTraditionalTarget = "萬與兩嚴個為書於雲產僅從會傷體餘俠側蘭關養內寫軍決凍減擊則創刪別劍劇務動勵區單衛卻歷發變葉號後啓響圍圖場聲處備復獎學實寬對導將屬師帶並庫應開強歸當錄態總戀懸戲戰護擇換據擲敵數鬥斷無舊時顯暫機殺雜權條標欄檔檢氣沒測遊滿點煉愛狀獻現蓋礎確稱篩簡類級線練組經結繪給絕緒續維編緣群勝腳範藥獲螢補規視覽計認記許設證識診試該語誤請讀負貢敗貴資賦贏軟輕載較輔輯輸邊達過運還這進選郁裏錢銀銷鎖錯鍵鍛長門閃閉問閒間閱陽陰際隨頁項順須預驗齊龍潔純樣";
        private const string TraditionalToSimplifiedSource = "並俠個側備傷僅內兩凍別刪則創劇劍動務勝勵區卻問啓單嚴圍圖場學實寫寬將對導屬師帶庫強後從復愛態應懸戀戰戲換擇擊據擲敗敵數斷於時暫書會條標機檔檢欄權歷歸殺氣決沒減測滿無煉爲狀獎獲獻現產當發確礎稱範篩簡級組結絕給經維緒線緣編練總繪續羣聲腳與舊萬葉蓋藥蘭處號螢衛裏補規視覽計記設許診試該認語誤請證識護讀變負貢貴資賦贏軍軟較載輔輕輯輸這進遊運過達選還邊銀銷錄錢錯鍛鍵鎖長門閃閉開閒間閱關陰陽際隨雜雲響頁項順須預類顯養餘驗體鬥鬱點齊龍為潔純樣";
        private const string TraditionalToSimplifiedTarget = "并侠个侧备伤仅内两冻别删则创剧剑动务胜励区却问启单严围图场学实写宽将对导属师带库强后从复爱态应悬恋战戏换择击据掷败敌数断于时暂书会条标机档检栏权历归杀气决没减测满无炼为状奖获献现产当发确础称范筛简级组结绝给经维绪线缘编练总绘续群声脚与旧万叶盖药兰处号萤卫里补规视览计记设许诊试该认语误请证识护读变负贡贵资赋赢军软较载辅轻辑输这进游运过达选还边银销录钱错锻键锁长门闪闭开闲间阅关阴阳际随杂云响页项顺须预类显养余验体斗郁点齐龙为洁纯样";

        internal static string Convert(string text, bool traditional)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string source = traditional ? SimplifiedToTraditionalSource : TraditionalToSimplifiedSource;
            string target = traditional ? SimplifiedToTraditionalTarget : TraditionalToSimplifiedTarget;
            char[] converted = null;
            for (int i = 0; i < text.Length; i++)
            {
                int index = source.IndexOf(text[i]);
                if (index < 0) continue;
                if (converted == null) converted = text.ToCharArray();
                converted[i] = target[index];
            }
            return converted == null ? text : new string(converted);
        }
    }
}

