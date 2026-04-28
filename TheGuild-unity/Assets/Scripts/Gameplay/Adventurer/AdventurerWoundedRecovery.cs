using TheGuild.Core.Events;
using UnityEngine;

namespace TheGuild.Gameplay.Adventurer
{
    /// <summary>
    /// C-02-D 冒險者受傷恢復驅動器：訂閱 OnSecondTickEvent，每秒呼叫 AdventurerRoster.TickWoundedRecovery()。
    /// FSD §4.3（C-02-D）/ §5.4 流程 C。
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class AdventurerWoundedRecovery : MonoBehaviour
    {
        private void OnEnable()
        {
            EventBus.Subscribe<OnSecondTickEvent>(HandleSecondTick);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnSecondTickEvent>(HandleSecondTick);
        }

        private void HandleSecondTick(OnSecondTickEvent ev)
        {
            if (AdventurerRoster.Instance == null)
            {
                Debug.LogWarning("[AdventurerWoundedRecovery] HandleSecondTick: AdventurerRoster.Instance 為 null，跳過。");
                return;
            }

            AdventurerRoster.Instance.TickWoundedRecovery();
        }
    }
}
