using NUnit.Framework;

namespace Tests.EditMode.UI.Panels
{
    public sealed class PanelManagerPanelDispatchTests
    {
        [Test]
        [Ignore("EditMode cannot reliably drive UIBootstrapController.Instance and PanelStateMachine timing; covered by PlayMode after R4 writeback.")]
        public void DoD_B6_OpenPanel_DispatchesIPanelOpen()
        {
        }

        [Test]
        [Ignore("EditMode cannot reliably drive UIBootstrapController.Instance and PanelStateMachine timing; covered by PlayMode after R4 writeback.")]
        public void DoD_B7_ClosePanel_DispatchesIPanelClose()
        {
        }
    }
}
