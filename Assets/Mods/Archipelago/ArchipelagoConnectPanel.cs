using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// In-game connection panel for Archipelago. Displays text fields for
    /// host/port/slot/password and Connect/Disconnect buttons.
    /// Settings are persisted via PlayerPrefs so they survive across sessions.
    /// </summary>
    public class ArchipelagoConnectPanel : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly UILayout _uiLayout;
        private readonly VisualElementLoader _visualElementLoader;

        private TextField _hostField;
        private TextField _portField;
        private TextField _slotField;
        private TextField _passwordField;
        private Button _connectButton;
        private Button _disconnectButton;
        private Label _statusLabel;

        public ArchipelagoConnectPanel(UILayout uiLayout,
                                        VisualElementLoader visualElementLoader)
        {
            _uiLayout = uiLayout;
            _visualElementLoader = visualElementLoader;
        }

        public void Load()
        {
            var root = _visualElementLoader.LoadVisualElement("ArchipelagoConnection");
            _uiLayout.AddBottomRight(root, 10);

            _hostField = root.Q<TextField>("HostField");
            _portField = root.Q<TextField>("PortField");
            _slotField = root.Q<TextField>("SlotField");
            _passwordField = root.Q<TextField>("PasswordField");
            _connectButton = root.Q<Button>("ConnectButton");
            _disconnectButton = root.Q<Button>("DisconnectButton");
            _statusLabel = root.Q<Label>("StatusLabel");

            // Restore saved settings
            _hostField.value = PlayerPrefs.GetString("AP_Host", "localhost");
            _portField.value = PlayerPrefs.GetString("AP_Port", "38281");
            _slotField.value = PlayerPrefs.GetString("AP_Slot", "");

            _connectButton.RegisterCallback<ClickEvent>(_ => OnConnectClicked());
            _disconnectButton.RegisterCallback<ClickEvent>(_ => OnDisconnectClicked());

            ArchipelagoManager.OnConnectionChanged += OnConnectionChanged;

            UpdateButtonStates();
        }

        public void Unload()
        {
            ArchipelagoManager.OnConnectionChanged -= OnConnectionChanged;
        }

        private void OnConnectClicked()
        {
            var host = _hostField.value.Trim();
            if (!int.TryParse(_portField.value.Trim(), out var port))
            {
                _statusLabel.text = "Invalid port number";
                return;
            }
            var slot = _slotField.value.Trim();
            var password = _passwordField.value;

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(slot))
            {
                _statusLabel.text = "Host and slot are required";
                return;
            }

            // Save settings for next session
            PlayerPrefs.SetString("AP_Host", host);
            PlayerPrefs.SetString("AP_Port", port.ToString());
            PlayerPrefs.SetString("AP_Slot", slot);
            PlayerPrefs.Save();

            _statusLabel.text = "Connecting...";
            ArchipelagoManager.Connect(host, port, slot, password);
        }

        private void OnDisconnectClicked()
        {
            ArchipelagoManager.Disconnect();
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            _statusLabel.text = message;
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            var connected = ArchipelagoManager.IsConnected;
            _connectButton.SetEnabled(!connected);
            _disconnectButton.SetEnabled(connected);
        }
    }
}
