using Fleck;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using Newtonsoft.Json;
using Org.BouncyCastle.Utilities.Zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TMDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            ClientWebSocket _webSocket = new ClientWebSocket();
            CancellationToken token = new CancellationToken();
            long roomid = 905816;//直播间id  测试地址 https://live.bilibili.com/905816  这里。。随便选的自己改，根据地址后的id改
            Task.Run(async () =>
            {
                try
                {
                    await _webSocket.ConnectAsync(new Uri("wss://broadcastlv.chat.bilibili.com/sub"), token);
                    var bsend = new byte[1024];
                    await _webSocket.SendAsync(Bili_WS_Helper.Encode(JsonConvert.SerializeObject(new { roomid = roomid }), 7), WebSocketMessageType.Binary, true, token); //发送数据

                    while (true)
                    {
                        var result = new byte[10240];

                        var test = await _webSocket.ReceiveAsync(result, new CancellationToken());//接受数据

                        var packet = Bili_WS_Helper.Decode(result, test.Count);

                        //Console.WriteLine(JsonConvert.SerializeObject(packet));
                        switch (packet.op)
                        {
                            case 8:
                                Console.WriteLine("加入房间");
                                break;
                            case 3:

                                break;
                            case 5:
                                packet.body.ForEach(b =>
                                {
                                    try
                                    {
                                        if (((string)b.cmd).Contains("DANMU_MSG")) { Console.WriteLine($"{(string)b.info[2][1]}: {(string)b.info[1]}"); }
                                        else if (((string)b.cmd).Contains("SEND_GIFT")) { Console.WriteLine($"{ (string)b.data.uname} ${ (string)b.data.action} ${ (int)b.data.num} 个 ${ (string)b.data.giftName}"); }
                                        else if (((string)b.cmd).Contains("WELCOME")) { Console.WriteLine($"欢迎{(string)b.data.uname}"); }
                                    }
                                    catch (Exception)
                                    {
                                        Console.WriteLine(JsonConvert.SerializeObject(b));
                                    }
                                });

                                break;
                            default:

                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            });

            Task.Run(async () =>
            {
                while (true)
                {
                    Thread.Sleep(30000);
                    await _webSocket.SendAsync(Bili_WS_Helper.Encode("", 2), WebSocketMessageType.Binary, true, token); //发送数据
                }
            });//当作心跳包

            Console.ReadKey();
        }


        
    }

    public static class Bili_WS_Helper {
        public static long ReadInt(byte[] buffer, int start, int len)
        {
            long result = 0;
            try
            {

                for (int i = len - 1; i >= 0; i--)
                {
                    result += Convert.ToInt32(Math.Pow(256, len - i - 1) * buffer[start + i]);
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            return result;
        }

        public static void WriteInt(byte[] buffer, int start, int len, int value)
        {
            int i = 0;
            while (i < len)
            {
                buffer[start + i] = (byte)(value / Math.Pow(256, len - i - 1));
                i++;
            }
        }
        public static byte[] Encode(string str, int op)
        {
            byte[] data = Encoding.UTF8.GetBytes(str);
            int packetLen = 16 + data.Length;
            byte[] header = new byte[] { 0, 0, 0, 0, 0, 16, 0, 1, 0, 0, 0, (byte)op, 0, 0, 0, 1 };
            WriteInt(header, 0, 4, packetLen);
            return header.Concat(data).ToArray();
        }
        public static Res Decode(byte[] blob, int len)
        {
            byte[] buffer = new byte[len];
            buffer = blob.Take(len).ToArray();
            var result = new Res(buffer) { };
            result.body = new List<dynamic>();
            if (result.op == 5)
            {

                var data = buffer.Skip(16).Take(len - 16).ToArray();
                //console.log(data);
                string body = null;
                try
                {
                    var Decompress = SharpZipLibDecompress(data);
                    body = Encoding.UTF8.GetString(Decompress);
                }
                catch (Exception ex)
                {
                    //console.log(e);
                }
                if (body != null)
                {
                    var group = Regex.Split(body, "[\x00-\x1f]+").ToList();
                    group.ForEach(item =>
                    {
                        try
                        {
                            if (item != null && item.Contains("{"))
                                result.body.Add(JsonConvert.DeserializeObject(item));
                        }
                        catch (Exception ex)
                        {
                            // 忽略非 JSON 字符串，通常情况下为分隔符
                        }
                    });
                }
            }
            else if (result.op == 3)
            {
                result.body.Add(new ResBody
                {
                    count = ReadInt(buffer, 16, 4)
                });
            }

            return result;
        }

        /// <summary>
        /// 解压 由于ws数据压缩所以需要解压数据 
        /// Header中Sec-WebSocket-Extensions: permessage-deflate;表示使用了deflate压缩算法，详看 https://www.cnblogs.com/mq0036/p/14711737.html
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] SharpZipLibDecompress(byte[] data)
        {
            MemoryStream compressed = new MemoryStream(data);
            MemoryStream decompressed = new MemoryStream();
            InflaterInputStream inputStream = new InflaterInputStream(compressed);
            inputStream.CopyTo(decompressed);
            return decompressed.ToArray();
        }
    }

    public class Res {

        public long packetLen { get; set; }
        public long headerLen { get; set; }
        public long ver { get; set; }
        public long op { get; set; }
        public long seq { get; set; }
        public List<dynamic> body { get; set; }

        public Res(byte[] buffer) {
            this.packetLen = Bili_WS_Helper.ReadInt(buffer, 0, 4);
            this.headerLen = Bili_WS_Helper.ReadInt(buffer, 4, 2);
            this.ver = Bili_WS_Helper.ReadInt(buffer, 6, 2);
            this.op = Bili_WS_Helper.ReadInt(buffer, 8, 4);
            this.seq = Bili_WS_Helper.ReadInt(buffer, 12, 4);
        }

    }

    public class ResBody { 
        public string cmd { get; set; }
        public object[] info { get; set; } 
        public ResBodyData data { get; set; }
        public long count { get; set; }
    }
    public class ResBodyData
    {
        public string uname { get; set; }
        public string action { get; set; }
        public string num { get; set; }
        public string giftName { get; set; }
    }
}
