using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class LanguageDetectionService : ILanguageDetectionService
{
    private const string TraditionalHints = "體臺灣萬與專業東絲兩嚴喪個豐臨為麗舉麼義烏樂喬習鄉書買亂爭於亞產畝親億僅從倉儀價眾優會傘偉傳傷倫偽傑備傢傭債傾僂僑儘償儲兒兌黨蘭關興養獸內岡冊寫軍農馮沖決況凍淨淒減湊凜幾鳳憑凱擊鑿劃劇劉則剛創刪別製剎劑勁動務勝勞勢勳匯區醫華協單賣盧衛卻廠廳歷厲壓厭廁釐廂廈廚廢廣莊慶庫應廟龐廬開異棄張彌彎彈強歸當錄彙徑從復徵徹恆愛態慘慣慶憂懷懸戰戲戶撲執擴掃揚換據損搶攜攝擺搖敗敘敵數斂斃時晉晝曆術機殺雜權條來楊極構標樓樣樹橋檔檢櫃歐歡殘毀氣漢湯溝滅滾滿濾濟濃濤灣燈靈災爐爭爺牆狀獨獎環現產畫異疊癡發盜盞監盤眾睏礦碼磚禮禱禦種積穀穩窮竄竅競筆筍節範築簡簽籌籠糧糾紀約紅紋納紐純紙級紛統絲絕經綁綠維綱網緊緒線練組細織終紹結給絡絢絕絞統繼續纖罷羅聖聞聯聰聲聽職腦臉臨舊艙藝節芻華萬葉號蟲補裝裡見規覺覽觀觸註認討讓訓議訊記講許設訪證評詞試詩話誠誤說課調談請諸諾謀謂謝譯讀變豈豐貝負財貢貨販貪貫責貴貸費貼賀資賓賞賠賢賣賦質賬購贈趕趙跡踐車軌軟轉輪輯輸辦辭農邊遙遞選遺鄧鄭醜釋里鑒針鈔鐘鋼錄錢錯鎖鍋鍵鏡長門閃閉問間閱隊陽陰陣階際陸陳險隨隱難雲電靈靜頂項順須頑顧風飛飯飲館馬駕驗驚體髮鬥魚鳥鳴鴨麥黃點齊齒龍龜";
    private const string SimplifiedHints = "体台万与专业东丝两严丧个丰临为丽举么义乌乐乔习乡书买乱争于亚产亩亲亿仅从仓仪价众优会伞伟传伤伦伪杰备家佣债倾偻侨尽偿储儿兑党兰关兴养兽内冈册写军农冯冲决况冻净凄减凑凛几凤凭凯击凿划剧刘则刚创删别制刹剂劲动务胜劳势勋汇区医华协单卖卢卫却厂厅历厉压厌厕厘厢厦厨废广庄庆库应庙庞庐开异弃张弥弯弹强归当录汇径从复征彻恒爱态惨惯忧怀悬战戏户扑执扩扫扬换据损抢携摄摆摇败叙敌数敛毙时晋昼术机杀杂权条来杨极构标楼样树桥档检柜欧欢残毁气汉汤沟灭滚满滤济浓涛湾灯灵灾炉爷墙状独奖环现产画叠痴发盗盏监盘困矿码砖礼祷御种积谷稳穷窜窍竞笔笋节范筑简签筹笼粮纠纪约红纹纳纽纯纸级纷统丝绝经绑绿维纲网紧绪线练组细织终绍结给络绚绞继续纤罢罗圣闻联聪声听职脑脸旧舱艺刍叶号虫补装里见规觉览观触注认讨让训议讯记讲许设访证评词试诗话诚误说课调谈请诸诺谋谓谢译读变岂贝负财贡货贩贪贯责贵贷费贴贺资宾赏赔贤卖赋质账购赠赶赵迹践车轨软转轮辑输办辞边遥递选遗邓郑丑释鉴针钞钟钢钱错锁锅键镜长门闪闭问间阅队阳阴阵阶际陆陈险随隐难云电静顶项顺须顽顾风飞饭饮馆马驾验惊发斗鱼鸟鸣鸭麦黄点齐齿龙龟";

    public LanguageDetectionResult Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new("und", 0, new Dictionary<string, double> { ["und"] = 1 }, false);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = 0, ["en"] = 0, ["ja"] = 0, ["ko"] = 0
        };
        var traditional = 0;
        var simplified = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (IsKana(value)) counts["ja"]++;
            else if (IsHangul(value)) counts["ko"]++;
            else if (IsHan(value))
            {
                counts["zh"]++;
                if (rune.IsBmp)
                {
                    var ch = (char)value;
                    if (TraditionalHints.Contains(ch)) traditional++;
                    if (SimplifiedHints.Contains(ch)) simplified++;
                }
            }
            else if (IsLatinLetter(value)) counts["en"]++;
        }

        // Han characters in Japanese text belong to the Japanese signal.
        if (counts["ja"] > 0 && counts["zh"] > 0)
        {
            counts["ja"] += counts["zh"];
            counts["zh"] = 0;
        }

        var total = counts.Values.Sum();
        if (total == 0)
            return new("und", 0, new Dictionary<string, double> { ["und"] = 1 }, false);

        var distribution = counts
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => Math.Round((double)pair.Value / total, 4));
        var dominant = distribution.MaxBy(pair => pair.Value);
        var primary = dominant.Key;
        if (primary == "zh") primary = traditional > simplified * 1.2 ? "zh-Hant" : "zh-Hans";

        var significant = distribution.Count(pair => pair.Value >= 0.15);
        var multilingual = significant > 1;
        return new(primary, dominant.Value, distribution, multilingual);
    }

    private static bool IsHan(int value) => value is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0x20000 and <= 0x3134F;
    private static bool IsKana(int value) => value is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF;
    private static bool IsHangul(int value) => value is >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xAC00 and <= 0xD7AF;
    private static bool IsLatinLetter(int value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 0x00C0 and <= 0x024F;
}

public sealed class ContentClassificationService : IContentClassificationService
{
    private static readonly Regex CodeLinePattern = new(@"(?m)^\s*(?:public|private|protected|class|function|def|const|let|var|SELECT|INSERT|UPDATE|using|import)\b");
    private static readonly Regex MarkdownTablePattern = new(@"(?m)^\s*\|.+\|\s*$");
    private static readonly Regex FormulaPattern = new(@"\$\$|\\\[|\\begin\{|\b(?:sin|cos|log)\s*\(");
    private static readonly Regex ReferencePattern = new(@"(?im)^\s*(?:references|参考文献|bibliography)\s*$|^\s*\[\d+\]\s+");
    private static readonly Regex UrlOnlyPattern = new(@"^https?://\S+$", RegexOptions.IgnoreCase);

    public ContentClassificationResult Classify(string? content, LanguageDetectionResult language)
    {
        if (string.IsNullOrWhiteSpace(content)) return new("paragraph", "skip", false);
        var trimmed = content.Trim();
        var type = trimmed.StartsWith("```", StringComparison.Ordinal) || CodeLinePattern.IsMatch(trimmed) ? "code"
            : MarkdownTablePattern.IsMatch(trimmed) ? "table"
            : FormulaPattern.IsMatch(trimmed) ? "formula"
            : ReferencePattern.IsMatch(trimmed) ? "reference"
            : trimmed.StartsWith('#') ? "heading"
            : UrlOnlyPattern.IsMatch(trimmed) ? "reference"
            : "paragraph";

        if (type is "code" or "formula" or "reference") return new(type, "keep_original", false);
        if (language.Confidence < 0.45 || language.PrimaryLanguage == "und") return new(type, "review", false);
        if (language.IsMultilingual) return new(type, "mixed_split", true);
        if (language.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return new(type, "zh_direct", false);
        return new(type, "translate", true);
    }
}

public sealed class ChineseNormalizationService : IChineseNormalizationService
{
    private static readonly Regex HorizontalWhitespacePattern = new(@"[\t\p{Zs}]+");
    private static readonly Regex ExcessBlankLinesPattern = new(@"\n{3,}");

    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        normalized = HorizontalWhitespacePattern.Replace(normalized, " ");
        normalized = ExcessBlankLinesPattern.Replace(normalized, "\n\n");
        return normalized.Trim();
    }
}
