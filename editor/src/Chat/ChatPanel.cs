//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class ChatPanel
{
    private const int MaxMessages = 256;

    [ElementId("Root")]
    [ElementId("InputBox")]
    [ElementId("MessageList")]
    [ElementId("SendButton")]
    [ElementId("CloseButton")]
    [ElementId("ProviderButton")]
    [ElementId("Message", MaxMessages)]
    private static partial class ElementId { }

    private static readonly List<ChatMessage> _messages = new();
    private static readonly Dictionary<string, int> _usageCounts = new();
    private static IChatBackend? _backend;
    private static ChatConfig? _config;
    private static string _inputText = string.Empty;

    public static bool IsOpen { get; private set; }

    public static void Init(EditorConfig config)
    {
        _config = new ChatConfig(config);
        _backend = CreateBackend(_config);
    }

    public static void Shutdown()
    {
        _backend?.Dispose();
        _backend = null;
        _messages.Clear();
    }

    public static void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        UI.SetFocus(ElementId.InputBox);
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        UI.ClearFocus();
    }

    public static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public static void Update()
    {
        if (!IsOpen) return;

        // Drain results from backend
        if (_backend != null)
            while (_backend.TryGetResult(out var msg))
                AddMessage(msg);

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            if (_backend?.IsBusy == true)
                _backend.Cancel();
            else
            {
                Input.ConsumeButton(InputCode.KeyEscape);
                Close();
            }
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter) && !Input.IsShiftDown())
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            SendMessage();
        }
    }

    public static void UpdateUI()
    {
        if (!IsOpen) return;

        using (UI.BeginColumn(ChatStyle.Root))
        {
            // Header
            using (UI.BeginRow(ChatStyle.Header))
            {
                UI.Label("Chat", EditorStyle.Control.Text);
                UI.Flex();
                if (_backend != null)
                {
                    using (UI.BeginContainer(ElementId.ProviderButton, ChatStyle.ProviderButton))
                    {
                        _usageCounts.TryGetValue(_backend.Provider, out var count);
                        var label = count > 0 ? $"{_backend.Provider} ({count})" : _backend.Provider;
                        UI.Label(label, ChatStyle.ProviderBadge);
                        if (UI.WasPressed())
                            CycleProvider();
                    }
                }
                using (UI.BeginContainer(ElementId.CloseButton, ChatStyle.CloseButton))
                    if (UI.WasPressed())
                        Close();
            }

            UI.Container(EditorStyle.Popup.Separator);

            // Messages
            using (UI.BeginContainer(ChatStyle.MessageContainer))
            using (UI.BeginScrollable(ElementId.MessageList))
            using (UI.BeginColumn(ChatStyle.MessageColumn))
            {
                for (var i = 0; i < _messages.Count && i < MaxMessages; i++)
                {
                    var msg = _messages[i];
                    var style = msg.Role switch
                    {
                        ChatRole.User => ChatStyle.UserMessage,
                        ChatRole.Assistant => ChatStyle.AssistantMessage,
                        _ => ChatStyle.SystemMessage,
                    };
                    var textStyle = msg.Role switch
                    {
                        ChatRole.User => ChatStyle.UserText,
                        ChatRole.System => ChatStyle.SystemText,
                        _ => ChatStyle.AssistantText,
                    };

                    using (UI.BeginContainer(ElementId.Message + i, style))
                        UI.Label(msg.Content, textStyle);
                }

                // Busy indicator
                if (_backend?.IsBusy == true)
                    using (UI.BeginContainer(ChatStyle.AssistantMessage))
                        UI.Label("...", ChatStyle.BusyText);
            }

            // Input row
            using (UI.BeginRow(ChatStyle.InputRow))
            {
                using (UI.BeginFlex())
                    if (UI.TextBox(ElementId.InputBox, style: ChatStyle.InputTextBox, placeholder: "Type a message..."))
                        _inputText = new string(UI.GetTextBoxText(ElementId.InputBox));

                using (UI.BeginContainer(ElementId.SendButton, ChatStyle.SendButton))
                {
                    UI.Label("Send", EditorStyle.Control.Text);
                    if (UI.WasPressed())
                        SendMessage();
                }
            }
        }
    }

    private static void SendMessage()
    {
        var text = _inputText.Trim();
        if (string.IsNullOrEmpty(text) || _backend == null || _backend.IsBusy)
            return;

        AddMessage(new ChatMessage(ChatRole.User, text));
        _inputText = string.Empty;
        UI.SetTextBoxText(ElementId.InputBox, string.Empty);

        var context = ChatContext.Build();
        _backend.Send(text, context);
    }

    private static void AddMessage(ChatMessage msg)
    {
        _messages.Add(msg);
        if (_config != null && _messages.Count > _config.MaxHistory)
            _messages.RemoveAt(0);

        if (msg.Role == ChatRole.Assistant && msg.Provider != null)
        {
            _usageCounts.TryGetValue(msg.Provider, out var count);
            _usageCounts[msg.Provider] = count + 1;
        }
    }

    private static void CycleProvider()
    {
        if (_backend == null) return;

        var providers = ChatConfig.AvailableProviders;
        var current = _backend.Provider;
        var index = Array.IndexOf(providers, current);
        var next = providers[(index + 1) % providers.Length];
        _backend.Provider = next;
    }

    public static void SaveUserSettings(PropertySet props)
    {
        if (_backend != null)
            props.SetString("chat", "last_provider", _backend.Provider);
    }

    public static void LoadUserSettings(PropertySet props)
    {
        var lastProvider = props.GetString("chat", "last_provider", string.Empty);
        if (_backend != null && !string.IsNullOrEmpty(lastProvider))
        {
            if (Array.IndexOf(ChatConfig.AvailableProviders, lastProvider) >= 0)
                _backend.Provider = lastProvider;
        }
    }

    private static IChatBackend CreateBackend(ChatConfig config)
    {
        return config.Mode.ToLowerInvariant() switch
        {
            "fleet" => new FleetChatBackend(config.FleetUrl, config.Provider),
            _ => new DirectChatBackend(config.Provider),
        };
    }
}
