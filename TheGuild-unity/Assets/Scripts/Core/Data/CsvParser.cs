using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TheGuild.Core.Data
{
    /// <summary>
    /// CSV 文字解析器，負責將資料列反射綁定到目標型別。
    /// </summary>
    public static class CsvParser
    {
        private static readonly Dictionary<Type, Dictionary<string, MemberBinding>> _memberMapCache =
            new Dictionary<Type, Dictionary<string, MemberBinding>>();
        private static readonly object _memberMapLock = new object();

        /// <summary>
        /// 解析一般資料表（第一欄為主鍵）。
        /// </summary>
        public static Dictionary<string, object> Parse(
            string csvText,
            Type dataType,
            string tableName,
            char listSeparator = '|',
            char commentPrefix = '#')
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);

            if (dataType == null)
            {
                Debug.LogError($"[CsvParser] 解析失敗：資料型別為 null，表格={tableName}");
                return result;
            }

            List<CsvRow> rows = ParseRows(csvText);
            int headerIndex = FindHeaderIndex(rows, commentPrefix);
            if (headerIndex < 0)
            {
                return result;
            }

            List<string> headers = rows[headerIndex].Columns;
            Dictionary<string, MemberBinding> memberMap = GetMemberMap(dataType);

            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                CsvRow row = rows[i];
                if (IsIgnorableRow(row.Columns, commentPrefix))
                {
                    continue;
                }

                if (row.Columns.Count != headers.Count)
                {
                    Debug.LogWarning($"[CsvParser] 欄位數不符，已跳過：表格={tableName}，列號={row.LineNumber}");
                    continue;
                }

                string id = row.Columns[0].Trim();
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[CsvParser] 主鍵為空，已跳過：表格={tableName}，列號={row.LineNumber}");
                    continue;
                }

                object instance = Activator.CreateInstance(dataType);
                for (int col = 0; col < headers.Count; col++)
                {
                    string header = headers[col].Trim();
                    if (string.IsNullOrEmpty(header))
                    {
                        continue;
                    }

                    if (!memberMap.TryGetValue(header, out MemberBinding binding))
                    {
                        continue;
                    }

                    object converted = ConvertValue(row.Columns[col], binding.MemberType, tableName, row.LineNumber, header, listSeparator);
                    binding.SetValue(instance, converted);
                }

                if (result.ContainsKey(id))
                {
                    Debug.LogWarning($"[CsvParser] 主鍵重複，後者覆蓋前者：表格={tableName}，ID={id}");
                }

                result[id] = instance;
            }

            return result;
        }

        /// <summary>
        /// 解析 SystemConstants（key-value）資料表。
        /// </summary>
        public static Dictionary<string, string> ParseSystemConstants(string csvText, string tableName, char commentPrefix = '#')
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            List<CsvRow> rows = ParseRows(csvText);
            int headerIndex = FindHeaderIndex(rows, commentPrefix);
            if (headerIndex < 0)
            {
                return result;
            }

            List<string> headers = rows[headerIndex].Columns;
            int keyIndex = FindColumnIndex(headers, "key");
            int valueIndex = FindColumnIndex(headers, "value");

            if (keyIndex < 0 || valueIndex < 0)
            {
                Debug.LogError($"[CsvParser] SystemConstants 缺少 key/value 欄位：表格={tableName}");
                return result;
            }

            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                CsvRow row = rows[i];
                if (IsIgnorableRow(row.Columns, commentPrefix))
                {
                    continue;
                }

                if (row.Columns.Count != headers.Count)
                {
                    Debug.LogWarning($"[CsvParser] 欄位數不符，已跳過：表格={tableName}，列號={row.LineNumber}");
                    continue;
                }

                string key = row.Columns[keyIndex].Trim();
                string value = row.Columns[valueIndex].Trim();

                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogWarning($"[CsvParser] SystemConstants key 為空，已跳過：表格={tableName}，列號={row.LineNumber}");
                    continue;
                }

                if (result.ContainsKey(key))
                {
                    Debug.LogWarning($"[CsvParser] SystemConstants key 重複，後者覆蓋前者：表格={tableName}，key={key}");
                }

                result[key] = value;
            }

            return result;
        }

        private static Dictionary<string, MemberBinding> GetMemberMap(Type dataType)
        {
            lock (_memberMapLock)
            {
                if (_memberMapCache.TryGetValue(dataType, out Dictionary<string, MemberBinding> cached))
                {
                    return cached;
                }

                Dictionary<string, MemberBinding> map = new Dictionary<string, MemberBinding>(StringComparer.Ordinal);
                FieldInfo[] fields = dataType.GetFields(BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    map[field.Name] = MemberBinding.Create(field);
                }

                PropertyInfo[] properties = dataType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (!map.ContainsKey(property.Name))
                    {
                        map[property.Name] = MemberBinding.Create(property);
                    }
                }

                _memberMapCache[dataType] = map;
                return map;
            }
        }

        private static object ConvertValue(
            string raw,
            Type targetType,
            string tableName,
            int rowNumber,
            string columnName,
            char listSeparator)
        {
            string trimmed = raw == null ? string.Empty : raw.Trim();

            if (targetType == typeof(string))
            {
                return raw ?? string.Empty;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }

                return LogTypeParseErrorAndReturnDefault(tableName, rowNumber, columnName, trimmed, targetType);
            }

            if (targetType == typeof(long))
            {
                if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                {
                    return parsed;
                }

                return LogTypeParseErrorAndReturnDefault(tableName, rowNumber, columnName, trimmed, targetType);
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    return parsed;
                }

                return LogTypeParseErrorAndReturnDefault(tableName, rowNumber, columnName, trimmed, targetType);
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }

                return LogTypeParseErrorAndReturnDefault(tableName, rowNumber, columnName, trimmed, targetType);
            }

            if (targetType == typeof(bool))
            {
                if (TryParseBool(trimmed, out bool parsed))
                {
                    return parsed;
                }

                return LogTypeParseErrorAndReturnDefault(tableName, rowNumber, columnName, trimmed, targetType);
            }

            if (targetType == typeof(string[]))
            {
                return ParseStringArray(raw, listSeparator);
            }

            if (targetType == typeof(float[]))
            {
                return ParseFloatArray(trimmed, listSeparator, tableName, rowNumber, columnName);
            }

            Debug.LogError($"[CsvParser] 不支援的型別：表格={tableName}，列號={rowNumber}，欄位={columnName}，型別={targetType.Name}");
            return GetDefaultValue(targetType);
        }

        private static object LogTypeParseErrorAndReturnDefault(
            string tableName,
            int rowNumber,
            string columnName,
            string rawValue,
            Type targetType)
        {
            Debug.LogError(
                $"[CsvParser] 型別轉換失敗：表格={tableName}，列號={rowNumber}，欄位={columnName}，目標型別={targetType.Name}，原始值={rawValue}");
            return GetDefaultValue(targetType);
        }

        private static object GetDefaultValue(Type targetType)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        private static string[] ParseStringArray(string raw, char separator)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<string>();
            }

            string[] parts = raw.Split(separator);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }

            return parts;
        }

        private static float[] ParseFloatArray(
            string raw,
            char separator,
            string tableName,
            int rowNumber,
            string columnName)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<float>();
            }

            string[] parts = raw.Split(separator);
            float[] result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    Debug.LogError(
                        $"[CsvParser] float[] 解析失敗：表格={tableName}，列號={rowNumber}，欄位={columnName}，索引={i}，值={token}");
                    result[i] = 0f;
                    continue;
                }

                result[i] = parsed;
            }

            return result;
        }

        private static bool TryParseBool(string raw, out bool value)
        {
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1")
            {
                value = true;
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0")
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private static int FindColumnIndex(List<string> headers, string expectedName)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i].Trim(), expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindHeaderIndex(List<CsvRow> rows, char commentPrefix)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (!IsIgnorableRow(rows[i].Columns, commentPrefix))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsIgnorableRow(List<string> columns, char commentPrefix)
        {
            if (columns == null || columns.Count == 0)
            {
                return true;
            }

            bool hasContent = false;
            for (int i = 0; i < columns.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(columns[i]))
                {
                    hasContent = true;
                    break;
                }
            }

            if (!hasContent)
            {
                return true;
            }

            string first = columns[0].TrimStart();
            return first.Length > 0 && first[0] == commentPrefix;
        }

        private static List<CsvRow> ParseRows(string csvText)
        {
            List<CsvRow> rows = new List<CsvRow>();
            if (string.IsNullOrEmpty(csvText))
            {
                return rows;
            }

            List<string> currentRow = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;
            int lineNumber = 1;
            int rowStartLine = 1;

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < csvText.Length && csvText[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (!inQuotes && c == ',')
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Length = 0;
                    continue;
                }

                if (!inQuotes && (c == '\n' || c == '\r'))
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Length = 0;
                    rows.Add(new CsvRow(currentRow, rowStartLine));
                    currentRow = new List<string>();

                    if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                    {
                        i++;
                    }

                    lineNumber++;
                    rowStartLine = lineNumber;
                    continue;
                }

                currentField.Append(c);
            }

            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                rows.Add(new CsvRow(currentRow, rowStartLine));
            }

            return rows;
        }

        private readonly struct CsvRow
        {
            public CsvRow(List<string> columns, int lineNumber)
            {
                Columns = columns;
                LineNumber = lineNumber;
            }

            public List<string> Columns { get; }
            public int LineNumber { get; }
        }

        private sealed class MemberBinding
        {
            private readonly FieldInfo _field;
            private readonly PropertyInfo _property;

            private MemberBinding(FieldInfo field, PropertyInfo property, Type memberType)
            {
                _field = field;
                _property = property;
                MemberType = memberType;
            }

            public Type MemberType { get; }

            public static MemberBinding Create(FieldInfo field)
            {
                return new MemberBinding(field, null, field.FieldType);
            }

            public static MemberBinding Create(PropertyInfo property)
            {
                return new MemberBinding(null, property, property.PropertyType);
            }

            public void SetValue(object target, object value)
            {
                if (_field != null)
                {
                    _field.SetValue(target, value);
                    return;
                }

                _property.SetValue(target, value, null);
            }
        }
    }
}
