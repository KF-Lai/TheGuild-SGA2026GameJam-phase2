using UnityEngine;

namespace TheGuild.UI.Core
{
    /// <summary>
    /// P-02 主 UI 框架視覺與動畫調校參數。
    /// </summary>
    [CreateAssetMenu(fileName = "P02UITuning", menuName = "TheGuild/UI/P02UITuning")]
    public sealed class P02UITuning : ScriptableObject
    {
        [Header("Panel Animation")]
        [Tooltip("面板淡入淡出秒數。")]
        public float PanelTransitionSeconds = 0.15f;

        [Tooltip("L2 modal 遮罩透明度。")]
        public float ModalOverlayAlpha = 0.40f;

        [Header("Scene Hover")]
        [Tooltip("場景 hover outline 像素寬度。")]
        public float HoverOutlineThickness = 2f;

        [Tooltip("場景 hover outline 透明度。")]
        public float HoverOutlineAlpha = 0.60f;

        [Header("Floating Log")]
        [Tooltip("Log 視窗至少保留在可視範圍內的邊距。")]
        public float LogVisibleMargin = 32f;

        [Header("Text Limits")]
        [Tooltip("卡片介紹文字最大顯示字數。")]
        public int IntroTextMaxDisplayChars = 40;

        [Tooltip("故事對話單行最大字數。")]
        public int DialogueLineMaxChars = 24;

        [Tooltip("故事對話每頁行數。")]
        public int DialogueLinesPerPage = 3;

        [Header("Story Colors")]
        [Tooltip("Stage 4 場景說明灰字。")]
        public string StoryNoteGrayHex = "#888888";

        [Tooltip("Stage 5 黑底顏色。")]
        public string StoryNoteBlackHex = "#000001";

        [Header("Font Sizes")]
        public int FontSizePanelTitle = 16;
        public int FontSizeCardHeader = 14;
        public int FontSizeCardBody = 11;
        public int FontSizeDialogue = 14;
        public int FontSizeHud = 13;
        public int FontSizeButton = 12;
    }
}
