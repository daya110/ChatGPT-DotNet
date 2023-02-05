using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatGPT.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<MessageViewModel>? _messages;
    [ObservableProperty] private MessageViewModel? _currentMessage;
    [ObservableProperty] private SettingsViewModel? _settings;
    [ObservableProperty] private bool _isEnabled;

    public MainViewModel(Action exit)
    {
        _settings = new SettingsViewModel(exit)
        {
            Temperature = 0.6m,
            MaxTokens = 100
        };

        _messages = new ObservableCollection<MessageViewModel>();
        _isEnabled = true;

        var welcomeItem = new MessageViewModel(Send)
        {
            Prompt = "",
            Message = "Hi! I'm Clippy, your Windows Assistant. Would you like to get some assistance?",
            IsSent = false
        };
        _messages.Add(welcomeItem);
        _currentMessage = welcomeItem;
    }
    
    private async Task Send(MessageViewModel sendMessage)
    {
        if (Messages is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(sendMessage.Prompt))
        {
            return;
        }

        IsEnabled = false;

        sendMessage.IsSent = true;

        MessageViewModel? promptMessage;
        MessageViewModel? resultMessage = null;
        
        if (sendMessage.Result is { })
        {
            promptMessage = sendMessage;
            resultMessage = sendMessage.Result;
        }
        else
        {
            promptMessage = new MessageViewModel(Send);
            Messages.Add(promptMessage);
        }

        var prompt = sendMessage.Prompt;

        promptMessage.Message = sendMessage.Prompt;
        promptMessage.Prompt = "";
        promptMessage.IsSent = true;

        CurrentMessage = promptMessage;
        promptMessage.IsAwaiting = true;

        var temperature = Settings?.Temperature ?? 0.6m;
        var maxTokens = Settings?.MaxTokens ?? 100;
        var responseData = await ChatService.GetResponseDataAsync(prompt, temperature, maxTokens);

        if (resultMessage is null)
        {
            resultMessage = new MessageViewModel(Send)
            {
                IsSent = false
            };
            Messages.Add(resultMessage);
        }
        else
        {
            resultMessage.IsSent = true;
        }

        resultMessage.Message = responseData.Choices?.FirstOrDefault()?.Text.Trim();
        resultMessage.Prompt = "";

        CurrentMessage = resultMessage;

        promptMessage.IsAwaiting = false;
        promptMessage.Result = resultMessage;

        IsEnabled = true;
    }
}
