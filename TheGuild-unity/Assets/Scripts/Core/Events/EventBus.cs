using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGuild.Core.Events
{
    /// <summary>
    /// 全域事件匯流排（static，跨場景持續存在）。
    /// 多訂閱者觸發順序：FIFO（依訂閱先後執行）。
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _typedHandlers =
            new Dictionary<Type, List<Delegate>>();

        private static readonly Dictionary<string, List<Action>> _namedHandlers =
            new Dictionary<string, List<Action>>(StringComparer.Ordinal);

        /// <summary>
        /// 訂閱強型別事件。
        /// </summary>
        public static void Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            Type eventType = typeof(T);
            if (!_typedHandlers.TryGetValue(eventType, out List<Delegate> handlers))
            {
                handlers = new List<Delegate>(4);
                _typedHandlers[eventType] = handlers;
            }

            handlers.Add(handler);
        }

        /// <summary>
        /// 退訂強型別事件。
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            Type eventType = typeof(T);
            if (!_typedHandlers.TryGetValue(eventType, out List<Delegate> handlers))
            {
                return;
            }

            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                if (handlers[i] == (Delegate)handler)
                {
                    handlers.RemoveAt(i);
                }
            }

            if (handlers.Count == 0)
            {
                _typedHandlers.Remove(eventType);
            }
        }

        /// <summary>
        /// 發布強型別事件。
        /// </summary>
        public static void Publish<T>(T eventData)
        {
            Type eventType = typeof(T);
            if (!_typedHandlers.TryGetValue(eventType, out List<Delegate> handlers))
            {
                return;
            }

            Delegate[] snapshot = handlers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Action<T> callback = snapshot[i] as Action<T>;
                if (callback == null)
                {
                    continue;
                }

                try
                {
                    callback.Invoke(eventData);
                }
                catch (Exception ex)
                {
                    string handlerName = callback.Method?.Name ?? "<unknown>";
                    Debug.LogError(
                        $"[EventBus] Exception in Publish<{eventType.Name}> handler {handlerName}: " +
                        $"{ex.GetType().Name} - {ex.Message}"
                    );
                }
            }
        }

        /// <summary>
        /// 訂閱無 payload 字串鍵事件。
        /// </summary>
        public static void Subscribe(string eventName, Action handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
            {
                return;
            }

            if (!_namedHandlers.TryGetValue(eventName, out List<Action> handlers))
            {
                handlers = new List<Action>(4);
                _namedHandlers[eventName] = handlers;
            }

            handlers.Add(handler);
        }

        /// <summary>
        /// 退訂無 payload 字串鍵事件。
        /// </summary>
        public static void Unsubscribe(string eventName, Action handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
            {
                return;
            }

            if (!_namedHandlers.TryGetValue(eventName, out List<Action> handlers))
            {
                return;
            }

            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                if (handlers[i] == handler)
                {
                    handlers.RemoveAt(i);
                }
            }

            if (handlers.Count == 0)
            {
                _namedHandlers.Remove(eventName);
            }
        }

        /// <summary>
        /// 發布無 payload 字串鍵事件。
        /// </summary>
        public static void Publish(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            if (!_namedHandlers.TryGetValue(eventName, out List<Action> handlers))
            {
                return;
            }

            Action[] snapshot = handlers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Action callback = snapshot[i];
                if (callback == null)
                {
                    continue;
                }

                try
                {
                    callback.Invoke();
                }
                catch (Exception ex)
                {
                    string handlerName = callback.Method?.Name ?? "<unknown>";
                    Debug.LogError(
                        $"[EventBus] Exception in Publish(\"{eventName}\") handler {handlerName}: " +
                        $"{ex.GetType().Name} - {ex.Message}"
                    );
                }
            }
        }

        /// <summary>
        /// 測試用：清空所有訂閱。
        /// </summary>
        internal static void ClearAll()
        {
            _typedHandlers.Clear();
            _namedHandlers.Clear();
        }
    }
}
