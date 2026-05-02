// P01Tuning ScriptableObject — 定義 P-01 Desktop Transparent Window 的 5 個調校旋鈕。
// 對應 GDD §7 / FSD §6.2 / FSD §6.3 嚴禁寫死清單。
// asset 位置：Assets/UI/Tuning/P01Tuning.asset

using UnityEngine;

namespace TheGuild.UI.Platform.Win32
{
    /// <summary>
    /// P-01 調校旋鈕 ScriptableObject。
    /// <para>
    /// 所有值均由此 asset 提供，不允許在程式碼中寫死（FSD §6.3）。
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "TheGuild/UI/P01 Tuning", fileName = "P01Tuning")]
    public sealed class P01Tuning : ScriptableObject
    {
        [Header("解析度自適應（GDD §7）")]
        [Tooltip("baseResolutionScale 計算基準解析度高度（像素）。安全範圍：720~2160")]
        [SerializeField] private int _referenceHeight = 1080;

        [Tooltip("視窗高度佔可用桌面高度的比例。安全範圍：0.20~0.50")]
        [Range(0.20f, 0.50f)]
        [SerializeField] private float _windowHeightRatio = 0.30f;

        [Header("玩家縮放範圍（GDD §7）")]
        [Tooltip("玩家縮放下限。安全範圍：0.3~0.8（過低導致 UI 文字不可讀）")]
        [Range(0.3f, 0.8f)]
        [SerializeField] private float _userScaleMin = 0.5f;

        [Tooltip("玩家縮放上限。安全範圍：1.5~3.0（過高導致元素超出視窗範圍）")]
        [Range(1.5f, 3.0f)]
        [SerializeField] private float _userScaleMax = 2.0f;

        [Tooltip("玩家每次調整的步進幅度。安全範圍：0.05~0.25")]
        [Range(0.05f, 0.25f)]
        [SerializeField] private float _userScaleStep = 0.1f;

        // ──────────────────────────────────────────────────────
        //  唯讀屬性
        // ──────────────────────────────────────────────────────

        /// <summary>解析度自適應基準高度（像素）。用於計算 baseResolutionScale。</summary>
        public int ReferenceHeight => _referenceHeight;

        /// <summary>視窗高度佔可用桌面高度的比例。</summary>
        public float WindowHeightRatio => _windowHeightRatio;

        /// <summary>玩家縮放下限。</summary>
        public float UserScaleMin => _userScaleMin;

        /// <summary>玩家縮放上限。</summary>
        public float UserScaleMax => _userScaleMax;

        /// <summary>玩家每次調整的步進幅度。</summary>
        public float UserScaleStep => _userScaleStep;
    }
}
