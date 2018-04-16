using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRC.Client;

namespace ExampleClient
{
    class Program
    {
        static void Main(string[] args)
        {
            IRCServer ServerConfig = new IRCServer
            {
                Host = "irc.freenode.net",
                Port = 6667,
                SSL = false,
            };

            IRCNickname NicknameConfig = new IRCNickname
            {
                Nickname = "daemon",
                User = "daemon",
                Realname = "C# Bot Library"
            };

            IRCConfiguration Config = new IRCConfiguration
            {
                Nickname = NicknameConfig,
                Servers = new IRCServer[] { ServerConfig },
            };

            IRCClient IRC = new IRCClient(Config, IRCHandler);
            IRC.Connect();
        }
        static public void IRCHandler (IRCPacket Packet)
        {
            if (Packet.Code == 433)
            {
                Console.WriteLine("Reting another nickname");

                IRCAction Response = new IRCAction
                {
                    Action = "NICK",
                    Arguments = new string[] { "daemongqw534igm" }
                };

                Packet.Queue.Enqueue(Response);
            }
        }
    }

}
