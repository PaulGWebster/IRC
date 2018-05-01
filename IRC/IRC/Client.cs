using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IRC.Client
{
    public class IRCClient
    {
        private TcpClient IRCConnection = new TcpClient() {
            SendTimeout = 5,
        };
        private Dictionary<string, object> IRCStates = new Dictionary<string, object> () {
            { "NickNameSelect", 0 },
            { "NickNameFail", 0 },
            { "FailedResolveMax", 100 },
            { "NameResolutionDelay", 100 },
            { "SelectedServer", 0 },
        };
        private IRCConfiguration IRCConfig = new IRCConfiguration();
        private Action<IRCPacket> IRCHandler;
        private Dictionary<string, object> Stash = new Dictionary<string,object> ();

        public Queue<IRCAction> IRCTXQueue = new Queue<IRCAction>();

        public IRCClient(IRCConfiguration Profile, Action<IRCPacket> UserIRCHandler)
        {
            // Save the handler within our space
            IRCHandler = UserIRCHandler;

            // Do checks on the profile here
            if (Profile.Nickname == null)
            {
                throw new Exception("Nickname may not be empty");
            }
            if (Profile.Servers == null || Profile.Servers.Length == 0)
            {
                throw new Exception("Servers may not be empty");
            }
            IRCConfig = Profile;
        }
        
        public IRCDisconnect Connect()
        {
            // We might want to leave this from somewhere else at some point
            bool RunClient = true;

            while (RunClient) {
                // Resolve the hostnames and relative other information
                IRCServer IRCServer = SelectServer(IRCConfig.Servers);
                Queue<string> IRCRX = new Queue<string>();

                // Try to connect
                try
                {
                    IRCConnection.Connect(IRCServer.Host, IRCServer.Port);
                }
                catch
                {
                    // ANCHOR Debug feedback
                    continue;
                }

                // If we got here we are connected (yay)
                IRCNickname IRCProfile = IRCConfig.Nickname;
                StreamReader IRCReadStream = new StreamReader(IRCConnection.GetStream());
                StreamWriter IRCWriteStream = new StreamWriter(IRCConnection.GetStream())
                {
                    NewLine = "\r\n"
                };

                try
                {
                    IRCWriteStream.WriteLine("USER {0} {1} {2} :{3}", IRCProfile.User, IRCProfile.Mode, "*", IRCProfile.Realname);
                    IRCWriteStream.WriteLine("NICK {0}", IRCProfile.Nickname[(int)IRCStates["NickNameSelect"]]);
                    IRCWriteStream.Flush();
                }
                catch
                {
                    continue;
                }

                bool WeRead = true;

                while (IRCConnection.Connected)
                {
                    // We must remain single threaded (fun fun)
                    // what mode are we in

                    // Flip if we are reading or writing
                    WeRead = !WeRead;

                    // Pull or push from the tcp connection depedant
                    if (WeRead) {
                        try
                        {
                            IRCReadStream.BaseStream.ReadTimeout = 10;
                            IRCRX.Enqueue(IRCReadStream.ReadLine());
                        }
                        catch
                        {
                            // Exception somehow may have got disconnected!
                        }
                    }
                    else if (IRCTXQueue.Count > 0)
                    {
                        IRCWriteStream.WriteLine(IRCTXQueue.Dequeue().ToIRC());
                        IRCWriteStream.Flush();
                    }
                    else
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    // If we have an array of any size we have at least one line
                    if (IRCRX.Count == 0)
                    {
                        continue;
                    }

                    // We will ALWAYS have at least 2 items but the last one must always be ignored.
                    foreach (string IRCRXString in IRCRX) { 
                        Console.WriteLine("RAW: {0}", IRCRXString);

                        // Create an IRC Packet
                        IRCPacket ProcessedPacket = new IRCPacket(IRCRXString, Stash)
                        {
                            Queue = IRCTXQueue
                        };

                        // Some packets we want to handle here without passing onto the client, so lets do it
                        if (ProcessedPacket.Code == 0)
                        {
                            if (ProcessedPacket.Origin.Equals("PING"))
                            {
                                // Ping? Pong! event
                                IRCWriteStream.WriteLine("PONG {0}", ProcessedPacket.Event);

                                // Flush the buffer
                                IRCWriteStream.Flush();
                            }
                        }
                        else
                        {
                            IRCHandler(ProcessedPacket);
                        }
                    }

                    // Reset the RX queue
                    IRCRX.Clear();
                }
            }

            return new IRCDisconnect()
            {
                Reason = "Lazy developer error"
            };
            
        }

        private IRCServer SelectServer(IRCServer[] Servers)
        {
            // Should probably sends things back to the handler for this...
            IRCServer ReturnHost = null;
            int FailedResolves = 0;
  
            while (ReturnHost == null)
            {
                if ((int)IRCStates["SelectedServer"] >= IRCConfig.Servers.Length)
                {
                    IRCStates["SelectedServer"] = 0;
                }
                foreach (IRCServer HostSet in Servers)
                {
                    try
                    {
                        Dns.GetHostEntry(HostSet.Host);
                        return IRCConfig.Servers[(int)IRCStates["SelectedServer"]];
                    }
                    catch
                    {
                        FailedResolves++;
                        IRCStates["SelectedServer"] = (int)IRCStates["SelectedServer"] + 1;
                        Thread.Sleep((int)IRCStates["NameResolutionDelay"]);
                    }
                    if (FailedResolves >= (int)IRCStates["FailedResolveMax"])
                    {
                        // ANCHOR, Should return a soft fail somehow
                        throw new Exception("To many resolution attempts.");
                    }
                }
            }

            return ReturnHost;
        }
    }
    public class IRCConfiguration 
    {
        public IRCNickname Nickname { get; set; }
        public IRCServer[] Servers { get; set; }
        public bool TryForPrimaryNick { get; set; }
        public bool ResetNickOnConnect { get; set; }
    }

    public class IRCServer
    {
        public IRCServer ()
        {
            Host = String.Empty;
            Port = 6667;
            SSL = false;
            Password = String.Empty;
        }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Password { get; set; }
        public bool SSL { get; set; }
    }
    public class IRCNickname
    {
        public IRCNickname () {
            // Defaults
            Realname = "IRC lib";
            User = "csharp";
            Mode = 0;
        }
        public int Mode { get; set;  }
        public string Nickname { get; set; }
        public string User { get; set; }
        public string Realname { get; set; }
    }
    public class IRCDisconnect
    {
        public string Reason { get; internal set; }
    }
    public class IRCAction
    {
        public string Action { get; set; }
        public string[] Arguments { get; set; }
        public string ToIRC()
        {
            return Action + " " + String.Join(" ", Arguments);
        }
    }
    public class IRCPacket
    {
        private string Raw;
        Regex IRCCodeEvent = new Regex(@"^\d\d\d$");
        
        public IRCPacket(string RawRead, Object Stash)
        {
            this.Raw = RawRead;
            string[] Tokens = RawRead.Split(new char[0], 4);

            Origin  = Tokens[0];
            if (Tokens.Length > 1)
            {
                Event = Tokens[1];
                Code = 000;
                if (new Regex(@"^\d\d\d$").Match(Event).Success)
                {
                    Code = Convert.ToInt32(Event);
                }
            }
            if (Tokens.Length > 2)
            {
                Target = Tokens[2];
            }
            if (Tokens.Length > 3)
            {
                Payload = Tokens[3];
            }
            this.Stash = Stash;
        }
        public Object Stash { get; private set; }
        public int Code { get; private set; }
        public string Origin { get; private set; }
        public string Event { get; private set; }
        public string Target { get; private set; }
        public string Payload { get; private set; }
        public Queue<IRCAction> Queue { get; internal set; }
        public void Send (IRCAction Packet)
        {
            Queue.Enqueue(Packet);
        }
    }
    
}

namespace IRC.Client.DCC
{

}
