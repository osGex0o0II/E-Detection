namespace EDetection.Desktop.Services;

// The network services used to take MainViewModel directly, which inverted the
// layering: a service depended on the view model that consumed it, and pulled
// the whole WinUI shell in with it.  These interfaces carry only the settings
// each service actually reads, so the services stay in this WinUI-free assembly
// and can be exercised by the headless test project.

public interface INetworkProxySettings
{
    bool EnableNetworkProxy { get; }

    string ProxyAddress { get; }

    bool ProxyRequiresAuthentication { get; }

    string ProxyUserName { get; }
}

public interface INtfyNotificationSettings : INetworkProxySettings
{
    bool EnableNtfyNotifications { get; }

    string NtfyServerUrl { get; }

    string NtfyTopic { get; }

    int SelectedNtfyPriorityIndex { get; }

    bool UseProxyForNotifications { get; }
}

public interface ILlmAssistantSettings : INetworkProxySettings
{
    bool EnableLlmAssistant { get; }

    string LlmEndpoint { get; }

    string LlmModel { get; }

    bool UseProxyForLlm { get; }
}

// Secrets live in the Windows credential locker, which is a WinRT API and stays
// in the shell project.  Consumers only ever read.
public interface IAppCredentialStore
{
    string GetProxyPassword();

    string GetNtfyToken();

    string GetLlmApiKey();
}
