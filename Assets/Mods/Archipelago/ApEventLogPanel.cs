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
    /// Collapsible AP event log panel on the right side of the screen.
    /// Displays server messages, item receives, and connection events with timestamps,
    /// colored like the Archipelago text client, with an optional own-feed filter.
    /// </summary>
    public class ApEventLogPanel : ILoadableSingleton, IUnloadableSingleton
    {
        private const int MaxMessages = 200;
        private const float BottomTolerance = 4f;
        private const string OwnFeedPrefKey = "Archipelago.EventLog.OwnFeedOnly";
        private const string CollapsedClass = "ap-log__wrapper--collapsed";
        private const string ActiveFilterClass = "ap-log__filter--active";

        private readonly UILayout _uiLayout;
        private readonly VisualElementLoader _visualElementLoader;
        private readonly List<ApLogEntry> _entries = new();

        private VisualElement _root;
        private ScrollView _scrollView;
        private Button _toggleButton;
        private Button _filterButton;
        private bool _collapsed;
        private bool _ownFeedOnly;
        private bool _stickToBottom = true;

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

            // GameUI children render in this order: Top-left, Top-right, Top-bar, Bottom-left,
            // Bottom-right, Bottom-bar, Absolute-items (entity panel, notifications), Panels
            // (menus and dialogs). Making the log the first child of GameUI puts it behind all
            // of them but still over the world. The AP shop sits on the panel root, above GameUI.
            _uiLayout.AddAbsoluteItem(_root);
            var gameUi = _root.parent?.parent;
            if (gameUi != null)
            {
                gameUi.Insert(0, _root);
                Debug.Log($"[Archipelago] Event log placed behind game UI (first child of '{gameUi.name}').");
            }
            else
            {
                _root.SendToBack();
                Debug.LogWarning("[Archipelago] Game UI root not found; event log kept at the back of Absolute-items.");
            }

            _scrollView = _root.Q<ScrollView>("LogScrollView");
            _toggleButton = _root.Q<Button>("LogToggle");
            _filterButton = _root.Q<Button>("LogFilter");

            if (_toggleButton != null)
                _toggleButton.clicked += ToggleCollapse;

            _ownFeedOnly = PlayerPrefs.GetInt(OwnFeedPrefKey, 0) == 1;
            if (_filterButton != null)
                _filterButton.clicked += ToggleOwnFeed;
            UpdateFilterButton();

            if (_scrollView != null)
            {
                _scrollView.verticalScroller.valueChanged += OnScrollValueChanged;
                _scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
            }

            ArchipelagoManager.OnLogMessage += AddMessage;

            Debug.Log("[Archipelago] Event log panel ready.");
        }

        public void Unload()
        {
            ArchipelagoManager.OnLogMessage -= AddMessage;
            _root?.RemoveFromHierarchy();
        }

        private void AddMessage(ApLogEntry entry)
        {
            if (_scrollView == null || entry == null) return;

            entry.Timestamp = DateTime.Now.ToString("HH:mm");
            _entries.Add(entry);

            // Cap message count
            if (_entries.Count > MaxMessages)
            {
                var removed = _entries[0];
                _entries.RemoveAt(0);
                if (_scrollView.childCount > 0 && _scrollView[0].userData == removed)
                    _scrollView.RemoveAt(0);
            }

            if (ApLogFeedFilter.IsVisible(entry, _ownFeedOnly))
                _scrollView.Add(CreateLabel(entry));
        }

        private static Label CreateLabel(ApLogEntry entry)
        {
            var label = new Label(ApLogColors.ToRichText(entry))
            {
                enableRichText = true,
                userData = entry,
            };
            label.AddToClassList("ap-log__message");
            return label;
        }

        private void Rebuild()
        {
            _scrollView.Clear();
            foreach (var entry in _entries)
            {
                if (ApLogFeedFilter.IsVisible(entry, _ownFeedOnly))
                    _scrollView.Add(CreateLabel(entry));
            }
            _stickToBottom = true;
            ScrollToBottomLater();
        }

        // Auto-scroll: follow new entries while the view is at the bottom. Scrolling up
        // stops following; scrolling back to the bottom resumes it.
        private void OnScrollValueChanged(float value)
        {
            _stickToBottom = value >= _scrollView.verticalScroller.highValue - BottomTolerance;
        }

        private void OnContentGeometryChanged(GeometryChangedEvent evt)
        {
            if (_stickToBottom) ScrollToBottomLater();
        }

        private void ScrollToBottomLater()
        {
            // Wait one tick so the ScrollView has updated its scroller range to the new content.
            _scrollView.schedule.Execute(() =>
            {
                if (!_stickToBottom) return;
                var scroller = _scrollView.verticalScroller;
                scroller.value = scroller.highValue;
            });
        }

        private void ToggleOwnFeed()
        {
            _ownFeedOnly = !_ownFeedOnly;
            PlayerPrefs.SetInt(OwnFeedPrefKey, _ownFeedOnly ? 1 : 0);
            PlayerPrefs.Save();
            UpdateFilterButton();
            Rebuild();
        }

        private void UpdateFilterButton()
        {
            if (_filterButton == null) return;
            _filterButton.text = _ownFeedOnly ? "Show: Mine" : "Show: All";
            _filterButton.EnableInClassList(ActiveFilterClass, _ownFeedOnly);
        }

        private void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            _scrollView.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _root.EnableInClassList(CollapsedClass, _collapsed);
            if (_toggleButton != null)
                _toggleButton.text = _collapsed ? "\u25BC" : "\u25B2";
            if (!_collapsed)
            {
                _stickToBottom = true;
                ScrollToBottomLater();
            }
        }
    }
}
