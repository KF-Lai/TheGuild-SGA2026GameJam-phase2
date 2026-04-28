using System;
using System.Collections.Generic;

namespace TheGuild.Gameplay.Trait
{
    /// <summary>
    /// C-05 特質資料 DTO（對應 TraitTable.csv）。
    /// </summary>
    [Serializable]
    public sealed class TraitData
    {
        public int traitID;
        public string name;
        public string description;
        public string effectType;
        public string effectTarget;
        public float effectValue;
    }

    /// <summary>
    /// C-05 特質群組 DTO（對應 TraitGroupTable.csv）。
    /// </summary>
    [Serializable]
    public sealed class TraitGroupData
    {
        public int groupID;
        public string groupName;

        // CsvParser 不支援 int[]，CSV 先綁定 string[] 再由 Loader 轉型注入。
        public string[] traitIDs;
        public int pickCount;
        public string pickMode;

        private static readonly int[] EmptyArray = Array.Empty<int>();
        private int[] _traitIDIntArray = EmptyArray;

        public IReadOnlyList<int> TraitIDs => _traitIDIntArray;

        internal void SetTraitIDs(int[] traitIDs)
        {
            if (traitIDs == null || traitIDs.Length == 0)
            {
                _traitIDIntArray = EmptyArray;
                return;
            }

            int[] clone = new int[traitIDs.Length];
            Array.Copy(traitIDs, clone, traitIDs.Length);
            _traitIDIntArray = clone;
        }
    }
}
