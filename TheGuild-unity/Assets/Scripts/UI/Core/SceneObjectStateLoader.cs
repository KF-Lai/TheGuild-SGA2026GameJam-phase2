using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.UI.Core
{
    [Serializable]
    public sealed class SceneObjectStateTable
    {
        public int rowID;
        public string objectID;
        public string stageCondition;
        public string spriteVariant;
        public string dialogueKey;
        public int priority;
        public string audioCue;
        public int factionID;
    }

    [DefaultExecutionOrder(-140)]
    public sealed class SceneObjectStateLoader : MonoBehaviour
    {
        private const string TABLE_NAME = "SceneObjectStateTable";

        private readonly Dictionary<string, List<SceneObjectStateTable>> _rowsByObjectID =
            new Dictionary<string, List<SceneObjectStateTable>>(StringComparer.Ordinal);

        public static SceneObjectStateLoader Instance { get; private set; }
        public bool IsLoaded { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<SceneObjectStateTable>(TABLE_NAME);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Initialize()
        {
            _rowsByObjectID.Clear();
            IsLoaded = false;

            try
            {
                IReadOnlyList<SceneObjectStateTable> rows = DataManager.Instance.GetAll<SceneObjectStateTable>();
                for (int i = 0; i < rows.Count; i++)
                {
                    SceneObjectStateTable row = rows[i];
                    if (row == null || string.IsNullOrEmpty(row.objectID))
                    {
                        continue;
                    }

                    if (!_rowsByObjectID.TryGetValue(row.objectID, out List<SceneObjectStateTable> list))
                    {
                        list = new List<SceneObjectStateTable>();
                        _rowsByObjectID[row.objectID] = list;
                    }

                    list.Add(row);
                }

                foreach (List<SceneObjectStateTable> list in _rowsByObjectID.Values)
                {
                    list.Sort((a, b) => b.priority.CompareTo(a.priority));
                }

                IsLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneObjectStateLoader] SceneObjectStateTable.csv 載入失敗：{ex.GetType().Name} - {ex.Message}");
                _rowsByObjectID.Clear();
            }
        }

        public IReadOnlyList<SceneObjectStateTable> GetRows(string objectID)
        {
            return _rowsByObjectID.TryGetValue(objectID, out List<SceneObjectStateTable> rows)
                ? rows
                : Array.Empty<SceneObjectStateTable>();
        }
    }
}
