// 管理 userScale / baseResolutionScale / effectiveScale 三個值，
// 並負責向所有已註冊的 callback 推送 effectiveScale 變更通知。
// 不持有 MonoBehaviour 生命週期；由 DesktopWindowService 持有。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGuild.UI.Platform.Win32
{
    /// <summary>
    /// 視窗縮放控制器。
    /// <para>職責：</para>
    /// <list type="bullet">
    /// <item>維護 <c>userScale</c>（玩家調整）和 <c>baseResolutionScale</c>（解析度自適應）</item>
    /// <item>計算 <c>effectiveScale = baseResolutionScale × userScale</c></item>
    /// <item>向所有已註冊的 <see cref="Action{Single}"/> callback 推送 effectiveScale 變更</item>
    /// </list>
    /// </summary>
    internal sealed class WindowScaleController
    {
        private readonly P01Tuning _tuning;

        private float _userScale = 1.0f;
        private float _baseResolutionScale = 1.0f;

        // callback 清單；不去重（FSD §7 EC：重複註冊由 P-02 端確保只呼叫一次）
        private readonly List<Action<float>> _scaleListeners = new List<Action<float>>(2);

        /// <summary>當前玩家設定縮放（未 clamp 版本由外層 SetUserScale 保證）。</summary>
        public float UserScale => _userScale;

        /// <summary>當前解析度自適應縮放。</summary>
        public float BaseResolutionScale => _baseResolutionScale;

        /// <summary>當前有效縮放 = baseResolutionScale × userScale（FSD §4 公式）。</summary>
        public float EffectiveScale => _baseResolutionScale * _userScale;

        /// <summary>
        /// 建立控制器實例。
        /// </summary>
        /// <param name="tuning">P01Tuning ScriptableObject（由 DesktopWindowService SerializeField 注入）</param>
        public WindowScaleController(P01Tuning tuning)
        {
            _tuning = tuning != null ? tuning : throw new ArgumentNullException(nameof(tuning));
        }

        /// <summary>
        /// 設定玩家縮放（值由外層呼叫者 clamp 後傳入）。
        /// 設定後重算 effectiveScale 並推送所有 callback。
        /// </summary>
        public void SetUserScale(float clampedValue)
        {
            _userScale = clampedValue;
            DispatchScaleListeners();
        }

        /// <summary>
        /// 依螢幕可用高度重算 baseResolutionScale，並推送所有 callback。
        /// 應在 WM_DISPLAYCHANGE / SwitchTargetScreen / 初始化時呼叫。
        /// </summary>
        /// <param name="workingAreaHeight">目標螢幕可用桌面高度（像素）</param>
        public void UpdateBaseResolutionScale(int workingAreaHeight)
        {
            int referenceHeight = _tuning.ReferenceHeight;
            if (referenceHeight <= 0)
            {
                Debug.LogError("[P-01] P01Tuning.ReferenceHeight 無效（<= 0），跳過 scale 更新。");
                return;
            }

            _baseResolutionScale = (float)workingAreaHeight / referenceHeight;
            DispatchScaleListeners();
        }

        /// <summary>回傳當前 effectiveScale（等同 <see cref="EffectiveScale"/> 屬性）。</summary>
        public float GetEffectiveScale() => EffectiveScale;

        /// <summary>
        /// 新增 effectiveScale 變更監聽器。
        /// 不去重；呼叫端（DesktopWindowService）在 RegisterEffectiveScaleListener 後立即 invoke 一次。
        /// </summary>
        public void AddListener(Action<float> callback)
        {
            if (callback == null)
            {
                return;
            }

            _scaleListeners.Add(callback);
        }

        /// <summary>向所有已註冊 callback 推送當前 effectiveScale。</summary>
        private void DispatchScaleListeners()
        {
            float current = EffectiveScale;
            // 倒序遍歷避免 callback 內部 Remove 影響索引
            for (int i = 0; i < _scaleListeners.Count; i++)
            {
                try
                {
                    _scaleListeners[i]?.Invoke(current);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[P-01] effectiveScale callback 拋例外：{ex.GetType().Name} - {ex.Message}");
                }
            }
        }
    }
}
