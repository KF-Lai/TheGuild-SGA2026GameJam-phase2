using System;
using System.Collections.Generic;
using UnityEngine;

// FT-07 Guild Building System — CSV 載入與驗證
// 實作依據：【FT-07-FSD】guild-building-system.md §4.4 / §6.1
// 職責（SRP）：BuildingTable.csv 全行索引建立、4 步驗證、GetRow / GetMaxLevel / GetMinLevel 查詢

namespace TheGuild.Gameplay.Building
{
    /// <summary>
    /// BuildingTable.csv 的索引載入器。
    /// 驗證 6 棟建築資料完整性，並提供同步查詢（熱路徑 O(1) dictionary lookup）。
    /// FSD §6.1 / §4.4。
    /// </summary>
    internal class BuildingTableLoader
    {
        // 複合主鍵 (buildingID, level) → BuildingRow；C# value tuple 自帶高效 GetHashCode
        private Dictionary<(int buildingID, int level), BuildingRow> _byKey;

        // 各建築的 maxLevel 快取
        private Dictionary<int, int> _maxLevelByBuildingID;

        // 全部合法的 buildingID 集合
        private static readonly HashSet<int> s_expectedBuildingIDs = new HashSet<int> { 1, 2, 3, 4, 5, 6 };

        /// <summary>
        /// 從 DataManager 取得的全行資料建立索引，並執行 FSD §6.1 四步驗證。
        /// 驗證失敗時拋出 <see cref="MissingBuildingDataException"/>。
        /// </summary>
        /// <param name="rows">DataManager.Instance.GetAll&lt;BuildingRow&gt;() 的結果。</param>
        public void Initialize(IReadOnlyList<BuildingRow> rows)
        {
            if (rows == null)
            {
                throw new MissingBuildingDataException("BuildingTable rows 為 null。");
            }

            // ── 步驟 1：建立 dictionary 索引 ─────────────────────────────────

            _byKey = new Dictionary<(int, int), BuildingRow>(rows.Count);
            _maxLevelByBuildingID = new Dictionary<int, int>();

            // 暫存各 buildingID 對應的所有 row，供步驟 3 驗證用
            Dictionary<int, List<BuildingRow>> rowsByID = new Dictionary<int, List<BuildingRow>>();

            for (int i = 0; i < rows.Count; i++)
            {
                BuildingRow row = rows[i];
                if (row == null)
                {
                    continue;
                }

                var key = (row.buildingID, row.level);
                if (_byKey.ContainsKey(key))
                {
                    // 複合主鍵重複視為資料異常，以後來者覆蓋並 LogWarning
                    Debug.LogWarning(
                        $"[BuildingTableLoader] 複合主鍵重複（buildingID={row.buildingID}, level={row.level}），後者覆蓋前者。");
                }

                _byKey[key] = row;

                if (!rowsByID.TryGetValue(row.buildingID, out List<BuildingRow> list))
                {
                    list = new List<BuildingRow>();
                    rowsByID[row.buildingID] = list;
                }

                list.Add(row);
            }

            // ── 步驟 2：驗證 buildingID 集合恰為 {1,2,3,4,5,6} ─────────────

            HashSet<int> actualIDs = new HashSet<int>(rowsByID.Keys);
            foreach (int expected in s_expectedBuildingIDs)
            {
                if (!actualIDs.Contains(expected))
                {
                    throw new MissingBuildingDataException(
                        $"BuildingTable 缺少 buildingID={expected} 的資料列。");
                }
            }

            // ── 步驟 3：對每個 buildingID 做細項驗證 ─────────────────────────

            foreach (int buildingID in s_expectedBuildingIDs)
            {
                List<BuildingRow> buildingRows = rowsByID[buildingID];

                // 3a：maxLevel 必須全部一致
                int firstMaxLevel = buildingRows[0].maxLevel;
                for (int i = 1; i < buildingRows.Count; i++)
                {
                    if (buildingRows[i].maxLevel != firstMaxLevel)
                    {
                        throw new MissingBuildingDataException(
                            $"buildingID={buildingID} 的 maxLevel 不一致（row[0]={firstMaxLevel}, row[{i}]={buildingRows[i].maxLevel}）。");
                    }
                }

                _maxLevelByBuildingID[buildingID] = firstMaxLevel;

                // 3b：level 集合完整性
                // buildingID=6：期望 {0,1,2,3,4,5}（假設 maxLevel=5）
                // 其他：期望 {1,2,...,maxLevel}
                int minLevel = buildingID == 6 ? 0 : 1;
                for (int lv = minLevel; lv <= firstMaxLevel; lv++)
                {
                    if (!_byKey.ContainsKey((buildingID, lv)))
                    {
                        throw new MissingBuildingDataException(
                            $"buildingID={buildingID} 缺少 level={lv} 的資料列。");
                    }
                }

                // 3c：最小 level 行 upgradeCost==0 且 guildLevelReq==0
                BuildingRow minRow = _byKey[(buildingID, minLevel)];
                if (minRow.upgradeCost != 0 || minRow.guildLevelReq != 0)
                {
                    throw new MissingBuildingDataException(
                        $"buildingID={buildingID} 的最小 level={minLevel} 行 upgradeCost 或 guildLevelReq 不為 0。" +
                        $"（upgradeCost={minRow.upgradeCost}, guildLevelReq={minRow.guildLevelReq}）");
                }

                // 3d：level=2 行 guildLevelReq==0（雙軌閘設計，LogError 不 throw；FSD §6.1）
                if (_byKey.TryGetValue((buildingID, 2), out BuildingRow lv2Row))
                {
                    if (lv2Row.guildLevelReq != 0)
                    {
                        Debug.LogError(
                            $"[BuildingTableLoader] buildingID={buildingID} 的 level=2 行 guildLevelReq={lv2Row.guildLevelReq}，" +
                            $"設計要求應為 0（雙軌閘：L2 不卡聲望）。");
                    }
                }
            }

            // ── 步驟 4：索引建立後常駐記憶體（無額外操作）─────────────────────
        }

        /// <summary>
        /// 取得指定建築指定等級的資料列。
        /// 找不到時拋例外（表示前期驗證遺漏，屬程式邏輯異常）。
        /// </summary>
        public BuildingRow GetRow(int buildingID, int level)
        {
            if (_byKey.TryGetValue((buildingID, level), out BuildingRow row))
            {
                return row;
            }

            // 依 FSD §7：找不到視為資料異常，前期驗證已遺漏
            throw new MissingBuildingDataException(
                $"BuildingTableLoader.GetRow：找不到 buildingID={buildingID}, level={level} 的資料。");
        }

        /// <summary>
        /// 取得指定建築的最高等級。
        /// </summary>
        public int GetMaxLevel(int buildingID)
        {
            if (_maxLevelByBuildingID.TryGetValue(buildingID, out int maxLevel))
            {
                return maxLevel;
            }

            throw new MissingBuildingDataException(
                $"BuildingTableLoader.GetMaxLevel：找不到 buildingID={buildingID} 的最高等級資料。");
        }

        /// <summary>
        /// 取得指定建築的最低等級（buildingID=6 回傳 0，其他回傳 1）。
        /// </summary>
        public int GetMinLevel(int buildingID)
        {
            return buildingID == 6 ? 0 : 1;
        }
    }

    /// <summary>
    /// BuildingTable.csv 資料缺失或格式錯誤時拋出。
    /// FSD §6.1 Step 2 / §7 Case 5.4。
    /// </summary>
    public class MissingBuildingDataException : Exception
    {
        public MissingBuildingDataException(string message) : base(message) { }
    }
}
