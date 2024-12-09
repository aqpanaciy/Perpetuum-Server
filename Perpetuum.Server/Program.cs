using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using Perpetuum.Bootstrapper;
using Mono.Unix;

namespace Perpetuum.Server
{
    public static class Program
    {

        static int Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.HelpOption("-h|--help");
            var gameRoot = app.Argument("<GAMEROOT>","d:\\server\\genxy");
            var dumpCommands = app.Option("-dc|--dump-commands", "dump commands", CommandOptionType.NoValue);

            var bootstrapper = new PerpetuumBootstrapper();

            app.OnExecute(() =>
            {
                if (dumpCommands.HasValue())
                {
                    Console.WriteLine("dumping commands to commands.txt");
                    bootstrapper.WriteCommandsToFile("commands.txt");
                    return 0;
                }

                if (gameRoot.Value == null)
                {
                    return 2;
                }

                if (!Directory.Exists(gameRoot.Value))
                {
                    Console.WriteLine($"GameRoot folder was not found: {gameRoot.Value}");
                    return 3;
                }

                bootstrapper.Init(gameRoot.Value);

                if (bootstrapper.TryInitUpnp(out bool upnpSuccess))
                {
                    if (!upnpSuccess)
                    {
                        //System Error Codes (500-999)
                        // signal upnp attempt error with custom errorcode
                        return 2000;
                    }
                }

                return 0;
            });

            var err = 0;
            try
            {
                err = app.Execute(args);
                if (err == 0)
                {
                    bootstrapper.Start();

                    using (CancellationTokenSource source = new CancellationTokenSource())
                    {
                        int p = (int)Environment.OSVersion.Platform;
                        if ((p == 4) || (p == 6) || (p == 128))
                        {
                            UnixSignal[] signals = new UnixSignal[] {
                                new UnixSignal(Mono.Unix.Native.Signum.SIGINT),
                                new UnixSignal(Mono.Unix.Native.Signum.SIGTERM),
                            };
                            CancellationToken token = source.Token;

                            Task.Run(() =>
                            {
                                while (!token.IsCancellationRequested)
                                {
                                    int index = UnixSignal.WaitAny(signals, 1000);
                                    if (index < signals.Length)
                                    {
                                        bootstrapper.Stop();
                                        break;
                                    }
                                }
                            });
                        }

                        Console.CancelKeyPress += (sender, eventArgs) =>
                        {
                            source.Cancel();
                            eventArgs.Cancel = true;

                            Console.WriteLine("");
                            Console.WriteLine("STOPPING HOST IN 4 SECONDS");
                            Console.WriteLine("");

                            bootstrapper.Stop(TimeSpan.FromSeconds(4));
                        };

                        bootstrapper.WaitForStop();
                    }
                }
                else
                {
                    app.ShowHelp();
                }
            }
            catch (Exception ex)
            {
                DisplayException(ex);
                err = 1; // generic error
            }

            return err;
        }

        private static void DisplayException(Exception ex)
        {
            if (ex is AggregateException aex)
            {
                foreach (var innerException in aex.InnerExceptions)
                {
                    DisplayException(innerException);    
                }
                return;
            }

            if (ex.InnerException != null)
            {
                DisplayException(ex.InnerException);
            }

            Console.WriteLine(ex.Message);
        }

    }
}
