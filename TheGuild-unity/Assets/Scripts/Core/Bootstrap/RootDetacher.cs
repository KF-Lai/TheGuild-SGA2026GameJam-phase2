using System.Collections.Generic;
using UnityEngine;

namespace TheGuild.Core.Bootstrap
{
    /// <summary>
    /// 在 Awake 最早期把所有子物件 reparent 到場景根層。
    /// 用途：保留編輯期 _Backend / _UI / _Panels 群組組織，runtime 自動平整以滿足 DontDestroyOnLoad 規範。
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class RootDetacher : MonoBehaviour
    {
        private void Awake()
        {
            int count = transform.childCount;
            if (count == 0)
            {
                return;
            }

            List<Transform> children = new List<Transform>(count);
            for (int i = 0; i < count; i++)
            {
                children.Add(transform.GetChild(i));
            }

            for (int i = 0; i < children.Count; i++)
            {
                children[i].SetParent(null, true);
            }
        }
    }
}
