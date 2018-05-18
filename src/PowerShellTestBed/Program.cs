namespace PowerShellTestBed
{
    using System;
    using System.Linq;
    using Particular.ServiceControl.Hosting;
    using ServiceControlInstaller.Engine;
    using ServiceControlInstaller.PowerShell;

    public class Program
    {
        static void Main(string[] args)
        {
            InvokeServiceControlInstanceUpgrade upgradeCmdlet = null;

            var upgradeOptions = new OptionSet
            {
                {"upgrade|u", _ =>
                    {
                        upgradeCmdlet = new InvokeServiceControlInstanceUpgrade();
                    }
                },
                {"name=|n=", arg =>
                {
                    var newName = new[] {arg};
                    if (upgradeCmdlet.Name == null)
                    {
                        upgradeCmdlet.Name = newName;
                    }
                    else
                    {
                        upgradeCmdlet.Name = upgradeCmdlet.Name.Concat(newName).ToArray();
                    }
                }},
                {"maintenance-port=|mp=", (int arg) => upgradeCmdlet.DatabaseMaintenancePort = arg},
                {"forward-error", (bool arg) => upgradeCmdlet.ForwardErrorMessages = arg}
            };

            Console.WriteLine("Upgrade:");
            upgradeOptions.WriteOptionDescriptions(Console.Out);

            while (true)
            {
                Console.Write(">");
                upgradeCmdlet = null;

                var command = Console.ReadLine();
                upgradeOptions.Parse(command.Split(' '));
                if (upgradeCmdlet != null)
                {
                    try
                    {
                        upgradeCmdlet.InvokeUpgrade(@"C:\Users\SzymonPobiega\Downloads", new ConsoleLogger(), record =>
                        {
                            throw new Exception(record.Exception.Message);
                        });
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Exception: {e.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Invalid command");
                    Console.WriteLine("Upgrade:");
                    upgradeOptions.WriteOptionDescriptions(Console.Out);
                }
            }
            
        }

        class ConsoleLogger : ILogging
        {
            public void Info(string message)
            {
                Console.WriteLine(message);
            }

            public void Warn(string message)
            {
                Console.WriteLine(message);
            }

            public void Error(string message)
            {
                Console.WriteLine(message);
            }
        }
    }
}

