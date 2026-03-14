using System;
using System.Collections.Generic;
using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Collapsible AP event log panel on the left side of the screen.
    /// Displays server messages, item receives, and connection events with timestamps.
    /// </summary>
    public class ApEventLogPanel : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly UILayout _uiLayout;
        private readonly VisualElementLoader _visualElementLoader;

        private VisualElement _root;
        private ScrollView _scrollView;
        private Button _toggleButton;
        private bool _collapsed;
        private const int MaxMessages = 100;

        public ApEventLogPanel(
            UILayout uiLayout,
            VisualElementLoader visualElementLoader)
        {
            _uiLayout = uiLayout;
            _visualElementLoader = visualElementLoader;
        }

        public void Load()
        {
            _root = _visualElementLoader.LoadVisualElement("ArchipelagoEventLog");

            // Add to UILayout to get into the visual tree, then reparent for absolute positioning
            _uiLayout.AddBottomRight(_root, 12);
            var panelRoot = _root.panel?.visualTree;
            if (panelRoot != null)
            {
                panelRoot.Add(_root);
                Debug.Log("[Archipelago] Event log reparented to panel root.");
            }

            _scrollView = _root.Q<ScrollView>("LogScrollView");
            _toggleButton = _root.Q<Button>("LogToggle");

            if (_toggleButton != null)
                _toggleButton.clicked += ToggleCollapse;

            ArchipelagoManager.OnLogMessage += AddMessage;

            Debug.Log("[Archipelago] Event log panel ready.");
        }

        public void Unload()
        {
            ArchipelagoManager.OnLogMessage -= AddMessage;
        }

        private void AddMessage(string text)
        {
            if (_scrollView == null) return;

            var timestamp = DateTime.Now.ToString("HH:mm");
            var formatted = $"[{timestamp}] {text}";

            var label = new Label(formatted);
            label.AddToClassList("ap-log__message");
            _scrollView.Add(label);

            // Cap message count
            if (_scrollView.childCount > MaxMessages)
                _scrollView.RemoveAt(0);

            // Auto-scroll to bottom
            _scrollView.schedule.Execute(() =>
            {
                _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
            });
        }

        private void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            _scrollView.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_toggleButton != null)
                _toggleButton.text = _collapsed ? "\u25BC" : "\u25B2";
        }
    }
}
