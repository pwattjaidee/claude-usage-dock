using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace ClaudeUsageDock;

// Must match the com:Class Id and CreateInstance ClassId in Package.appxmanifest.
[Guid("DAAC02D7-95AA-411B-AAB0-CA90EB56C623")]
public sealed partial class ClaudeUsageDockExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly ClaudeUsageDockCommandsProvider _provider = new();

    public ClaudeUsageDockExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose()
    {
        _provider.Dispose();
        _extensionDisposedEvent.Set();
    }
}
