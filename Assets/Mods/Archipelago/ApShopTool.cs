using Timberborn.ToolSystem;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Tool activated by the AP Shop bottom bar button.
    /// Shows/hides the shop panel when the tool is entered/exited.
    /// </summary>
    internal class ApShopTool : ITool
    {
        private readonly ApShopPanel _shopPanel;

        public ApShopTool(ApShopPanel shopPanel)
        {
            _shopPanel = shopPanel;
        }

        public void Enter()
        {
            _shopPanel.Show();
        }

        public void Exit()
        {
            _shopPanel.Hide();
        }
    }
}
