// Entry point. Command Palette launches this exe as an out-of-process COM server.
// Based on the official PowerToys Command Palette extension template (MIT).

using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer.CsWinRT; // RegisterClass<T, TInterface>() extension method

namespace ClaudeUsageDock;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();
            ManualResetEvent extensionDisposedEvent = new(false);

            // One extension instance for the life of the process, so there is only
            // ever one usage poller running.
            ClaudeUsageDockExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<ClaudeUsageDockExtension, IExtension>(() => extensionInstance);
            server.Start();

            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as an extension... exiting.");
        }
    }
}
