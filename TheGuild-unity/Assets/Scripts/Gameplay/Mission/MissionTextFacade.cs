using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Mission
{
    /// <summary>
    /// 任務文字查詢 Facade。
    /// </summary>
    public sealed class MissionTextFacade
    {
        private const string FallbackName = "未知委託";
        private const string FallbackDesc = "（無描述）";

        public (string name, string desc) GetMissionText(string difficulty, int typeID)
        {
            if (DataManager.Instance == null)
            {
                Debug.LogWarning("[MissionTextFacade] DataManager.Instance 為 null，回傳 fallback。");
                return (FallbackName, FallbackDesc);
            }

            List<MissionNameData> rows = DataManager.Instance.PickRandomWhere<MissionNameData>(
                row => row != null &&
                       string.Equals(row.difficulty, difficulty, StringComparison.Ordinal) &&
                       row.typeID == typeID,
                1);

            if (rows == null || rows.Count == 0)
            {
                Debug.LogWarning($"[MissionTextFacade] GetMissionText 查無資料 difficulty={difficulty}, typeID={typeID}，回傳 fallback。");
                return (FallbackName, FallbackDesc);
            }

            MissionNameData picked = rows[0];
            string name = string.IsNullOrWhiteSpace(picked.missionName) ? FallbackName : picked.missionName;
            string desc = string.IsNullOrWhiteSpace(picked.missionDesc) ? FallbackDesc : picked.missionDesc;

            if (ReferenceEquals(name, FallbackName) && ReferenceEquals(desc, FallbackDesc))
            {
                Debug.LogWarning($"[MissionTextFacade] GetMissionText 命中空文字 difficulty={difficulty}, typeID={typeID}，回傳 fallback。");
            }

            return (name, desc);
        }
    }
}
