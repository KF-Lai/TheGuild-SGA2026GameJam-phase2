namespace TheGuild.Gameplay.Resources
{
    /// <summary>
    /// 破產門檻設定資料列。
    /// </summary>
    public sealed class BankruptcyThresholdData
    {
        // 欄位名稱需與 CSV header 精準對應；CsvParser 對一般表格採 Ordinal（case-sensitive）匹配。
        public int reputationMin;
        public int reputationMax;
        public long warningDurationSec;
    }
}
