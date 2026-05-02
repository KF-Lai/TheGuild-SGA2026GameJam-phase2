using UnityEngine;

namespace TheGuild.UI.Scene
{
    /// <summary>
    /// 場景座標轉 UI Toolkit panel local 座標的純計算工具。
    /// </summary>
    public static class ScreenAnchorCalculator
    {
        public static Vector2 WorldToPanelLocal(
            Vector3 worldPos,
            Camera camera,
            Vector2 windowClientSize,
            float effectiveScale,
            float offsetY = 0f)
        {
            if (camera == null)
            {
                return Vector2.zero;
            }

            float scale = SanitizeScale(effectiveScale);
            Vector3 screenPos = camera.WorldToScreenPoint(worldPos);
            return new Vector2(
                screenPos.x / scale,
                (windowClientSize.y - screenPos.y) / scale + offsetY);
        }

        public static Rect WorldBoundsToPanelRect(
            Bounds spriteBounds,
            Camera camera,
            Vector2 windowClientSize,
            float effectiveScale)
        {
            if (camera == null)
            {
                return Rect.zero;
            }

            Vector3 min = spriteBounds.min;
            Vector3 max = spriteBounds.max;
            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 local = WorldToPanelLocal(corners[i], camera, windowClientSize, effectiveScale);
                minX = Mathf.Min(minX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxX = Mathf.Max(maxX, local.x);
                maxY = Mathf.Max(maxY, local.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public static Vector2 ClampLogWindow(
            Vector2 desiredPos,
            Vector2 windowClientSize,
            Vector2 logSize,
            float visibleMargin)
        {
            float margin = Mathf.Max(0f, visibleMargin);
            float maxX = Mathf.Max(margin - logSize.x, windowClientSize.x - margin);
            float maxY = Mathf.Max(margin - logSize.y, windowClientSize.y - margin);
            float minX = Mathf.Min(windowClientSize.x - margin, margin - logSize.x);
            float minY = Mathf.Min(windowClientSize.y - margin, margin - logSize.y);

            return new Vector2(
                Mathf.Clamp(desiredPos.x, minX, maxX),
                Mathf.Clamp(desiredPos.y, minY, maxY));
        }

        private static float SanitizeScale(float effectiveScale)
        {
            return effectiveScale > 0f ? effectiveScale : 1f;
        }
    }
}
