using System;
using System.Collections;
using System.Collections.Generic;

namespace TheGuild.Gameplay.Profession
{
    /// <summary>
    /// 職業資料中唯讀整數集合的對外契約（替代 .NET 5+ 才有的 IReadOnlySet<int>）。
    /// </summary>
    public interface IReadOnlyIntSet : IReadOnlyCollection<int>
    {
        bool Contains(int item);
    }

    /// <summary>
    /// C-03 職業資料 DTO（CSV 反射綁定 + 對外唯讀集合）。
    /// </summary>
    [Serializable]
    public sealed class ProfessionData
    {
        // CSV 綁定欄位（需與 CSV header 精確一致）
        public int professionID;
        public string name;
        public string description;
        public string[] strongTypeIDs;
        public string[] weakTypeIDs;
        public int tier;
        public int baseProfessionID;
        public string[] raceIDs;
        public string[] raceWeights;
        public string[] traitGroupIDs;

        private static readonly HashSet<int> EmptySet = new HashSet<int>();
        private static readonly IReadOnlyIntSet EmptyReadOnlySet = new ReadOnlySetView(EmptySet);
        private static readonly int[] EmptyArray = Array.Empty<int>();

        private HashSet<int> _strongTypeIDSet = new HashSet<int>();
        private HashSet<int> _weakTypeIDSet = new HashSet<int>();
        private IReadOnlyIntSet _strongTypeIds = EmptyReadOnlySet;
        private IReadOnlyIntSet _weakTypeIds = EmptyReadOnlySet;
        private int[] _raceIDs = EmptyArray;
        private int[] _raceWeights = EmptyArray;
        private int[] _traitGroupIDs = EmptyArray;

        public int ProfessionID => professionID;
        public string Name => name;
        public string Description => description;
        public int Tier => tier;
        public int BaseProfessionID => baseProfessionID;
        public IReadOnlyIntSet StrongTypeIds => _strongTypeIds;
        public IReadOnlyIntSet WeakTypeIds => _weakTypeIds;
        public IReadOnlyList<int> RaceIDs => _raceIDs;
        public IReadOnlyList<int> RaceWeights => _raceWeights;
        public IReadOnlyList<int> TraitGroupIDs => _traitGroupIDs;

        internal void FreezeCollections(
            HashSet<int> strongTypeIDs,
            HashSet<int> weakTypeIDs,
            int[] raceIDs,
            int[] raceWeights,
            int[] traitGroupIDs)
        {
            _strongTypeIDSet = strongTypeIDs == null ? new HashSet<int>() : new HashSet<int>(strongTypeIDs);
            _weakTypeIDSet = weakTypeIDs == null ? new HashSet<int>() : new HashSet<int>(weakTypeIDs);
            _strongTypeIds = new ReadOnlySetView(_strongTypeIDSet);
            _weakTypeIds = new ReadOnlySetView(_weakTypeIDSet);
            _raceIDs = raceIDs == null ? EmptyArray : (int[])raceIDs.Clone();
            _raceWeights = raceWeights == null ? EmptyArray : (int[])raceWeights.Clone();
            _traitGroupIDs = traitGroupIDs == null ? EmptyArray : (int[])traitGroupIDs.Clone();
        }

        internal bool ContainsStrongType(int typeID)
        {
            return _strongTypeIDSet.Contains(typeID);
        }

        internal bool ContainsWeakType(int typeID)
        {
            return _weakTypeIDSet.Contains(typeID);
        }

        /// <summary>
        /// HashSet 的唯讀檢視，避免外部透過 ISet 轉型修改內容。
        /// </summary>
        private sealed class ReadOnlySetView : IReadOnlyIntSet
        {
            private readonly HashSet<int> _source;

            public ReadOnlySetView(HashSet<int> source)
            {
                _source = source ?? EmptySet;
            }

            public int Count => _source.Count;

            public bool Contains(int item)
            {
                return _source.Contains(item);
            }

            public IEnumerator<int> GetEnumerator()
            {
                return _source.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
