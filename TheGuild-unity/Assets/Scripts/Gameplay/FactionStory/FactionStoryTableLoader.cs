using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.Gameplay.FactionStory
{
    internal sealed class FactionStoryTableLoader
    {
        private static readonly IReadOnlyList<StoryStageData> s_emptyStages = new List<StoryStageData>(0);

        private readonly Dictionary<int, FactionRouteData> _routeByFactionID = new Dictionary<int, FactionRouteData>(8);
        private readonly Dictionary<int, StoryStageData> _stageByID = new Dictionary<int, StoryStageData>(32);
        private readonly Dictionary<int, StoryStageData> _stageByMissionID = new Dictionary<int, StoryStageData>(32);
        private readonly Dictionary<int, List<StoryStageData>> _stagesByFactionID = new Dictionary<int, List<StoryStageData>>(8);

        private bool _initialized;

        public bool IsInitialized => _initialized;

        public bool Initialize(int factionNeutralID)
        {
            _initialized = false;
            _routeByFactionID.Clear();
            _stageByID.Clear();
            _stageByMissionID.Clear();
            _stagesByFactionID.Clear();

            if (DataManager.Instance == null)
            {
                Debug.LogError("[FactionStoryTableLoader] DataManager.Instance is null.");
                return false;
            }

            IReadOnlyList<FactionRouteData> routes = DataManager.Instance.GetAll<FactionRouteData>();
            IReadOnlyList<StoryStageData> stages = DataManager.Instance.GetAll<StoryStageData>();

            if (!LoadRoutes(routes, factionNeutralID))
            {
                return false;
            }

            if (!LoadStages(stages, factionNeutralID))
            {
                return false;
            }

            if (_routeByFactionID.Count == 0)
            {
                Debug.LogWarning("[FactionStoryTableLoader] No non-neutral route found.");
                return false;
            }

            if (_stageByID.Count == 0)
            {
                Debug.LogWarning("[FactionStoryTableLoader] StoryStageTable has no valid stage.");
                return false;
            }

            ValidateFactionStageContinuity();
            _initialized = true;
            return true;
        }

        public bool HasFaction(int factionID)
        {
            return _routeByFactionID.ContainsKey(factionID);
        }

        public IReadOnlyList<StoryStageData> GetByFactionID(int factionID)
        {
            return _stagesByFactionID.TryGetValue(factionID, out List<StoryStageData> list) ? list : s_emptyStages;
        }

        public StoryStageData GetByMissionID(int missionID)
        {
            return _stageByMissionID.TryGetValue(missionID, out StoryStageData stage) ? stage : null;
        }

        public StoryStageData GetByStageID(int stageID)
        {
            return _stageByID.TryGetValue(stageID, out StoryStageData stage) ? stage : null;
        }

        public int GetStageCount(int factionID)
        {
            return _stagesByFactionID.TryGetValue(factionID, out List<StoryStageData> list) ? list.Count : 0;
        }

        public int GetMaxStageIndex(int factionID)
        {
            if (!_stagesByFactionID.TryGetValue(factionID, out List<StoryStageData> list) || list.Count == 0)
            {
                return -1;
            }

            return list[list.Count - 1].stageIndex;
        }

        public StoryStageData GetFinalStage(int factionID)
        {
            if (!_stagesByFactionID.TryGetValue(factionID, out List<StoryStageData> list) || list.Count == 0)
            {
                return null;
            }

            return list[list.Count - 1];
        }

        private bool LoadRoutes(IReadOnlyList<FactionRouteData> routes, int factionNeutralID)
        {
            if (routes == null || routes.Count == 0)
            {
                Debug.LogWarning("[FactionStoryTableLoader] FactionRouteTable is empty.");
                return false;
            }

            for (int i = 0; i < routes.Count; i++)
            {
                FactionRouteData row = routes[i];
                if (row == null)
                {
                    continue;
                }

                if (row.factionID == factionNeutralID)
                {
                    continue;
                }

                if (row.factionID <= 0)
                {
                    Debug.LogWarning($"[FactionStoryTableLoader] Invalid factionID={row.factionID}, skip.");
                    continue;
                }

                if (_routeByFactionID.ContainsKey(row.factionID))
                {
                    throw new FactionStoryTableValidationException($"Duplicate factionID in FactionRouteTable: {row.factionID}");
                }

                _routeByFactionID[row.factionID] = row;
            }

            return true;
        }

        private bool LoadStages(IReadOnlyList<StoryStageData> stages, int factionNeutralID)
        {
            if (stages == null || stages.Count == 0)
            {
                Debug.LogWarning("[FactionStoryTableLoader] StoryStageTable is empty.");
                return false;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                StoryStageData row = stages[i];
                if (row == null)
                {
                    continue;
                }

                if (row.factionID == factionNeutralID)
                {
                    Debug.LogWarning($"[FactionStoryTableLoader] stageID={row.stageID} uses neutral faction, skip.");
                    continue;
                }

                if (!_routeByFactionID.ContainsKey(row.factionID))
                {
                    Debug.LogWarning($"[FactionStoryTableLoader] stageID={row.stageID} references unknown factionID={row.factionID}, skip.");
                    continue;
                }

                if (row.stageID <= 0 || row.missionID <= 0 || row.stageIndex <= 0)
                {
                    throw new FactionStoryTableValidationException($"Invalid stage row: stageID={row.stageID}, missionID={row.missionID}, stageIndex={row.stageIndex}");
                }

                if (_stageByID.ContainsKey(row.stageID))
                {
                    throw new FactionStoryTableValidationException($"Duplicate stageID in StoryStageTable: {row.stageID}");
                }

                if (_stageByMissionID.ContainsKey(row.missionID))
                {
                    throw new FactionStoryTableValidationException($"Duplicate missionID in StoryStageTable: {row.missionID}");
                }

                _stageByID[row.stageID] = row;
                _stageByMissionID[row.missionID] = row;

                if (!_stagesByFactionID.TryGetValue(row.factionID, out List<StoryStageData> list))
                {
                    list = new List<StoryStageData>(8);
                    _stagesByFactionID[row.factionID] = list;
                }

                list.Add(row);
            }

            return true;
        }

        private void ValidateFactionStageContinuity()
        {
            foreach (KeyValuePair<int, List<StoryStageData>> pair in _stagesByFactionID)
            {
                List<StoryStageData> list = pair.Value;
                list.Sort((a, b) => a.stageIndex.CompareTo(b.stageIndex));

                int expectedStageIndex = 1;
                int prevThreshold = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    StoryStageData row = list[i];
                    if (row.stageIndex != expectedStageIndex)
                    {
                        throw new FactionStoryTableValidationException(
                            $"Faction {pair.Key} has non-continuous stageIndex. expected={expectedStageIndex}, actual={row.stageIndex}");
                    }

                    if (row.scoreThreshold <= prevThreshold)
                    {
                        throw new FactionStoryTableValidationException(
                            $"Faction {pair.Key} scoreThreshold must be strictly increasing. stageID={row.stageID}");
                    }

                    prevThreshold = row.scoreThreshold;
                    expectedStageIndex++;
                }
            }
        }
    }
}
