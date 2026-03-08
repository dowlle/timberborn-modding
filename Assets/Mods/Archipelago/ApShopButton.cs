using System.Collections.Generic;
using Timberborn.BottomBarSystem;
using Timberborn.ToolButtonSystem;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Provides the AP Shop button for the bottom bar.
    /// </summary>
    internal class ApShopButton : IBottomBarElementsProvider
    {
        private static readonly string ToolImageKey = "ApShopTool";
        private readonly ApShopTool _apShopTool;
        private readonly ToolButtonFactory _toolButtonFactory;

        public ApShopButton(ApShopTool apShopTool, ToolButtonFactory toolButtonFactory)
        {
            _apShopTool = apShopTool;
            _toolButtonFactory = toolButtonFactory;
        }

        public IEnumerable<BottomBarElement> GetElements()
        {
            var button = _toolButtonFactory.CreateGrouplessRed(_apShopTool, ToolImageKey);
            yield return BottomBarElement.CreateSingleLevel(button.Root);
        }
    }
}
