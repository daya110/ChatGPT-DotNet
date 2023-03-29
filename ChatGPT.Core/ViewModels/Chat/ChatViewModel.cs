using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AI;
using AI.Model.Json.Chat;
using AI.Model.Services;
using ChatGPT.Model.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.ViewModels.Chat;

public class ChatViewModel : ObservableObject
{
    private string? _name;
    private ChatSettingsViewModel? _settings;
    private ObservableCollection<ChatMessageViewModel> _messages;
    private ChatMessageViewModel? _currentMessage;
    private bool _isEnabled;
    private CancellationTokenSource? _cts;

    [JsonConstructor]
    public ChatViewModel()
    {
        _messages = new ObservableCollection<ChatMessageViewModel>();
        _isEnabled = true;
    }

    public ChatViewModel(ChatSettingsViewModel settings) : this()
    {
        _settings = settings;
    }

    public ChatViewModel(
        string directions = "You are a helpful assistant.", 
        decimal temperature = 0.7m,
        decimal topP = 1m,
        decimal presencePenalty = 0m,
        decimal frequencyPenalty = 0m,
        int maxTokens = 2000, 
        string? apiKey = null,
        string model = "gpt-3.5-turbo") : this()
    {
        _settings = new ChatSettingsViewModel
        {
            Temperature = temperature,
            TopP = topP,
            PresencePenalty = presencePenalty,
            FrequencyPenalty = frequencyPenalty,
            MaxTokens = maxTokens,
            ApiKey = apiKey,
            Model = model,
            Directions = directions
        };
    }

    [JsonPropertyName("name")]
    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonPropertyName("settings")]
    public ChatSettingsViewModel? Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }

    [JsonPropertyName("messages")]
    public ObservableCollection<ChatMessageViewModel> Messages
    {
        get => _messages;
        set => SetProperty(ref _messages, value);
    }

    [JsonPropertyName("currentMessage")]
    public ChatMessageViewModel? CurrentMessage
    {
        get => _currentMessage;
        set => SetProperty(ref _currentMessage, value);
    }

    [JsonIgnore]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public void SetMessageActions(ChatMessageViewModel message)
    {
        message.SetSendAction(SendAsync);
        message.SetCopyAction(CopyAsync);
        message.SetRemoveAction(Remove);
    }

    public async Task CopyAsync(ChatMessageViewModel message)
    {
        var app = Defaults.Locator.GetService<IApplicationService>();
        if (app is { })
        {
            if (message.Message is { } text)
            {
                await app.SetClipboardTextAsync(text);
            }
        }
    }

    public void Remove(ChatMessageViewModel message)
    {
        if (message.IsAwaiting)
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        if (message is { CanRemove: true })
        {
            Messages.Remove(message);

            var lastMessage = Messages.LastOrDefault();
            if (lastMessage is { })
            {
                lastMessage.IsSent = false;

                if (Messages.Count == 2)
                {
                    lastMessage.CanRemove = false;
                }
            }
        }
    }

    public async Task<bool> SendAsync(ChatMessageViewModel sendMessage, bool onlyAddMessage = false)
    {
        var isError = true;

        if (Settings is null)
        {
            return isError;
        }

        if (string.IsNullOrEmpty(sendMessage.Message) && !onlyAddMessage)
        {
            return isError;
        }

        IsEnabled = false;

        try
        {
            sendMessage.CanRemove = true;
            sendMessage.IsSent = true;

            var isCanceled = false;
            
            if (!onlyAddMessage)
            {
                var chatPrompt = CreateChatMessages();

                _cts = new CancellationTokenSource();

                try
                {
                    var result = await CreateResultMessageAsync(chatPrompt, _cts.Token);
                    isError = result == null || result.IsError;
                }
                catch (OperationCanceledException)
                {
                    isError = true;
                }

                isCanceled = _cts.IsCancellationRequested;

                _cts.Dispose();
                _cts = null;
            }

            if (!isCanceled)
            {
                var nextMessage = new ChatMessageViewModel
                {
                    Role = "user",
                    Message = "",
                    IsSent = false,
                    CanRemove = true,
                    Format = Settings.Format
                };
                SetMessageActions(nextMessage);
                Messages.Add(nextMessage);
                CurrentMessage = nextMessage;

                isError = false;
            }
        }
        catch (Exception)
        {
            isError = true;
        }

        IsEnabled = true;

        return isError;
    }

    private async Task<ChatResultViewModel?> CreateResultMessageAsync(ChatMessage[] messages, CancellationToken token)
    {
        if (Settings is null)
        {
            return null;
        }

        // Sending...
        
        var resultMessage = new ChatMessageViewModel
        {
            Role = "assistant",
            Message = "Sending...",
            IsSent = false,
            CanRemove = true,
            IsAwaiting = true,
            Format = Defaults.TextMessageFormat
        };
        SetMessageActions(resultMessage);
        Messages.Add(resultMessage);
        CurrentMessage = resultMessage;

        // Response

        var result = await SendAsync(messages, token);

        // Update

        if (result is null)
        {
            resultMessage.Message = "Unknown error.";
            resultMessage.IsError = true;
        }
        else
        {
            resultMessage.Message = result.Message;
            resultMessage.IsError = result.IsError;
        }

        resultMessage.Format = Settings.Format;
        resultMessage.IsAwaiting = false;
        resultMessage.IsSent = true;

        return result;
    }

    public ChatMessage[] CreateChatMessages()
    {
        var chatMessages = new List<ChatMessage>();

        // TODO: Ensure that chat prompt does not exceed maximum token limit.

        for (var i = 0; i < Messages.Count; i++)
        {
            var message = Messages[i];

            if (i == 0)
            {
                var content = Settings?.Directions ?? "";

                if (message.Message != Defaults.WelcomeMessage)
                {
                    content = message.Message;
                }

                chatMessages.Add(new ChatMessage
                {
                    Role = message.Role,
                    Content = content
                });

                continue;
            }

            chatMessages.Add(new ChatMessage
            {
                Role = message.Role, 
                Content = message.Message
            });
        }

        return chatMessages.ToArray();
    }

    private static async Task<ChatResponse?> GetResponseDataAsync(ChatServiceSettings chatServiceSettings, ChatSettingsViewModel chatSettings, CancellationToken token)
    {
        var chat = Defaults.Locator.GetService<IChatService>();
        if (chat is null)
        {
            return null;
        }

        var apiKey = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiKey);
        var restoreApiKey = false;

        if (!string.IsNullOrWhiteSpace(chatSettings.ApiKey))
        {
            Environment.SetEnvironmentVariable(Constants.EnvironmentVariableApiKey, chatSettings.ApiKey);
            restoreApiKey = true;
        }

        ChatResponse? responseData = null;
        try
        {
            responseData = await chat.GetResponseDataAsync(chatServiceSettings, token);
        }
        catch (Exception)
        {
            // ignored
        }

        if (restoreApiKey && !string.IsNullOrWhiteSpace(apiKey))
        {
            Environment.SetEnvironmentVariable(Constants.EnvironmentVariableApiKey, apiKey);
        }

        return responseData;
    }

    public async Task<ChatResultViewModel?> SendAsync(ChatMessage[] messages, CancellationToken token)
    {
        if (Settings is null)
        {
            return default;
        }

        var chatServiceSettings = new ChatServiceSettings
        {
            Model = Settings.Model,
            Messages = messages,
            Suffix = null,
            Temperature = Settings.Temperature,
            MaxTokens = Settings.MaxTokens,
            TopP = 1.0m,
            Stop = null,
        };

        var result = new ChatResultViewModel
        {
            Message = default,
            IsError = false
        };
   
        var responseData = await GetResponseDataAsync(chatServiceSettings, Settings, token);
        if (responseData is null)
        {
            result.Message = "Unknown error.";
            result.IsError = true;
        }
        else if (responseData is ChatResponseError error)
        {
            var message = error.Error?.Message;
            result.Message = message ?? "Unknown error.";
            result.IsError = true;
        }
        else if (responseData is ChatResponseSuccess success)
        {
            var message = success.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            result.Message = message ?? "";
            result.IsError = false;
        }

        return result;
    }

    public ChatViewModel AddSystemMessage(string? message)
    {
        Messages.Add(new ChatMessageViewModel
        {
            Role = "system",
            Message = message
        });
        return this;
    }

    public ChatViewModel AddUserMessage(string? message)
    {
        Messages.Add(new ChatMessageViewModel
        {
            Role = "user",
            Message = message
        });
        return this;
    }

    public ChatViewModel AddAssistantMessage(string? message)
    {
        Messages.Add(new ChatMessageViewModel
        {
            Role = "assistant",
            Message = message
        });
        return this;
    }

    private ObservableCollection<ChatMessageViewModel> CopyMessages(out ChatMessageViewModel? currentMessage)
    {
        var messages = new ObservableCollection<ChatMessageViewModel>();

        currentMessage = null;

        foreach (var message in _messages)
        {
            var messageCopy = message.Copy();

            messages.Add(messageCopy);

            if (message == _currentMessage)
            {
                currentMessage = messageCopy;
            }
        }

        return messages;
    }

    public ChatViewModel Copy()
    {
        return new ChatViewModel
        {
            Name = _name,
            Settings = _settings?.Copy(),
            Messages = CopyMessages(out var currentMessage),
            CurrentMessage = currentMessage
        };
    }
}
