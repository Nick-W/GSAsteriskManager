using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Mono.Options;


namespace SFGSManager
{
    internal class Program
    {
        public class Phone : IEquatable<String>
        {
            public string Extension;
            public string IP;
            public string Username;
            public bool IsActive;

            public bool Equals(string Extension)
            {
                if (String.IsNullOrEmpty(Extension))
                    return false;
                return this.Extension == Extension;
            }

            public override bool Equals(object obj)
            {
                string Extension = (obj as Phone).Extension;
                return Extension != null && Equals(Extension);
            }

            public static explicit operator Phone(string Extension)
            {
                return new Phone() { Extension = Extension };
            }

            public override string ToString()
            {
                return String.Format("[Phone] Extension: {0}, User: {1}, IP: {2}{3}", Extension, Username, IP, IsActive ? ", Active" : String.Empty);
            }
        }

        public static List<Phone> PhoneList = new List<Phone>();

        [Flags]
        enum optionflags
        {
            REBOOT = 1,
            ALLATONCE = 2,
            NOACTIVE = 4,
            VERSIONS = 8,
            SHOWHELP = 16,
            ONLYACTIVE = 32,
            INFO = 64
        }

        private static optionflags Options;
        public static string option_remote;
        public static string option_remoteuser;
        public static string option_remotepass;
        public static string option_gspass;
        static OptionSet p = new OptionSet()
                        {
                            {"a|all-at-once", "Execute action on all devices simultaneously", v => Options |= optionflags.ALLATONCE },
                            {"n|no-active", "Skip any devices that are currently in use", v => Options |= optionflags.NOACTIVE},
                            {"o|only-active", "Only include devices that are currently in use", v => Options |= optionflags.ONLYACTIVE},
                            {"v|phone-versions", "Get all device versions", v => Options |= optionflags.VERSIONS},
                            {"g|phone-password=", "Specify the grandstream admin password", v => option_gspass = v},
                            {"i|info", "Show registered devices", v => Options |= optionflags.INFO},
                            {"s|host=", "Specify a remote asterisk host to use (ssh)", v => option_remote = v},
                            {"u|username=", "Specify an username on a remote asterisk host (ssh)", v => option_remoteuser = v},
                            {"p|password=", "Specify a password on a remote asterisk host (ssh)", v => option_remotepass = v},
                            {"b|reboot", "Issue a reboot to the device pool", v => Options |= optionflags.REBOOT},
                            {"h|help", "Show this help documentation", v => Options |= optionflags.SHOWHELP},
                        };

        static void ShowHelp(string err = null)
        {
            Console.WriteLine("SparkFun Asterisk GrandStream Manager by Nick Wilson");
            Console.WriteLine("  Usage: {0} [options]", Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName));
            if (!String.IsNullOrEmpty(err))
                Console.WriteLine("    {0}", err);
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        private static void Main(string[] args)
        {
            try
            {
                var unknownArgs = p.Parse(args);
                //Handle actions here

                //Fail
                if (unknownArgs.Count > 0 || (Options & optionflags.SHOWHELP) == optionflags.SHOWHELP)
                {
                    StringBuilder err = new StringBuilder();
                    foreach (var a in unknownArgs)
                    {
                        err.AppendLine(String.Format("Invalid argument: {0}", a));
                    }
                    ShowHelp(err.ToString().Trim());
                    return;
                }
            }
            catch (OptionException e)
            {
                ShowHelp("Error: " + e.Message);
                return;
            }

            //Handle invalid conditions
            if (((Options & optionflags.ONLYACTIVE) == optionflags.ONLYACTIVE) && (Options & optionflags.NOACTIVE) == optionflags.NOACTIVE)
            {
                ShowHelp("Error: You can't specify -n with -o");
                return;
            }

            if (((Options & optionflags.INFO) == optionflags.INFO) && (Options & optionflags.REBOOT) == optionflags.REBOOT)
            {
                ShowHelp("Error: You can't specify -i with -b");
                return;
            }

            //No params
            if (Options.Equals(new optionflags()))
            {
                ShowHelp("Error: No options specified");
            }

            else
            {
                UpdatePhones();
                if ((Options & optionflags.INFO) == optionflags.INFO)
                {
                    ShowInfo();
                }
                else if ((Options & optionflags.REBOOT) == optionflags.REBOOT)
                {
                    RebootPhones();
                }
                else
                    ShowHelp("Error: No actions specified");
            }
        }

        private static bool IsActive(string Extension)
        {
            UpdatePhones();
            UpdateActivePhones();
            if (!PhoneList.Contains((Phone)Extension))
                throw new Exception("Phone is no longer registered!");
            return PhoneList.First(p => p.Extension == Extension).IsActive;
        }

        private static void ShowInfo()
        {
            foreach (var phone in PhoneList)
            {
                //Reduce calls to asterisk
                bool active = IsActive(phone.Extension);

                if (((Options & optionflags.ONLYACTIVE) == optionflags.ONLYACTIVE) && !active)
                    continue;
                if (((Options & optionflags.NOACTIVE) == optionflags.NOACTIVE) && active)
                    continue;
                Console.WriteLine(phone);
            }
        }

        private static void UpdateActivePhones()
        {
            Process getPhones = Process.Start(new ProcessStartInfo("asterisk", "-rx \"sip show inuse\"") { RedirectStandardOutput = true, UseShellExecute = false });
            getPhones.Start();
            getPhones.WaitForExit();
            var phones = getPhones.StandardOutput.ReadToEnd().Split('\n').ToList();
            phones.RemoveAt(0);
            phones.RemoveAt(phones.Count - 1);
            foreach (var phone in phones)
            {
                var extension = phone.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                foreach (var ph in PhoneList.Where(ph => ph.Extension == phone))
                {
                    ph.IsActive = (phone.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1] != "0/0/0");
                }
                //PhoneList.First(ph => ph.Extension == extension).IsActive = (phone.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries)[1] != "0/0/0");
            }
        }

        private static void UpdatePhones()
        {
            PhoneList = new List<Phone>();
            Process getPhones = Process.Start(new ProcessStartInfo("asterisk", "-rx \"sip show peers\"") { RedirectStandardOutput = true, UseShellExecute = false });
            getPhones.Start();
            getPhones.WaitForExit();
            var phones = getPhones.StandardOutput.ReadToEnd().Split('\n').ToList();
            phones.RemoveAt(phones.Count - 1);
            phones.RemoveAt(phones.Count - 1);
            phones.RemoveAt(0);
            foreach (var phone in phones)
            {
                var result = phone.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].Split('/');
                var extension = result.First();
                string username = result.Count() > 1 ? phone.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].Split('/')[1] : "Unknown";
                var ip = phone.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1];
                if (!ip.Contains("Unspecified") && !extension.Contains("trunk"))
                    PhoneList.Add(new Phone { Extension = extension, IP = ip, Username = username });
            }
        }

        private static void RebootPhones()
        {
            foreach (var phone in PhoneList)
            {
                //Reduce calls to asterisk
                bool active = IsActive(phone.Extension);

                if (((Options & optionflags.ONLYACTIVE) == optionflags.ONLYACTIVE) && !active)
                    continue;
                if (((Options & optionflags.NOACTIVE) == optionflags.NOACTIVE) && active)
                    continue;
                Console.WriteLine(RebootPhone(phone) ? String.Format("Rebooted {0}", phone) : String.Format("FAILED TO REBOOT {0}", phone));
            }
        }

        private static bool RebootPhone(Phone phone)
        {
            Process getPhones = Process.Start(new ProcessStartInfo("gsutil", String.Format("-n -b {0}", phone.IP)) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true});
            getPhones.WaitForExit();
            return (getPhones.ExitCode == 0);
        }
    }
}
