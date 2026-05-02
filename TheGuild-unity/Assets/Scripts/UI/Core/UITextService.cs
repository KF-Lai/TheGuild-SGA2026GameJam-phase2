using System;
using System.Collections.Generic;
using TheGuild.Core.Data;
using UnityEngine;

namespace TheGuild.UI.Core
{
    /// <summary>
    /// UI 固定文字查詢服務。Jam 版固定使用 zhTW。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class UITextService : MonoBehaviour
    {
        private const string TABLE_NAME = "UIText";

        private readonly Dictionary<string, UITextRow> _rows =
            new Dictionary<string, UITextRow>(StringComparer.Ordinal);

        private TextLanguage _currentLanguage = TextLanguage.zhTW;
        private bool _loaded;

        public static UITextService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<UITextRow>(TABLE_NAME);
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
            if (_loaded)
            {
                return;
            }

            _rows.Clear();
            try
            {
                if (DataManager.Instance == null)
                {
                    Debug.LogError("[UITextService] DataManager.Instance is null.");
                    _loaded = false;
                    return;
                }

                IReadOnlyList<UITextRow> table = DataManager.Instance.GetAll<UITextRow>();
                for (int i = 0; i < table.Count; i++)
                {
                    UITextRow row = table[i];
                    if (row == null || string.IsNullOrEmpty(row.key))
                    {
                        continue;
                    }

                    _rows[row.key] = row;
                }

                _loaded = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UITextService] UIText.csv 載入失敗：{ex.GetType().Name} - {ex.Message}");
                _loaded = false;
            }
        }

        public string Lookup(string key, string fallback = null)
        {
            if (!_loaded)
            {
                Debug.LogWarning("[UITextService] Lookup called before Initialize.");
                return fallback ?? key;
            }

            if (!string.IsNullOrEmpty(key) && _rows.TryGetValue(key, out UITextRow row))
            {
                return SelectText(row);
            }

            Debug.LogError($"[UITextService] Missing UIText key: {key}");
            return fallback ?? key;
        }

        public string LookupFormat(string key, params object[] args)
        {
            string format = Lookup(key);
            return string.Format(format, args);
        }

        private string SelectText(UITextRow row)
        {
            if (_currentLanguage == TextLanguage.en && !string.IsNullOrEmpty(row.en))
            {
                return row.en;
            }

            return string.IsNullOrEmpty(row.zhTW) ? row.key : row.zhTW;
        }

        public sealed class UITextRow
        {
            public string key;
            public string zhTW;
            public string en;
        }
    }
}
