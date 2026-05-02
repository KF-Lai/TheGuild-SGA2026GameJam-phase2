using System.Collections.Generic;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Trait;
using UnityEngine;

namespace TheGuild.Gameplay.Adventurer
{
    /// <summary>
    /// C-02-B 冒險者工廠：建立 AdventurerInstance（模板路徑 / 隨機路徑）。
    /// FSD §4.3（C-02-B）/ §5.4 流程 A。
    /// </summary>
    public sealed class AdventurerFactory
    {
        private static bool _d01StubWarningLogged;

        private readonly AdventurerTemplateLoader _loader;
        private readonly AdventurerRoster _roster;

        public AdventurerFactory(AdventurerTemplateLoader loader, AdventurerRoster roster)
        {
            _loader = loader;
            _roster = roster;
        }

        /// <summary>
        /// 依模板建立冒險者實例；isUnique 驗證、raceID 隨機、特質建立全在此執行。
        /// 模板不存在或驗證失敗回傳 null。
        /// FSD §5.4 流程 A。
        /// </summary>
        public AdventurerInstance CreateFromTemplate(int templateID)
        {
            // 步驟 1：取得模板
            AdventurerTemplate tmpl = _loader.GetTemplate(templateID);
            if (tmpl == null)
            {
                Debug.LogError($"[AdventurerFactory] CreateFromTemplate: templateID={templateID} 找不到，回傳 null。");
                return null;
            }

            // 步驟 2：isUnique 驗證（含 Dead 狀態）
            if (tmpl.isUnique == 1)
            {
                IReadOnlyList<AdventurerInstance> roster = _roster.GetRoster();
                for (int i = 0; i < roster.Count; i++)
                {
                    if (roster[i].templateID == templateID)
                    {
                        Debug.LogWarning($"[AdventurerFactory] CreateFromTemplate: templateID={templateID} 為 isUnique=1，名冊中已存在（instanceID={roster[i].instanceID}），回傳 null。");
                        return null;
                    }
                }
            }

            // 步驟 3：分配 instanceID，填入靜態欄位
            AdventurerInstance instance = new AdventurerInstance
            {
                instanceID = _roster.AllocateInstanceID(),
                templateID = templateID,
                name = tmpl.name,
                rank = tmpl.rank,
                professionID = tmpl.professionID,
            };

            // 步驟 4：raceID
            instance.raceID = tmpl.raceID != 0
                ? tmpl.raceID
                : RaceService.Instance.RollRace(tmpl.professionID);

            // 步驟 5：BuildTraitList
            instance.traitIDs = BuildTraitList(tmpl.fixedTraitIDs, tmpl.randomTraitGroupIDs);

            // 步驟 6：factionID
            instance.factionID = tmpl.factionID;

            instance.gender = 0; // v1.1：具名 NPC 預設 0；如未來 AdventurerTemplate 加 gender 欄位再對接
            instance.bio = tmpl.bio ?? string.Empty; // v1.1：具名 NPC 靜態 bio，無則空字串

            // 步驟 7：初始狀態
            instance.status = AdventurerStatus.Idle;
            instance.currentMissionID = 0;
            instance.woundedUntilTimestamp = 0;
            instance.idleSinceTimestamp = 0;
            instance.lastAutoPickupTimestamp = 0;

            // 步驟 8：回傳（不加入名冊，由呼叫端負責 AddAdventurer）
            return instance;
        }

        /// <summary>
        /// 建立隨機冒險者實例；分配 instanceID，但不加入名冊。
        /// FSD §5.1 IAdventurerFactory / GDD §4.3a。
        /// </summary>
        public AdventurerInstance CreateRandomInstance(string rank, int professionID, int raceID, int[] traitIDs)
        {
            AdventurerInstance instance = new AdventurerInstance
            {
                instanceID = _roster.AllocateInstanceID(),
                templateID = 0,
                // v1.1：D-01 Character Content Database 尚未實作，使用降級 stub
                // GDD §4.3a：服務未就緒時 name="冒險者" / gender=0 / bio=""，並 LogWarning 一次
                // 待 D-01 (NamePool/BioPool) 實作後切換為：
                //   (instance.name, instance.gender) = D01.PickRandomNameWithGender(raceID)
                //   instance.bio = D01.GetRandomBio(raceID, professionID, instance.name, instance.gender)
                name = "冒險者",
                gender = 0,
                bio = string.Empty,
                rank = rank,
                professionID = professionID,
                raceID = raceID,
                traitIDs = traitIDs ?? System.Array.Empty<int>(),
                factionID = 0,
                status = AdventurerStatus.Idle,
                currentMissionID = 0,
                woundedUntilTimestamp = 0,
                idleSinceTimestamp = 0,
                lastAutoPickupTimestamp = 0,
            };

            if (!_d01StubWarningLogged)
            {
                _d01StubWarningLogged = true;
                Debug.LogWarning("[AdventurerFactory] CreateRandomInstance: D-01 Character Content Database is not implemented; using stub name/gender/bio fallback.");
            }

            return instance;
        }

        // ── 私有 Helper ──────────────────────────────────────────────────────────

        /// <summary>
        /// 建立特質合集：固定特質 + 各隨機群組各抽一次，最終去重。
        /// FSD §8.3 B-03：不對外公開。
        /// GDD §4.4 BuildTraitList。
        /// </summary>
        private int[] BuildTraitList(string[] fixedTraitIDTokens, string[] randomTraitGroupIDTokens)
        {
            List<int> result = new List<int>();

            // 固定特質：去除 null sentinel（0）
            if (fixedTraitIDTokens != null)
            {
                for (int i = 0; i < fixedTraitIDTokens.Length; i++)
                {
                    string token = fixedTraitIDTokens[i];
                    if (string.IsNullOrEmpty(token) || token == "0")
                    {
                        continue;
                    }

                    if (!int.TryParse(token, out int traitID) || traitID <= 0)
                    {
                        continue;
                    }

                    // 驗證 traitID 存在（GDD §5.1）
                    if (TraitService.Instance != null && TraitService.Instance.GetTrait(traitID) == null)
                    {
                        Debug.LogWarning($"[AdventurerFactory] BuildTraitList: traitID={traitID} 在 TraitTable 找不到，過濾。");
                        continue;
                    }

                    result.Add(traitID);
                }
            }

            // 隨機群組：去除 null sentinel（0），各群組呼叫 RollTraits
            if (randomTraitGroupIDTokens != null && TraitService.Instance != null)
            {
                for (int i = 0; i < randomTraitGroupIDTokens.Length; i++)
                {
                    string token = randomTraitGroupIDTokens[i];
                    if (string.IsNullOrEmpty(token) || token == "0")
                    {
                        continue;
                    }

                    if (!int.TryParse(token, out int groupID) || groupID <= 0)
                    {
                        continue;
                    }

                    TraitGroupData group = TraitService.Instance.GetTraitGroup(groupID);
                    if (group == null)
                    {
                        Debug.LogWarning($"[AdventurerFactory] BuildTraitList: groupID={groupID} 在 TraitGroupTable 找不到，跳過。");
                        continue;
                    }

                    int[] picked = TraitService.Instance.RollTraits(group);
                    for (int j = 0; j < picked.Length; j++)
                    {
                        result.Add(picked[j]);
                    }
                }
            }

            // Distinct（去重）
            HashSet<int> seen = new HashSet<int>();
            List<int> distinct = new List<int>(result.Count);
            for (int i = 0; i < result.Count; i++)
            {
                if (seen.Add(result[i]))
                {
                    distinct.Add(result[i]);
                }
            }

            return distinct.ToArray();
        }
    }
}
