using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheGuild.Core.Data
{
    /// <summary>
    /// 遊戲資料載入與查詢中心。
    /// 由下游系統在 Awake 前註冊表格，DataManager 於 Awake 載入後提供唯讀查詢。
    /// </summary>
    public sealed class DataManager : MonoBehaviour
    {
        private const string DATA_TABLE_PATH = "Data/Tables/";
        private const char LIST_SEPARATOR = '|';
        private const char COMMENT_PREFIX = '#';
        private const bool LOG_MISSING_KEY = true;

        private static readonly List<TableRegistration> _pendingRegistrations = new List<TableRegistration>();
        private static readonly Dictionary<string, int> _registrationIndexByTableName =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static bool _loaded;
        private static Func<string, string> _testTableTextProvider;

        private readonly Dictionary<Type, Dictionary<string, object>> _tableCache =
            new Dictionary<Type, Dictionary<string, object>>();
        private readonly Dictionary<Type, string> _tableNameByType =
            new Dictionary<Type, string>();
        private readonly Dictionary<string, string> _systemConstants =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, GroupPoolData> _groupPools =
            new Dictionary<string, GroupPoolData>(StringComparer.Ordinal);

        private System.Random _random = new System.Random();

        /// <summary>
        /// 目前 DataManager 全域實例。
        /// </summary>
        public static DataManager Instance { get; private set; }

        /// <summary>
        /// 註冊一般資料表，供 DataManager 在 Awake 載入。
        /// </summary>
        public static void RegisterTable<T>(string tableName) where T : class, new()
        {
            RegisterInternal(typeof(T), tableName, isSystemConstants: false, isGroupPool: false, keepExistingOnDuplicate: false);
        }

        /// <summary>
        /// 註冊 SystemConstants 資料表（key-value 特殊表）。
        /// </summary>
        public static void RegisterSystemConstantsTable(string tableName)
        {
            RegisterInternal(typeof(SystemConstantsMarker), tableName, isSystemConstants: true, isGroupPool: false, keepExistingOnDuplicate: true);
        }

        /// <summary>
        /// 註冊群組池資料表（GroupPoolData 或其子型別）。
        /// </summary>
        public static void RegisterGroupPoolTable<T>(string tableName) where T : class, new()
        {
            RegisterInternal(typeof(T), tableName, isSystemConstants: false, isGroupPool: true, keepExistingOnDuplicate: false);
        }

        /// <summary>
        /// 以字串主鍵查詢單筆資料。
        /// </summary>
        public T Get<T>(string id) where T : class
        {
            if (!TryGetLoadedTable(typeof(T), out Dictionary<string, object> table, out string tableName))
            {
                return null;
            }

            if (string.IsNullOrEmpty(id))
            {
                if (LOG_MISSING_KEY)
                {
                    Debug.LogWarning($"[DataManager] 查詢 ID 為空字串，型別={typeof(T).Name}，表格={tableName}");
                }

                return null;
            }

            if (table.TryGetValue(id, out object raw))
            {
                return raw as T;
            }

            if (LOG_MISSING_KEY)
            {
                Debug.LogWarning($"[DataManager] 找不到資料：ID={id}，型別={typeof(T).Name}，表格={tableName}");
            }

            return null;
        }

        /// <summary>
        /// 以整數主鍵查詢單筆資料（內部轉為字串主鍵）。
        /// </summary>
        public T Get<T>(int id) where T : class
        {
            return Get<T>(id.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 取得某型別資料表的所有資料。
        /// </summary>
        public IReadOnlyList<T> GetAll<T>() where T : class
        {
            if (!TryGetLoadedTable(typeof(T), out Dictionary<string, object> table, out _))
            {
                return new List<T>(0);
            }

            List<T> result = new List<T>(table.Count);
            foreach (KeyValuePair<string, object> pair in table)
            {
                if (pair.Value is T item)
                {
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// 取得符合條件的資料列。
        /// </summary>
        public IReadOnlyList<T> GetWhere<T>(Predicate<T> predicate) where T : class
        {
            if (predicate == null)
            {
                Debug.LogError($"[DataManager] GetWhere<{typeof(T).Name}> predicate 不可為 null");
                return new List<T>(0);
            }

            IReadOnlyList<T> source = GetAll<T>();
            List<T> result = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                T item = source[i];
                if (predicate(item))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// 取得 SystemConstants 浮點數常數值。
        /// </summary>
        public float GetFloat(string key)
        {
            if (!TryGetSystemConstantRawValue(key, out string value))
            {
                return 0f;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }

            Debug.LogError($"[DataManager] SystemConstants 浮點解析失敗：key={key}，value={value}");
            return 0f;
        }

        /// <summary>
        /// 取得 SystemConstants 整數常數值。
        /// </summary>
        public int GetInt(string key)
        {
            if (!TryGetSystemConstantRawValue(key, out string value))
            {
                return 0;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            Debug.LogError($"[DataManager] SystemConstants 整數解析失敗：key={key}，value={value}");
            return 0;
        }

        /// <summary>
        /// 依群組池設定抽取資料（支援 weighted / uniform，無置回）。
        /// </summary>
        public List<T> PickRandom<T>(string groupID, int? overrideCount = null) where T : class
        {
            if (string.IsNullOrEmpty(groupID))
            {
                Debug.LogError($"[DataManager] PickRandom<{typeof(T).Name}> groupID 不可為空");
                return new List<T>(0);
            }

            if (!_groupPools.TryGetValue(groupID, out GroupPoolData pool))
            {
                Debug.LogError($"[DataManager] 找不到群組池：groupID={groupID}，型別={typeof(T).Name}");
                return new List<T>(0);
            }

            if (pool.memberIDs == null || pool.memberIDs.Length == 0)
            {
                return new List<T>(0);
            }

            int targetCount = overrideCount.HasValue ? overrideCount.Value : pool.pickCount;
            if (targetCount <= 0)
            {
                return new List<T>(0);
            }

            List<T> candidates = new List<T>(pool.memberIDs.Length);
            List<float> weights = new List<float>(pool.memberIDs.Length);

            for (int i = 0; i < pool.memberIDs.Length; i++)
            {
                string memberId = pool.memberIDs[i];
                T item = Get<T>(memberId);
                if (item == null)
                {
                    Debug.LogWarning($"[DataManager] 群組池成員不存在：groupID={groupID}，memberID={memberId}，型別={typeof(T).Name}");
                    continue;
                }

                candidates.Add(item);
                weights.Add(GetWeightAt(pool, i));
            }

            List<T> result = RandomPool.PickWithoutReplacement(candidates, targetCount, pool.pickMode, weights, _random, out bool fallbackToUniform);
            if (fallbackToUniform && string.Equals(pool.pickMode, "weighted", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[DataManager] 群組池權重總和為 0，退化為 uniform：groupID={groupID}，型別={typeof(T).Name}");
            }

            return result;
        }

        /// <summary>
        /// 依條件篩選後，從子集合中進行等機率無置回抽樣。
        /// </summary>
        public List<T> PickRandomWhere<T>(Predicate<T> predicate, int count) where T : class
        {
            if (count <= 0)
            {
                return new List<T>(0);
            }

            IReadOnlyList<T> filtered = GetWhere(predicate);
            return RandomPool.PickWithoutReplacement(filtered, count, "uniform", null, _random, out _);
        }

        internal static void SetTableTextProviderForTests(Func<string, string> provider)
        {
            _testTableTextProvider = provider;
        }

        internal void SetRandomForTests(System.Random random)
        {
            if (random != null)
            {
                _random = random;
            }
        }

        internal void InitializeForTests()
        {
            InitializeInstance();
        }

        internal static void ResetForTests()
        {
            _pendingRegistrations.Clear();
            _registrationIndexByTableName.Clear();
            _loaded = false;
            _testTableTextProvider = null;

            if (Instance != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(Instance.gameObject);
#else
                Destroy(Instance.gameObject);
#endif
                Instance = null;
            }
        }

        private void Awake()
        {
            InitializeInstance();
        }

        private void InitializeInstance()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
#if UNITY_EDITOR
                    DestroyImmediate(gameObject);
#endif
                }
                return;
            }

            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
            LoadAllTables();
            _loaded = true;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void LoadAllTables()
        {
            for (int i = 0; i < _pendingRegistrations.Count; i++)
            {
                TableRegistration registration = _pendingRegistrations[i];
                string csvText = LoadTableText(registration.TableName);

                if (csvText == null)
                {
                    Debug.LogError($"[DataManager] 表格 {registration.TableName} 未找到");
                    continue;
                }

                if (registration.IsSystemConstants)
                {
                    Dictionary<string, string> constants = CsvParser.ParseSystemConstants(csvText, registration.TableName, COMMENT_PREFIX);
                    MergeSystemConstants(constants, registration.TableName);
                    continue;
                }

                Dictionary<string, object> parsed = CsvParser.Parse(csvText, registration.DataType, registration.TableName, LIST_SEPARATOR, COMMENT_PREFIX);
                _tableCache[registration.DataType] = parsed;
                _tableNameByType[registration.DataType] = registration.TableName;

                if (registration.IsGroupPool)
                {
                    CacheGroupPools(parsed, registration.TableName);
                }
            }
        }

        private static void RegisterInternal(Type dataType, string tableName, bool isSystemConstants, bool isGroupPool, bool keepExistingOnDuplicate)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                Debug.LogError("[DataManager] 註冊失敗：tableName 不可為空");
                return;
            }

            if (_loaded)
            {
                Debug.LogError($"DataManager 已載入，表格 {tableName} 註冊失敗");
                return;
            }

            TableRegistration registration = new TableRegistration(dataType, tableName, isSystemConstants, isGroupPool);

            if (_registrationIndexByTableName.TryGetValue(tableName, out int index))
            {
                TableRegistration existing = _pendingRegistrations[index];
                if (keepExistingOnDuplicate && existing.IsSystemConstants && registration.IsSystemConstants)
                {
                    Debug.LogWarning($"[DataManager] SystemConstants 表格 {tableName} 已註冊，重複註冊已忽略");
                    return;
                }

                Debug.LogWarning(
                    $"[DataManager] 表格 {tableName} 已由 {GetRegistrationTypeDisplayName(existing)} 註冊，後續註冊以 {GetRegistrationTypeDisplayName(registration)} 覆蓋");
                _pendingRegistrations[index] = registration;
                return;
            }

            _registrationIndexByTableName[tableName] = _pendingRegistrations.Count;
            _pendingRegistrations.Add(registration);
        }

        private static string GetRegistrationTypeDisplayName(TableRegistration registration)
        {
            return registration.IsSystemConstants ? "SystemConstants" : registration.DataType.Name;
        }

        private float GetWeightAt(GroupPoolData pool, int index)
        {
            if (pool.weights == null || index >= pool.weights.Length)
            {
                return 1f;
            }

            return pool.weights[index];
        }

        private void CacheGroupPools(Dictionary<string, object> parsed, string tableName)
        {
            foreach (KeyValuePair<string, object> pair in parsed)
            {
                GroupPoolData group = pair.Value as GroupPoolData;
                if (group == null)
                {
                    Debug.LogError($"[DataManager] 群組池表型別需為 GroupPoolData 或子型別：表格={tableName}，PK={pair.Key}");
                    continue;
                }

                string groupID = string.IsNullOrEmpty(group.groupID) ? pair.Key : group.groupID;
                if (_groupPools.ContainsKey(groupID))
                {
                    Debug.LogWarning($"[DataManager] 群組池 ID 重複，後者覆蓋前者：groupID={groupID}，表格={tableName}");
                }

                _groupPools[groupID] = group;
            }
        }

        private void MergeSystemConstants(Dictionary<string, string> constants, string tableName)
        {
            foreach (KeyValuePair<string, string> pair in constants)
            {
                if (_systemConstants.ContainsKey(pair.Key))
                {
                    Debug.LogWarning($"[DataManager] SystemConstants key 重複，後者覆蓋前者：key={pair.Key}，表格={tableName}");
                }

                _systemConstants[pair.Key] = pair.Value;
            }
        }

        private bool TryGetSystemConstantRawValue(string key, out string value)
        {
            value = null;

            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[DataManager] SystemConstants 查詢失敗：key 不可為空");
                return false;
            }

            if (!_systemConstants.TryGetValue(key, out value))
            {
                Debug.LogError($"[DataManager] SystemConstants 查無 key：{key}");
                return false;
            }

            return true;
        }

        private bool TryGetLoadedTable(Type dataType, out Dictionary<string, object> table, out string tableName)
        {
            table = null;
            tableName = string.Empty;

            if (!_loaded)
            {
                Debug.LogError($"[DataManager] 尚未完成載入，無法查詢型別 {dataType.Name}");
                return false;
            }

            if (_tableCache.TryGetValue(dataType, out table))
            {
                if (_tableNameByType.TryGetValue(dataType, out string loadedName))
                {
                    tableName = loadedName;
                }

                return true;
            }

            tableName = FindRegisteredTableName(dataType);
            if (string.IsNullOrEmpty(tableName))
            {
                tableName = "<tableName>";
            }

            Debug.LogError(
                $"[DataManager] 型別 {dataType.Name} 未註冊，請於下游系統 [RuntimeInitializeOnLoadMethod] 中呼叫 DataManager.RegisterTable<{dataType.Name}>(\"{tableName}\")");
            return false;
        }

        private static string FindRegisteredTableName(Type dataType)
        {
            for (int i = 0; i < _pendingRegistrations.Count; i++)
            {
                if (_pendingRegistrations[i].DataType == dataType)
                {
                    return _pendingRegistrations[i].TableName;
                }
            }

            return string.Empty;
        }

        private string LoadTableText(string tableName)
        {
            if (_testTableTextProvider != null)
            {
                return _testTableTextProvider(tableName);
            }

            TextAsset asset = Resources.Load<TextAsset>(DATA_TABLE_PATH + tableName);
            return asset == null ? null : asset.text;
        }

        private readonly struct TableRegistration
        {
            public TableRegistration(Type dataType, string tableName, bool isSystemConstants, bool isGroupPool)
            {
                DataType = dataType;
                TableName = tableName;
                IsSystemConstants = isSystemConstants;
                IsGroupPool = isGroupPool;
            }

            public Type DataType { get; }
            public string TableName { get; }
            public bool IsSystemConstants { get; }
            public bool IsGroupPool { get; }
        }

        private sealed class SystemConstantsMarker
        {
        }
    }
}
