using System;
using System.IO;
using UnityEngine;

namespace TheGuild.Gameplay.Save
{
    public static class SaveFileIO
    {
        public static string BuildSavePath(string directoryPath, string saveFileName)
        {
            return Path.Combine(directoryPath, saveFileName);
        }

        public static string BuildBackupPath(string directoryPath, string backupPrefix, int backupIndex)
        {
            return Path.Combine(directoryPath, $"{backupPrefix}{backupIndex}.json");
        }

        public static bool HasAnySaveFile(string directoryPath, string saveFileName, string backupPrefix, int backupCount)
        {
            if (File.Exists(BuildSavePath(directoryPath, saveFileName)))
            {
                return true;
            }

            for (int i = 1; i <= backupCount; i++)
            {
                if (File.Exists(BuildBackupPath(directoryPath, backupPrefix, i)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryReadWithFallback(
            string directoryPath,
            string saveFileName,
            string backupPrefix,
            int backupCount,
            out string rawJson,
            out string loadedPath,
            out int loadedFromBackupIndex,
            out Exception failure)
        {
            rawJson = null;
            loadedPath = null;
            loadedFromBackupIndex = -1;
            failure = null;

            for (int i = 0; i <= backupCount; i++)
            {
                string candidatePath = i == 0
                    ? BuildSavePath(directoryPath, saveFileName)
                    : BuildBackupPath(directoryPath, backupPrefix, i);

                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                try
                {
                    string raw = File.ReadAllText(candidatePath);
                    if (string.IsNullOrEmpty(raw))
                    {
                        continue;
                    }

                    rawJson = raw;
                    loadedPath = candidatePath;
                    loadedFromBackupIndex = i;
                    return true;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Debug.LogWarning($"[SaveFileIO] Read failed: {candidatePath}, ex={ex.Message}");
                }
            }

            return false;
        }

        public static bool WriteSaveWithRotation(
            string directoryPath,
            string saveFileName,
            string backupPrefix,
            int backupCount,
            string content,
            out Exception failure)
        {
            failure = null;

            try
            {
                Directory.CreateDirectory(directoryPath);
                string terminalPath = BuildSavePath(directoryPath, saveFileName);

                RotateBackups(directoryPath, backupPrefix, backupCount);

                if (File.Exists(terminalPath))
                {
                    File.Copy(terminalPath, BuildBackupPath(directoryPath, backupPrefix, 1), true);
                }

                WriteTerminalAtomically(terminalPath, content);
                return true;
            }
            catch (Exception ex)
            {
                failure = ex;
                Debug.LogError($"[SaveFileIO] Write failed: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        public static void CopyLoadedCandidateToTerminalSave(string loadedCandidatePath, string directoryPath, string saveFileName)
        {
            string terminalPath = BuildSavePath(directoryPath, saveFileName);
            if (string.Equals(loadedCandidatePath, terminalPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(directoryPath);
            File.Copy(loadedCandidatePath, terminalPath, true);
        }

        public static bool DeleteAllSaveFiles(string directoryPath, string saveFileName, string backupPrefix, int backupCount)
        {
            bool any = false;
            string terminal = BuildSavePath(directoryPath, saveFileName);
            if (File.Exists(terminal))
            {
                File.Delete(terminal);
                any = true;
            }

            for (int i = 1; i <= backupCount; i++)
            {
                string backup = BuildBackupPath(directoryPath, backupPrefix, i);
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                    any = true;
                }
            }

            return any;
        }

        private static void RotateBackups(string directoryPath, string backupPrefix, int backupCount)
        {
            for (int i = backupCount; i >= 2; i--)
            {
                string from = BuildBackupPath(directoryPath, backupPrefix, i - 1);
                string to = BuildBackupPath(directoryPath, backupPrefix, i);
                if (!File.Exists(from))
                {
                    continue;
                }

                File.Copy(from, to, true);
            }
        }

        private static void WriteTerminalAtomically(string terminalPath, string content)
        {
            string tempPath = terminalPath + ".tmp";
            string bakPath = terminalPath + ".bak";

            File.WriteAllText(tempPath, content);

            if (File.Exists(terminalPath))
            {
                File.Replace(tempPath, terminalPath, bakPath, true);
                if (File.Exists(bakPath))
                {
                    File.Delete(bakPath);
                }
            }
            else
            {
                File.Move(tempPath, terminalPath);
            }
        }
    }
}
