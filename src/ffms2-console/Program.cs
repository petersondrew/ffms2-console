using System;
using System.Threading;
using ffms2.console.ipc;
using Zyan.Communication;
using Zyan.Communication.Protocols.Ipc;

namespace ffms2.console
{
    internal static class Program
    {
        static readonly ManualResetEventSlim QuitEvent = new ManualResetEventSlim(false);

        static int Main(string[] args)
        {
            string portName;
#if DEBUG
            portName = "localhost";
#else
            if (args.Length < 1 || String.IsNullOrEmpty(args[0]))
            {
                Console.Error.WriteLine("Please specify a unique IPC port name");
                return 1;
            }
            portName = args[0];
#endif

            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            try
            {
                using (var host = new ZyanComponentHost("ffms2.console", new IpcBinaryServerProtocolSetup(portName)))
                {
                    host.RegisterComponent<IFrameRetrievalService, FrameRetrievalService>(ActivationType.Singleton);
                    Console.WriteLine("Started new host on {0}", host.DiscoverableUrl);
                    Console.WriteLine("Press Ctrl-C to quit");
                    QuitEvent.Wait();
                }
                //Test();
                QuitEvent.Wait();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= ConsoleOnCancelKeyPress;
            }

            return 0;
        }

        static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs consoleCancelEventArgs)
        {
            consoleCancelEventArgs.Cancel = true;
            QuitEvent.Set();
        }
    }
}
