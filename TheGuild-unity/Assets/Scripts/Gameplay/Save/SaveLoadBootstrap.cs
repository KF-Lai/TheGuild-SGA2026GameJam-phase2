using System;
using System.Collections.Generic;
using System.IO;
using TheGuild.Core.SaveContract;
using UnityEngine;

namespace TheGuild.Gameplay.Save
{
    public static class SaveLoadBootstrap
    {
        public static BootstrapResult Execute(
            IReadOnlyList<ISaveable> saveables,
            string directoryPath,
            string saveFileName,
            string backupPrefix,
            int backupCount)
        {
            List<ISaveable> ordered = SortByRestoreOrder(saveables);

            if (!SaveFileIO.HasAnySaveFile(directoryPath, saveFileName, backupPrefix, backupCount))
            {
                ResetAllSaveables(ordered);
                return new BootstrapResult(false, -1, false, null);
            }

            Exception lastFailure = null;

            for (int candidateIndex = 0; candidateIndex <= backupCount; candidateIndex++)
            {
                string candidatePath = candidateIndex == 0
                    ? SaveFileIO.BuildSavePath(directoryPath, saveFileName)
                    : SaveFileIO.BuildBackupPath(directoryPath, backupPrefix, candidateIndex);

                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                string raw;
                try
                {
                    raw = File.ReadAllText(candidatePath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveLoadBootstrap] Read failed candidate={candidatePath}: {ex.Message}");
                    lastFailure = ex;
                    continue;
                }

                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                try
                {
                    SaveDataRoot root = JsonUtility.FromJson<SaveDataRoot>(raw);
                    if (root == null || root.schemaMeta == null)
                    {
                        throw new InvalidOperationException("Root or schemaMeta is null.");
                    }

                    foreach (ISaveable saveable in ordered)
                    {
                        string ownerJson = ExtractOwnerJson(root, saveable.OwnerKey);
                        try
                        {
                            saveable.RestoreFromSave(ownerJson);
                        }
                        catch (Exception ex)
                        {
                            if (saveable.IsCritical)
                            {
                                throw new CriticalRestoreFailedException(saveable.OwnerKey, ex);
                            }

                            Debug.LogWarning($"[SaveLoadBootstrap] Degradable fallback owner={saveable.OwnerKey}: {ex.Message}");
                            saveable.InitializeAsNewGame();
                        }
                    }

                    if (candidateIndex > 0)
                    {
                        SaveFileIO.CopyLoadedCandidateToTerminalSave(candidatePath, directoryPath, saveFileName);
                    }

                    bool isGameOver = string.Equals(root.schemaMeta.gameOverState, "Over", StringComparison.Ordinal);
                    return new BootstrapResult(true, candidateIndex, isGameOver, null);
                }
                catch (CriticalRestoreFailedException ex)
                {
                    Debug.LogWarning($"[SaveLoadBootstrap] Critical failure on candidate {candidateIndex}, retry next: {ex.Message}");
                    ResetAllSaveables(ordered);
                    lastFailure = ex;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveLoadBootstrap] Parse failed candidate={candidatePath}: {ex.Message}");
                    ResetAllSaveables(ordered);
                    lastFailure = ex;
                }
            }

            ResetAllSaveables(ordered);
            return new BootstrapResult(true, -1, false, lastFailure);
        }

        public static List<ISaveable> SortByRestoreOrder(IReadOnlyList<ISaveable> saveables)
        {
            var order = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["f03Resources"] = 0,
                ["c06WorldDanger"] = 1,
                ["c02AdventurerRoster"] = 2,
                ["ft06Guild"] = 3,
                ["ft07Buildings"] = 4,
                ["ft02Dispatch"] = 5,
                ["ft08Gacha"] = 6,
                ["ft12Staff"] = 7,
                ["ft01Recruitment"] = 8,
                ["factionStorySaveData"] = 9,
                ["ft03Decision"] = 10
            };

            List<ISaveable> ordered = new List<ISaveable>(saveables.Count);
            for (int i = 0; i < saveables.Count; i++)
            {
                ordered.Add(saveables[i]);
            }

            ordered.Sort((a, b) =>
            {
                int oa = order.TryGetValue(a.OwnerKey, out int va) ? va : int.MaxValue;
                int ob = order.TryGetValue(b.OwnerKey, out int vb) ? vb : int.MaxValue;
                return oa.CompareTo(ob);
            });

            return ordered;
        }

        public static string ExtractOwnerJson(SaveDataRoot root, string ownerKey)
        {
            switch (ownerKey)
            {
                case "f03Resources": return root.f03Resources;
                case "c06WorldDanger": return root.c06WorldDanger;
                case "c02AdventurerRoster": return root.c02AdventurerRoster;
                case "ft06Guild": return root.ft06Guild;
                case "ft07Buildings": return root.ft07Buildings;
                case "ft02Dispatch": return root.ft02Dispatch;
                case "ft08Gacha": return root.ft08Gacha;
                case "ft12Staff": return root.ft12Staff;
                case "ft01Recruitment": return root.ft01Recruitment;
                case "factionStorySaveData": return root.factionStorySaveData;
                case "ft03Decision": return root.ft03Decision;
                default: return null;
            }
        }

        public static void ResetAllSaveables(IReadOnlyList<ISaveable> saveables)
        {
            for (int i = 0; i < saveables.Count; i++)
            {
                ResetWithRetry(saveables[i], 3);
            }
        }

        private static void ResetWithRetry(ISaveable saveable, int retryCount)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    saveable.InitializeAsNewGame();
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveLoadBootstrap] Reset failed owner={saveable.OwnerKey}, attempt={i + 1}, ex={ex.Message}");
                }
            }
        }
    }
}
