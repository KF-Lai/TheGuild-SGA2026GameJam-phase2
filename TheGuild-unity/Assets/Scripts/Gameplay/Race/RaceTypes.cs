using System;
using System.Collections.Generic;

namespace TheGuild.Gameplay.Race
{
    /// <summary>
    /// C-04 種族資料 DTO。
    /// </summary>
    [Serializable]
    public sealed class RaceData
    {
        public int raceID;
        public string name;
        public string description;
        public string modifiers;

        private static readonly IReadOnlyDictionary<int, RaceModifierEntry> EmptyModifierCache =
            new Dictionary<int, RaceModifierEntry>();

        private IReadOnlyDictionary<int, RaceModifierEntry> _modifierCache = EmptyModifierCache;

        public int RaceID => raceID;
        public string Name => name;
        public string Description => description;

        internal void SetModifierCache(IReadOnlyDictionary<int, RaceModifierEntry> cache)
        {
            _modifierCache = cache ?? EmptyModifierCache;
        }

        internal bool TryGetModifier(int typeID, out RaceModifierEntry entry)
        {
            return _modifierCache.TryGetValue(typeID, out entry);
        }
    }

    /// <summary>
    /// 種族對任務類型的修正值。
    /// </summary>
    [Serializable]
    public sealed class RaceModifierEntry
    {
        public int typeID;
        public float successDelta;
        public float deathDelta;
    }

    /// <summary>
    /// JsonUtility 解析 top-level array 的包裝 DTO。
    /// </summary>
    [Serializable]
    public sealed class RaceModifierListWrapper
    {
        public List<RaceModifierEntry> modifiers;
    }
}
