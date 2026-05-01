using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Mission
{
    /// <summary>
    /// 載入任務資料表並建立查詢索引。
    /// </summary>
    public sealed class MissionDatabaseLoader
    {
        private static readonly string[] RequiredDifficulties =
        {
            "F", "E", "D", "C", "B", "A", "S", "SS", "SSS"
        };

        private readonly int _escortTypeID;
        private readonly int _factionNeutralID;
        private readonly Func<int, bool> _factionRouteValidator;

        // === v3.1 patch P3.1-001 ===
        /// <summary>
        /// TraitTable FK 驗證器；null 表示 TraitTable 尚未整合，跳過 requiredTraitID FK 驗證。
        /// 透過 MissionDatabaseService.SetTraitTableValidatorForTests 注入（測試用）。
        /// 正式執行時由 MissionDatabaseService.InitializeDatabase 傳入 DataManager 查詢委派。
        /// </summary>
        private readonly Func<int, bool> _traitTableValidator;

        public MissionDatabaseLoader(int escortTypeID, int factionNeutralID,
            Func<int, bool> factionRouteValidator = null,
            Func<int, bool> traitTableValidator = null)
        {
            _escortTypeID = escortTypeID;
            _factionNeutralID = factionNeutralID;
            _factionRouteValidator = factionRouteValidator;
            _traitTableValidator = traitTableValidator;
        }

        public MissionDatabaseCache Build()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogError("[MissionDatabaseLoader] DataManager.Instance 為 null，無法載入 Mission Database。");
                return MissionDatabaseCache.Empty;
            }

            Dictionary<string, MissionDifficultyData> difficultyDict = LoadDifficultyDictionary();
            ValidateRequiredDifficulties(difficultyDict);

            Dictionary<int, MissionTypeData> typeDict = LoadTypeDictionary();
            Dictionary<int, MissionCategoryData> categoryDict = LoadCategoryDictionary();

            Dictionary<string, List<MissionTemplate>> regularByDifficultyBuffer =
                new Dictionary<string, List<MissionTemplate>>(StringComparer.Ordinal);
            Dictionary<string, Dictionary<int, List<MissionTemplate>>> regularByDifficultyTypeBuffer =
                new Dictionary<string, Dictionary<int, List<MissionTemplate>>>(StringComparer.Ordinal);
            Dictionary<int, List<MissionTemplate>> byCategoryBuffer =
                new Dictionary<int, List<MissionTemplate>>();

            Dictionary<int, MissionTemplate> templateDict = LoadTemplateDictionary(
                typeDict,
                categoryDict,
                regularByDifficultyBuffer,
                regularByDifficultyTypeBuffer,
                byCategoryBuffer);

            return new MissionDatabaseCache(
                templateDict,
                difficultyDict,
                typeDict,
                categoryDict,
                FinalizeDifficultyIndex(regularByDifficultyBuffer),
                FinalizeDifficultyTypeIndex(regularByDifficultyTypeBuffer),
                FinalizeCategoryIndex(byCategoryBuffer),
                BuildSortedTypeArray(typeDict));
        }

        public static bool IsEscortDifficultyAllowed(string difficulty)
        {
            string normalized = NormalizeDifficulty(difficulty);
            return normalized == "D" || normalized == "C" || normalized == "B" || normalized == "A";
        }

        private static string NormalizeDifficulty(string difficulty)
        {
            return string.IsNullOrWhiteSpace(difficulty) ? string.Empty : difficulty.Trim();
        }

        private Dictionary<string, MissionDifficultyData> LoadDifficultyDictionary()
        {
            Dictionary<string, MissionDifficultyData> dict =
                new Dictionary<string, MissionDifficultyData>(StringComparer.Ordinal);
            IReadOnlyList<MissionDifficultyData> rows = DataManager.Instance.GetAll<MissionDifficultyData>();

            if (rows.Count == 0)
            {
                Debug.LogError("[MissionDatabaseLoader] MissionDifficultyTable 為空，將使用 fallback 行為。");
                return dict;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                MissionDifficultyData row = rows[i];
                if (row == null)
                {
                    continue;
                }

                string difficulty = NormalizeDifficulty(row.difficulty);
                if (string.IsNullOrEmpty(difficulty))
                {
                    Debug.LogError("[MissionDatabaseLoader] MissionDifficultyTable 發現空 difficulty，已跳過。");
                    continue;
                }

                row.difficulty = difficulty;

                if (row.baseDeathRate < 0f || row.baseDeathRate > 1f)
                {
                    Debug.LogError($"[MissionDatabaseLoader] difficulty={difficulty} 的 baseDeathRate={row.baseDeathRate} 超出 [0,1]，已 Clamp01。");
                    row.baseDeathRate = Mathf.Clamp01(row.baseDeathRate);
                }

                if (row.factionScoreDelta < 0)
                {
                    Debug.LogError($"[MissionDatabaseLoader] difficulty={difficulty} 的 factionScoreDelta={row.factionScoreDelta} 小於 0，已設為 0。");
                    row.factionScoreDelta = 0;
                }

                if (dict.ContainsKey(difficulty))
                {
                    Debug.LogWarning($"[MissionDatabaseLoader] MissionDifficultyTable difficulty={difficulty} 重複，後者覆蓋前者。");
                }

                dict[difficulty] = row;
            }

            return dict;
        }

        private static void ValidateRequiredDifficulties(IReadOnlyDictionary<string, MissionDifficultyData> difficultyDict)
        {
            for (int i = 0; i < RequiredDifficulties.Length; i++)
            {
                string key = RequiredDifficulties[i];
                if (!difficultyDict.ContainsKey(key))
                {
                    Debug.LogError($"[MissionDatabaseLoader] MissionDifficultyTable 缺少難度：{key}");
                }
            }
        }

        private Dictionary<int, MissionTypeData> LoadTypeDictionary()
        {
            Dictionary<int, MissionTypeData> dict = new Dictionary<int, MissionTypeData>();
            IReadOnlyList<MissionTypeData> rows = DataManager.Instance.GetAll<MissionTypeData>();

            if (rows.Count == 0)
            {
                Debug.LogError("[MissionDatabaseLoader] MissionTypeTable 為空。");
                return dict;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                MissionTypeData row = rows[i];
                if (row == null)
                {
                    continue;
                }

                if (dict.ContainsKey(row.typeID))
                {
                    Debug.LogWarning($"[MissionDatabaseLoader] MissionTypeTable typeID={row.typeID} 重複，後者覆蓋前者。");
                }

                dict[row.typeID] = row;
            }

            return dict;
        }

        private Dictionary<int, MissionCategoryData> LoadCategoryDictionary()
        {
            Dictionary<int, MissionCategoryData> dict = new Dictionary<int, MissionCategoryData>();
            IReadOnlyList<MissionCategoryData> rows = DataManager.Instance.GetAll<MissionCategoryData>();

            if (rows.Count == 0)
            {
                Debug.LogError("[MissionDatabaseLoader] MissionCategoryTable 為空。");
                return dict;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                MissionCategoryData row = rows[i];
                if (row == null)
                {
                    continue;
                }

                if (dict.ContainsKey(row.categoryID))
                {
                    Debug.LogWarning($"[MissionDatabaseLoader] MissionCategoryTable categoryID={row.categoryID} 重複，後者覆蓋前者。");
                }

                dict[row.categoryID] = row;
            }

            return dict;
        }

        private Dictionary<int, MissionTemplate> LoadTemplateDictionary(
            IReadOnlyDictionary<int, MissionTypeData> typeDict,
            IReadOnlyDictionary<int, MissionCategoryData> categoryDict,
            Dictionary<string, List<MissionTemplate>> regularByDifficultyBuffer,
            Dictionary<string, Dictionary<int, List<MissionTemplate>>> regularByDifficultyTypeBuffer,
            Dictionary<int, List<MissionTemplate>> byCategoryBuffer)
        {
            Dictionary<int, MissionTemplate> dict = new Dictionary<int, MissionTemplate>();
            IReadOnlyList<MissionTemplate> rows = DataManager.Instance.GetAll<MissionTemplate>();

            if (rows.Count == 0)
            {
                Debug.LogError("[MissionDatabaseLoader] MissionTemplate 為空。");
                return dict;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                MissionTemplate row = rows[i];
                if (row == null)
                {
                    continue;
                }

                row.difficulty = NormalizeDifficulty(row.difficulty);

                if (!typeDict.ContainsKey(row.typeID))
                {
                    Debug.LogError($"[MissionDatabaseLoader] missionID={row.missionID} 的 typeID={row.typeID} 無對應 MissionTypeTable，已跳過。");
                    continue;
                }

                if (!categoryDict.ContainsKey(row.categoryID))
                {
                    Debug.LogError($"[MissionDatabaseLoader] missionID={row.missionID} 的 categoryID={row.categoryID} 無對應 MissionCategoryTable，已跳過。");
                    continue;
                }

                if (row.typeID == _escortTypeID && !IsEscortDifficultyAllowed(row.difficulty))
                {
                    Debug.LogError($"[MissionDatabaseLoader] missionID={row.missionID} 為護送任務但 difficulty={row.difficulty} 不在 D/C/B/A，已跳過。");
                    continue;
                }

                if (_factionRouteValidator != null &&
                    row.factionID != _factionNeutralID &&
                    !_factionRouteValidator(row.factionID))
                {
                    Debug.LogWarning($"[MissionDatabaseLoader] missionID={row.missionID} 的 factionID={row.factionID} 不存在，已回退為 FACTION_NEUTRAL_ID={_factionNeutralID}。");
                    row.factionID = _factionNeutralID;
                }
                else if (_factionRouteValidator == null)
                {
                    // FT-09 尚未落地前暫停 faction FK 驗證，保留偽碼如下：
                    // if (row.factionID != _factionNeutralID && !FactionRouteTable.Contains(row.factionID))
                    // {
                    //     Debug.LogWarning(...);
                    //     row.factionID = _factionNeutralID;
                    // }
                }

                // === v3.1 patch P3.1-001：三欄位 validation ===
                ValidateV31Fields(row);

                if (dict.ContainsKey(row.missionID))
                {
                    Debug.LogWarning($"[MissionDatabaseLoader] missionID={row.missionID} 重複，後者覆蓋前者。");
                }

                dict[row.missionID] = row;
                IndexTemplate(row, regularByDifficultyBuffer, regularByDifficultyTypeBuffer, byCategoryBuffer);
            }

            return dict;
        }

        // === v3.1 patch P3.1-001 ===
        /// <summary>
        /// 驗證 MissionTemplate v3.1 新增的三個欄位，違規時 LogError 並重置為預設值 0。
        /// GDD C-01 §5.1 Validation 規則。
        /// </summary>
        private void ValidateV31Fields(MissionTemplate row)
        {
            // Rule 1：isScriptedDeath=1 僅允許 categoryID=3
            if (row.isScriptedDeath == 1 && row.categoryID != 3)
            {
                Debug.LogError(
                    $"[MissionDatabaseLoader] missionID={row.missionID}: isScriptedDeath=1 但 categoryID={row.categoryID}（必須為 categoryID=3），已重置為 0。");
                row.isScriptedDeath = 0;
            }

            // Rule 2：minDangerLevel 必須在 [0, 4]
            if (row.minDangerLevel < 0 || row.minDangerLevel > 4)
            {
                Debug.LogError(
                    $"[MissionDatabaseLoader] missionID={row.missionID}: minDangerLevel={row.minDangerLevel} 超出範圍 [0,4]，已重置為 0。");
                row.minDangerLevel = 0;
            }

            // Rule 3：requiredTraitID FK 驗證（TraitTable 整合後生效；validator=null 時跳過驗證）
            if (row.requiredTraitID > 0 && _traitTableValidator != null && !_traitTableValidator(row.requiredTraitID))
            {
                Debug.LogError(
                    $"[MissionDatabaseLoader] missionID={row.missionID}: requiredTraitID={row.requiredTraitID} 在 TraitTable 中不存在，已重置為 0。");
                row.requiredTraitID = 0;
            }
        }

        private static void IndexTemplate(
            MissionTemplate row,
            Dictionary<string, List<MissionTemplate>> regularByDifficultyBuffer,
            Dictionary<string, Dictionary<int, List<MissionTemplate>>> regularByDifficultyTypeBuffer,
            Dictionary<int, List<MissionTemplate>> byCategoryBuffer)
        {
            if (!byCategoryBuffer.TryGetValue(row.categoryID, out List<MissionTemplate> byCategory))
            {
                byCategory = new List<MissionTemplate>();
                byCategoryBuffer[row.categoryID] = byCategory;
            }
            byCategory.Add(row);

            if (row.categoryID != 0)
            {
                return;
            }

            string difficulty = NormalizeDifficulty(row.difficulty);
            if (!regularByDifficultyBuffer.TryGetValue(difficulty, out List<MissionTemplate> byDifficulty))
            {
                byDifficulty = new List<MissionTemplate>();
                regularByDifficultyBuffer[difficulty] = byDifficulty;
            }
            byDifficulty.Add(row);

            if (!regularByDifficultyTypeBuffer.TryGetValue(difficulty, out Dictionary<int, List<MissionTemplate>> byTypeMap))
            {
                byTypeMap = new Dictionary<int, List<MissionTemplate>>();
                regularByDifficultyTypeBuffer[difficulty] = byTypeMap;
            }

            if (!byTypeMap.TryGetValue(row.typeID, out List<MissionTemplate> byType))
            {
                byType = new List<MissionTemplate>();
                byTypeMap[row.typeID] = byType;
            }
            byType.Add(row);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<MissionTemplate>> FinalizeDifficultyIndex(Dictionary<string, List<MissionTemplate>> buffer)
        {
            Dictionary<string, IReadOnlyList<MissionTemplate>> result =
                new Dictionary<string, IReadOnlyList<MissionTemplate>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<MissionTemplate>> pair in buffer)
            {
                result[pair.Key] = pair.Value.ToArray();
            }

            return result;
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>> FinalizeDifficultyTypeIndex(Dictionary<string, Dictionary<int, List<MissionTemplate>>> buffer)
        {
            Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>> result =
                new Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<int, List<MissionTemplate>>> outer in buffer)
            {
                Dictionary<int, IReadOnlyList<MissionTemplate>> inner = new Dictionary<int, IReadOnlyList<MissionTemplate>>();
                foreach (KeyValuePair<int, List<MissionTemplate>> innerPair in outer.Value)
                {
                    inner[innerPair.Key] = innerPair.Value.ToArray();
                }
                result[outer.Key] = inner;
            }

            return result;
        }

        private static IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>> FinalizeCategoryIndex(Dictionary<int, List<MissionTemplate>> buffer)
        {
            Dictionary<int, IReadOnlyList<MissionTemplate>> result = new Dictionary<int, IReadOnlyList<MissionTemplate>>();
            foreach (KeyValuePair<int, List<MissionTemplate>> pair in buffer)
            {
                result[pair.Key] = pair.Value.ToArray();
            }

            return result;
        }

        private static IReadOnlyList<MissionTypeData> BuildSortedTypeArray(IReadOnlyDictionary<int, MissionTypeData> typeDict)
        {
            List<MissionTypeData> list = new List<MissionTypeData>(typeDict.Values);
            list.Sort((a, b) => a.typeID.CompareTo(b.typeID));
            return list.ToArray();
        }
    }
}
