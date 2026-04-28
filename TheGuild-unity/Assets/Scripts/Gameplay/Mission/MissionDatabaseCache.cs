using System;
using System.Collections.Generic;

namespace TheGuild.Gameplay.Mission
{
    /// <summary>
    /// Loader 建立後交給 Service 的唯讀快取。
    /// </summary>
    public sealed class MissionDatabaseCache
    {
        private static readonly IReadOnlyList<MissionTypeData> EmptyTypeList = Array.Empty<MissionTypeData>();

        public static MissionDatabaseCache Empty { get; } = new MissionDatabaseCache(
            new Dictionary<int, MissionTemplate>(),
            new Dictionary<string, MissionDifficultyData>(StringComparer.Ordinal),
            new Dictionary<int, MissionTypeData>(),
            new Dictionary<int, MissionCategoryData>(),
            new Dictionary<string, IReadOnlyList<MissionTemplate>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>>(StringComparer.Ordinal),
            new Dictionary<int, IReadOnlyList<MissionTemplate>>(),
            EmptyTypeList);

        public MissionDatabaseCache(
            IReadOnlyDictionary<int, MissionTemplate> templateByID,
            IReadOnlyDictionary<string, MissionDifficultyData> difficultyByKey,
            IReadOnlyDictionary<int, MissionTypeData> typeByID,
            IReadOnlyDictionary<int, MissionCategoryData> categoryByID,
            IReadOnlyDictionary<string, IReadOnlyList<MissionTemplate>> regularTemplatesByDifficulty,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>> regularTemplatesByDifficultyAndType,
            IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>> templatesByCategory,
            IReadOnlyList<MissionTypeData> allMissionTypes)
        {
            TemplateByID = templateByID ?? new Dictionary<int, MissionTemplate>();
            DifficultyByKey = difficultyByKey ?? new Dictionary<string, MissionDifficultyData>(StringComparer.Ordinal);
            TypeByID = typeByID ?? new Dictionary<int, MissionTypeData>();
            CategoryByID = categoryByID ?? new Dictionary<int, MissionCategoryData>();
            RegularTemplatesByDifficulty =
                regularTemplatesByDifficulty ?? new Dictionary<string, IReadOnlyList<MissionTemplate>>(StringComparer.Ordinal);
            RegularTemplatesByDifficultyAndType =
                regularTemplatesByDifficultyAndType ??
                new Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>>(StringComparer.Ordinal);
            TemplatesByCategory = templatesByCategory ?? new Dictionary<int, IReadOnlyList<MissionTemplate>>();
            AllMissionTypes = allMissionTypes ?? EmptyTypeList;
        }

        public IReadOnlyDictionary<int, MissionTemplate> TemplateByID { get; }
        public IReadOnlyDictionary<string, MissionDifficultyData> DifficultyByKey { get; }
        public IReadOnlyDictionary<int, MissionTypeData> TypeByID { get; }
        public IReadOnlyDictionary<int, MissionCategoryData> CategoryByID { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<MissionTemplate>> RegularTemplatesByDifficulty { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>>> RegularTemplatesByDifficultyAndType { get; }
        public IReadOnlyDictionary<int, IReadOnlyList<MissionTemplate>> TemplatesByCategory { get; }
        public IReadOnlyList<MissionTypeData> AllMissionTypes { get; }
    }
}
