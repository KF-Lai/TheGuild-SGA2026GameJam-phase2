using System;
using System.Collections.Generic;
using System.Globalization;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.Staff
{
    /// <summary>
    /// StaffTable 與 StaffTuning 載入器。
    /// </summary>
    internal sealed class StaffTableLoader
    {
        private const string KEY_EFFECT_MAX_WILLINGNESS = "EFFECT_MAX_WILLINGNESS_BONUS";
        private const string KEY_EFFECT_MAX_COMMISSION = "EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS";
        private const string KEY_EFFECT_MAX_PENALTY = "EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS";
        private const string KEY_EFFECT_MAX_RECRUIT_REFRESH = "EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC";
        private const string KEY_SWITCH_COOLDOWN = "BUILDING_SWITCH_COOLDOWN_SECONDS";
        private const string KEY_REALLOCATING_AUTO_LEAVE = "REALLOCATING_AUTO_LEAVE_SECONDS";
        private const string KEY_AUTO_LEAVE_SCAN_INTERVAL = "AUTO_LEAVE_SCAN_INTERVAL_SECONDS";
        private const string KEY_ROSTER_CAP = "ROSTER_CAP";

        private const float DEFAULT_EFFECT_MAX_WILLINGNESS = 0.20f;
        private const float DEFAULT_EFFECT_MAX_COMMISSION = 0.10f;
        private const float DEFAULT_EFFECT_MAX_PENALTY = -0.10f;
        private const float DEFAULT_EFFECT_MAX_RECRUIT_REFRESH = 14400f;
        private const int DEFAULT_SWITCH_COOLDOWN_SEC = 21600;
        private const int DEFAULT_REALLOCATING_AUTO_LEAVE_SEC = 43200;
        private const int DEFAULT_AUTO_LEAVE_SCAN_INTERVAL_SEC = 3600;
        private const int DEFAULT_ROSTER_CAP = 0;

        private readonly Dictionary<int, StaffData> _byStaffID = new Dictionary<int, StaffData>(128);
        private bool _initialized;

        public float EffectMaxWillingnessBonus { get; private set; } = DEFAULT_EFFECT_MAX_WILLINGNESS;
        public float EffectMaxAccountantCommissionBonus { get; private set; } = DEFAULT_EFFECT_MAX_COMMISSION;
        public float EffectMaxAccountantPenaltyBonus { get; private set; } = DEFAULT_EFFECT_MAX_PENALTY;
        public float EffectMaxRecruitRefreshReductionSec { get; private set; } = DEFAULT_EFFECT_MAX_RECRUIT_REFRESH;
        public int BuildingSwitchCooldownSec { get; private set; } = DEFAULT_SWITCH_COOLDOWN_SEC;
        public int ReallocatingAutoLeaveSec { get; private set; } = DEFAULT_REALLOCATING_AUTO_LEAVE_SEC;
        public int AutoLeaveScanIntervalSec { get; private set; } = DEFAULT_AUTO_LEAVE_SCAN_INTERVAL_SEC;
        public int RosterCap { get; private set; } = DEFAULT_ROSTER_CAP;

        public bool IsInitialized => _initialized;

        /// <summary>
        /// 載入 StaffTable 與 StaffTuning。
        /// </summary>
        public void Initialize()
        {
            _byStaffID.Clear();
            _initialized = false;

            if (DataManager.Instance == null)
            {
                throw new StaffTableValidationException("DataManager.Instance 為 null，無法初始化 StaffTableLoader。");
            }

            IReadOnlyList<StaffData> rows = DataManager.Instance.GetAll<StaffData>();
            if (rows == null)
            {
                throw new StaffTableValidationException("StaffTable 讀取結果為 null。");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                StaffData row = rows[i];
                if (row == null)
                {
                    continue;
                }

                ValidateAndNormalizeRow(row);

                if (row.staffID == 0)
                {
                    continue;
                }

                if (_byStaffID.ContainsKey(row.staffID))
                {
                    throw new StaffTableValidationException($"StaffTable staffID 重複：{row.staffID}");
                }

                _byStaffID[row.staffID] = row;
            }

            LoadTuningConstants();
            _initialized = true;
        }

        public StaffData Get(int staffID)
        {
            return _byStaffID.TryGetValue(staffID, out StaffData row) ? row : null;
        }

        public IReadOnlyDictionary<int, StaffData> GetAll()
        {
            return _byStaffID;
        }

        private void ValidateAndNormalizeRow(StaffData row)
        {
            if (row.staffID == 0)
            {
                Debug.LogWarning("[StaffTableLoader] staffID=0，已跳過該列。");
                return;
            }

            if (row.salary < 0)
            {
                throw new StaffTableValidationException($"staffID={row.staffID} 的 salary 不可為負值：{row.salary}");
            }

            if (row.severancePay < 0)
            {
                throw new StaffTableValidationException($"staffID={row.staffID} 的 severancePay 不可為負值：{row.severancePay}");
            }

            if (row.minGuildLevel < 1)
            {
                Debug.LogWarning($"[StaffTableLoader] staffID={row.staffID} 的 minGuildLevel={row.minGuildLevel}，已 clamp 為 1。");
                row.minGuildLevel = 1;
            }

            if (row.rarity < 1)
            {
                Debug.LogWarning($"[StaffTableLoader] staffID={row.staffID} 的 rarity={row.rarity}，已 clamp 為 1。");
                row.rarity = 1;
            }

            row.effectIDsParsed.Clear();
            row.effectValuesParsed.Clear();
            row.slotBuildingIDsParsed.Clear();
            row.uiFlagIDsParsed.Clear();
            row.uiFlagBuildingIDsParsed.Clear();

            ParseEffectIDs(row.staffID, row.effectIDs, row.effectIDsParsed);
            ParseEffectValues(row.staffID, row.effectValues, row.effectValuesParsed);

            if (row.effectIDsParsed.Count != row.effectValuesParsed.Count)
            {
                throw new StaffTableValidationException(
                    $"staffID={row.staffID} 的 effectIDs 與 effectValues 長度不一致：" +
                    $"{row.effectIDsParsed.Count} != {row.effectValuesParsed.Count}");
            }

            ParseIntList(row.staffID, "slotBuildingIDs", row.slotBuildingIDs, row.slotBuildingIDsParsed);
            ParseUIFlagIDs(row.staffID, row.uiFlagIDs, row.uiFlagIDsParsed);
            ParseIntList(row.staffID, "uiFlagBuildingIDs", row.uiFlagBuildingIDs, row.uiFlagBuildingIDsParsed);

            if (row.uiFlagIDsParsed.Count != row.uiFlagBuildingIDsParsed.Count)
            {
                throw new StaffTableValidationException(
                    $"staffID={row.staffID} 的 uiFlagIDs 與 uiFlagBuildingIDs 長度不一致：" +
                    $"{row.uiFlagIDsParsed.Count} != {row.uiFlagBuildingIDsParsed.Count}");
            }
        }

        private void LoadTuningConstants()
        {
            EffectMaxWillingnessBonus = ReadFloatOrDefault(KEY_EFFECT_MAX_WILLINGNESS, DEFAULT_EFFECT_MAX_WILLINGNESS, allowZero: true);
            EffectMaxAccountantCommissionBonus = ReadFloatOrDefault(KEY_EFFECT_MAX_COMMISSION, DEFAULT_EFFECT_MAX_COMMISSION, allowZero: true);
            EffectMaxAccountantPenaltyBonus = ReadFloatOrDefault(KEY_EFFECT_MAX_PENALTY, DEFAULT_EFFECT_MAX_PENALTY, allowZero: true);
            EffectMaxRecruitRefreshReductionSec = ReadFloatOrDefault(KEY_EFFECT_MAX_RECRUIT_REFRESH, DEFAULT_EFFECT_MAX_RECRUIT_REFRESH, allowZero: true);
            BuildingSwitchCooldownSec = ReadIntOrDefault(KEY_SWITCH_COOLDOWN, DEFAULT_SWITCH_COOLDOWN_SEC, allowZero: true);
            ReallocatingAutoLeaveSec = ReadIntOrDefault(KEY_REALLOCATING_AUTO_LEAVE, DEFAULT_REALLOCATING_AUTO_LEAVE_SEC, allowZero: false);
            AutoLeaveScanIntervalSec = ReadIntOrDefault(KEY_AUTO_LEAVE_SCAN_INTERVAL, DEFAULT_AUTO_LEAVE_SCAN_INTERVAL_SEC, allowZero: false);
            RosterCap = ReadIntOrDefault(KEY_ROSTER_CAP, DEFAULT_ROSTER_CAP, allowZero: true);

            if (EffectMaxWillingnessBonus < 0f)
            {
                Debug.LogWarning("[StaffTableLoader] EFFECT_MAX_WILLINGNESS_BONUS 小於 0，已改用預設值。");
                EffectMaxWillingnessBonus = DEFAULT_EFFECT_MAX_WILLINGNESS;
            }

            if (EffectMaxAccountantCommissionBonus < 0f)
            {
                Debug.LogWarning("[StaffTableLoader] EFFECT_MAX_ACCOUNTANT_COMMISSION_BONUS 小於 0，已改用預設值。");
                EffectMaxAccountantCommissionBonus = DEFAULT_EFFECT_MAX_COMMISSION;
            }

            if (EffectMaxAccountantPenaltyBonus > 0f)
            {
                Debug.LogWarning("[StaffTableLoader] EFFECT_MAX_ACCOUNTANT_PENALTY_BONUS 大於 0，已改用預設值。");
                EffectMaxAccountantPenaltyBonus = DEFAULT_EFFECT_MAX_PENALTY;
            }

            if (EffectMaxRecruitRefreshReductionSec < 0f)
            {
                Debug.LogWarning("[StaffTableLoader] EFFECT_MAX_RECRUIT_REFRESH_REDUCTION_SEC 小於 0，已改用預設值。");
                EffectMaxRecruitRefreshReductionSec = DEFAULT_EFFECT_MAX_RECRUIT_REFRESH;
            }

            if (BuildingSwitchCooldownSec < 0)
            {
                Debug.LogWarning("[StaffTableLoader] BUILDING_SWITCH_COOLDOWN_SECONDS 小於 0，已改用預設值。");
                BuildingSwitchCooldownSec = DEFAULT_SWITCH_COOLDOWN_SEC;
            }

            if (ReallocatingAutoLeaveSec <= 0)
            {
                Debug.LogWarning("[StaffTableLoader] REALLOCATING_AUTO_LEAVE_SECONDS 非正值，已改用預設值。");
                ReallocatingAutoLeaveSec = DEFAULT_REALLOCATING_AUTO_LEAVE_SEC;
            }

            if (AutoLeaveScanIntervalSec <= 0)
            {
                Debug.LogWarning("[StaffTableLoader] AUTO_LEAVE_SCAN_INTERVAL_SECONDS 非正值，已改用預設值。");
                AutoLeaveScanIntervalSec = DEFAULT_AUTO_LEAVE_SCAN_INTERVAL_SEC;
            }

            if (RosterCap < 0)
            {
                Debug.LogWarning("[StaffTableLoader] ROSTER_CAP 小於 0，已改為 0（代表無上限）。");
                RosterCap = 0;
            }
        }

        private static void ParseEffectIDs(int staffID, string[] raw, List<StaffEffect> output)
        {
            if (raw == null || raw.Length == 0)
            {
                return;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                string token = raw[i] == null ? string.Empty : raw[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (!Enum.TryParse(token, true, out StaffEffect parsed))
                {
                    throw new StaffTableValidationException($"staffID={staffID} 的 effectIDs 含非法值：{token}");
                }

                output.Add(parsed);
            }
        }

        private static void ParseEffectValues(int staffID, float[] raw, List<float> output)
        {
            if (raw == null || raw.Length == 0)
            {
                return;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                float value = raw[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new StaffTableValidationException($"staffID={staffID} 的 effectValues[{i}] 非法：{value}");
                }

                output.Add(value);
            }
        }

        private static void ParseUIFlagIDs(int staffID, string[] raw, List<StaffUIFlag> output)
        {
            if (raw == null || raw.Length == 0)
            {
                return;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                string token = raw[i] == null ? string.Empty : raw[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (!Enum.TryParse(token, true, out StaffUIFlag parsed))
                {
                    throw new StaffTableValidationException($"staffID={staffID} 的 uiFlagIDs 含非法值：{token}");
                }

                output.Add(parsed);
            }
        }

        private static void ParseIntList(int staffID, string fieldName, string[] raw, List<int> output)
        {
            if (raw == null || raw.Length == 0)
            {
                return;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                string token = raw[i] == null ? string.Empty : raw[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    throw new StaffTableValidationException($"staffID={staffID} 的 {fieldName}[{i}] 非法：{token}");
                }

                output.Add(value);
            }
        }

        private static float ReadFloatOrDefault(string key, float fallback, bool allowZero)
        {
            float value = DataManager.Instance.GetFloat(key);
            if (allowZero ? value < 0f : value <= 0f)
            {
                return fallback;
            }

            return value;
        }

        private static int ReadIntOrDefault(string key, int fallback, bool allowZero)
        {
            int value = DataManager.Instance.GetInt(key);
            if (allowZero ? value < 0 : value <= 0)
            {
                return fallback;
            }

            return value;
        }
    }
}
