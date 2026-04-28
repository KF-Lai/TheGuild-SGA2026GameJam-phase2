using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGuild.Gameplay.Staff
{
    /// <summary>
    /// 職員效果聚合器（即時遍歷、無快取）。
    /// </summary>
    internal sealed class StaffEffectAggregator
    {
        private readonly StaffTableLoader _loader;
        private readonly Func<IReadOnlyDictionary<int, StaffInstance>> _rosterGetter;
        private readonly Func<bool> _isSystemUnlocked;

        public StaffEffectAggregator(
            StaffTableLoader loader,
            Func<IReadOnlyDictionary<int, StaffInstance>> rosterGetter,
            Func<bool> isSystemUnlocked)
        {
            _loader = loader;
            _rosterGetter = rosterGetter;
            _isSystemUnlocked = isSystemUnlocked;
        }

        public float GetStaffWillingnessBonus()
        {
            if (!_isSystemUnlocked())
            {
                return 0f;
            }

            float sum = 0f;
            IReadOnlyDictionary<int, StaffInstance> roster = _rosterGetter();
            foreach (KeyValuePair<int, StaffInstance> pair in roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState == StaffState.OnLeave)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null)
                {
                    continue;
                }

                for (int i = 0; i < data.effectIDsParsed.Count; i++)
                {
                    if (data.effectIDsParsed[i] == StaffEffect.Willingness)
                    {
                        sum += data.effectValuesParsed[i];
                    }
                }
            }

            return Mathf.Min(sum, _loader.EffectMaxWillingnessBonus);
        }

        public float GetAccountantCommissionBonus()
        {
            if (!_isSystemUnlocked())
            {
                return 0f;
            }

            float sum = 0f;
            IReadOnlyDictionary<int, StaffInstance> roster = _rosterGetter();
            foreach (KeyValuePair<int, StaffInstance> pair in roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Working)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null)
                {
                    continue;
                }

                for (int i = 0; i < data.effectIDsParsed.Count; i++)
                {
                    if (data.effectIDsParsed[i] != StaffEffect.AccountantCommission)
                    {
                        continue;
                    }

                    if (data.slotBuildingIDsParsed.Count <= i)
                    {
                        continue;
                    }

                    if (staff.assignedBuildingID != data.slotBuildingIDsParsed[i])
                    {
                        continue;
                    }

                    sum += data.effectValuesParsed[i];
                }
            }

            return Mathf.Min(sum, _loader.EffectMaxAccountantCommissionBonus);
        }

        public float GetAccountantPenaltyBonus()
        {
            if (!_isSystemUnlocked())
            {
                return 0f;
            }

            float sum = 0f;
            IReadOnlyDictionary<int, StaffInstance> roster = _rosterGetter();
            foreach (KeyValuePair<int, StaffInstance> pair in roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Working)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null)
                {
                    continue;
                }

                for (int i = 0; i < data.effectIDsParsed.Count; i++)
                {
                    if (data.effectIDsParsed[i] != StaffEffect.AccountantPenaltyOnVault)
                    {
                        continue;
                    }

                    if (data.slotBuildingIDsParsed.Count <= i)
                    {
                        continue;
                    }

                    if (staff.assignedBuildingID != data.slotBuildingIDsParsed[i])
                    {
                        continue;
                    }

                    sum += data.effectValuesParsed[i];
                }
            }

            // 負向 floor：max(sum, 負值上限)
            return Mathf.Max(sum, _loader.EffectMaxAccountantPenaltyBonus);
        }

        public int GetRecruitRefreshReductionSec()
        {
            if (!_isSystemUnlocked())
            {
                return 0;
            }

            float sum = 0f;
            IReadOnlyDictionary<int, StaffInstance> roster = _rosterGetter();
            foreach (KeyValuePair<int, StaffInstance> pair in roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Working)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null)
                {
                    continue;
                }

                for (int i = 0; i < data.effectIDsParsed.Count; i++)
                {
                    if (data.effectIDsParsed[i] != StaffEffect.RecruitRefreshOnCounter)
                    {
                        continue;
                    }

                    if (data.slotBuildingIDsParsed.Count <= i)
                    {
                        continue;
                    }

                    if (staff.assignedBuildingID != data.slotBuildingIDsParsed[i])
                    {
                        continue;
                    }

                    sum += data.effectValuesParsed[i];
                }
            }

            sum = Mathf.Min(sum, _loader.EffectMaxRecruitRefreshReductionSec);
            return Mathf.RoundToInt(sum);
        }

        public bool IsSuccessRatePreviewEnabled()
        {
            if (!_isSystemUnlocked())
            {
                return false;
            }

            IReadOnlyDictionary<int, StaffInstance> roster = _rosterGetter();
            foreach (KeyValuePair<int, StaffInstance> pair in roster)
            {
                StaffInstance staff = pair.Value;
                if (staff == null || staff.currentState != StaffState.Working)
                {
                    continue;
                }

                StaffData data = _loader.Get(staff.staffID);
                if (data == null)
                {
                    continue;
                }

                for (int i = 0; i < data.uiFlagIDsParsed.Count; i++)
                {
                    if (data.uiFlagIDsParsed[i] == StaffUIFlag.SuccessRatePreview &&
                        data.uiFlagBuildingIDsParsed.Count > i &&
                        staff.assignedBuildingID == data.uiFlagBuildingIDsParsed[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
