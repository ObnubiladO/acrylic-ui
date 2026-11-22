using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AcrylicUI.Core
{
    public sealed class DnsAnswer
    {
        public ushort Type;
        public string Text;
    }

    public sealed class DnsMessage
    {
        public ushort Id;
        public int ResponseCode;
        public string QuestionName = "";
        public ushort QuestionType;
        public List<DnsAnswer> Answers = new List<DnsAnswer>();

        /// <summary>
        /// A one line summary of the content of a response.
        /// </summary>
        public string Summarize()
        {
            if (ResponseCode != 0) return DnsPacket.ResponseCodeName(ResponseCode);

            if (Answers.Count == 0) return "No data";

            var preferred = new List<string>();
            var others = new List<string>();

            foreach (DnsAnswer answer in Answers)
            {
                if (answer.Type == QuestionType) preferred.Add(answer.Text); else others.Add(DnsPacket.TypeName(answer.Type) + " " + answer.Text);
            }

            List<string> shown = preferred.Count > 0 ? preferred : others;

            string text = string.Join(", ", shown.GetRange(0, Math.Min(3, shown.Count)));

            if (shown.Count > 3) text += " (+" + (shown.Count - 3) + ")";

            return text;
        }
    }

    /// <summary>
    /// The little of the DNS wire format the UI needs: reading a response and writing a simple query.
    /// </summary>
    public static class DnsPacket
    {
        public const ushort TypeA = 1, TypeNS = 2, TypeCNAME = 5, TypeSOA = 6, TypePTR = 12, TypeMX = 15, TypeTXT = 16, TypeAAAA = 28, TypeSRV = 33, TypeHTTPS = 65, TypeANY = 255;

        public static string TypeName(ushort type)
        {
            switch (type)
            {
                case TypeA: return "A";
                case TypeNS: return "NS";
                case TypeCNAME: return "CNAME";
                case TypeSOA: return "SOA";
                case TypePTR: return "PTR";
                case TypeMX: return "MX";
                case TypeTXT: return "TXT";
                case TypeAAAA: return "AAAA";
                case TypeSRV: return "SRV";
                case TypeHTTPS: return "HTTPS";
                case TypeANY: return "ANY";
                default: return "TYPE" + type;
            }
        }

        public static ushort ParseTypeName(string name)
        {
            foreach (ushort type in new[] { TypeA, TypeNS, TypeCNAME, TypeSOA, TypePTR, TypeMX, TypeTXT, TypeAAAA, TypeSRV, TypeHTTPS, TypeANY })
            {
                if (string.Equals(TypeName(type), name, StringComparison.OrdinalIgnoreCase)) return type;
            }

            return ushort.TryParse(name, out ushort value) ? value : (ushort)0;
        }

        public static string ResponseCodeName(int code)
        {
            switch (code)
            {
                case 0: return "No error";
                case 1: return "Format error";
                case 2: return "Server failure";
                case 3: return "Name does not exist (NXDOMAIN)";
                case 4: return "Not implemented";
                case 5: return "Refused";
                default: return "Response code " + code;
            }
        }

        public static byte[] BuildQuery(ushort id, string domainName, ushort type)
        {
            var bytes = new List<byte>(64) { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            foreach (string label in domainName.Trim().TrimEnd('.').Split('.'))
            {
                byte[] labelBytes = Encoding.ASCII.GetBytes(label);

                if (labelBytes.Length == 0 || labelBytes.Length > 63) throw new FormatException("\"" + domainName + "\" is not a valid domain name.");

                bytes.Add((byte)labelBytes.Length);
                bytes.AddRange(labelBytes);
            }

            bytes.Add(0);
            bytes.Add((byte)(type >> 8)); bytes.Add((byte)type);
            bytes.Add(0); bytes.Add(1);

            if (bytes.Count > 12 + 255 + 4) throw new FormatException("\"" + domainName + "\" is too long for a domain name.");

            return bytes.ToArray();
        }

        /// <summary>
        /// Reads the DNS message found in data[offset..offset+length). Returns null if the header or the question are
        /// not readable; answers that cannot be read are simply left out.
        /// </summary>
        public static DnsMessage TryParse(byte[] data, int offset, int length)
        {
            if (length < 12) return null;

            int end = offset + length;

            var message = new DnsMessage
            {
                Id = (ushort)((data[offset] << 8) | data[offset + 1]),
                ResponseCode = data[offset + 3] & 0x0F
            };

            int questions = (data[offset + 4] << 8) | data[offset + 5];
            int answers = (data[offset + 6] << 8) | data[offset + 7];

            int position = offset + 12;

            try
            {
                for (int i = 0; i < questions; i++)
                {
                    string name = ReadName(data, offset, end, ref position);

                    if (position + 4 > end) return null;

                    if (i == 0)
                    {
                        message.QuestionName = name;
                        message.QuestionType = (ushort)((data[position] << 8) | data[position + 1]);
                    }

                    position += 4;
                }
            }
            catch (FormatException)
            {
                return null;
            }

            if (questions == 0) return null;

            try
            {
                for (int i = 0; i < answers; i++)
                {
                    ReadName(data, offset, end, ref position);

                    if (position + 10 > end) break;

                    ushort type = (ushort)((data[position] << 8) | data[position + 1]);
                    int dataLength = (data[position + 8] << 8) | data[position + 9];

                    position += 10;

                    if (position + dataLength > end) break;

                    message.Answers.Add(new DnsAnswer { Type = type, Text = ReadRecordData(data, offset, end, position, dataLength, type) });

                    position += dataLength;
                }
            }
            catch (FormatException)
            {
            }

            return message;
        }

        private static string ReadRecordData(byte[] data, int messageStart, int end, int position, int length, ushort type)
        {
            switch (type)
            {
                case TypeA when length == 4:
                    return data[position] + "." + data[position + 1] + "." + data[position + 2] + "." + data[position + 3];

                case TypeAAAA when length == 16:
                    var address = new byte[16];
                    Buffer.BlockCopy(data, position, address, 0, 16);
                    return new IPAddress(address).ToString();

                case TypeCNAME:
                case TypeNS:
                case TypePTR:
                    return ReadName(data, messageStart, end, ref position);

                case TypeMX when length > 2:
                    int preference = (data[position] << 8) | data[position + 1];
                    position += 2;
                    return preference + " " + ReadName(data, messageStart, end, ref position);

                case TypeTXT:
                    var text = new StringBuilder();
                    int stop = position + length;
                    while (position < stop)
                    {
                        int size = data[position++];
                        if (position + size > stop) break;
                        if (text.Length > 0) text.Append(' ');
                        text.Append('"').Append(Encoding.ASCII.GetString(data, position, size)).Append('"');
                        position += size;
                    }
                    return text.ToString();

                default:
                    return "(" + length + " bytes)";
            }
        }

        private static string ReadName(byte[] data, int messageStart, int end, ref int position)
        {
            var name = new StringBuilder();

            int cursor = position;
            int jumps = 0;
            bool jumped = false;

            while (true)
            {
                if (cursor >= end) throw new FormatException();

                int size = data[cursor];

                if (size == 0)
                {
                    cursor++;

                    if (!jumped) position = cursor;

                    break;
                }

                if ((size & 0xC0) == 0xC0)
                {
                    if (cursor + 1 >= end) throw new FormatException();

                    if (!jumped) position = cursor + 2;

                    cursor = messageStart + (((size & 0x3F) << 8) | data[cursor + 1]);

                    jumped = true;

                    if (++jumps > 64) throw new FormatException();

                    continue;
                }

                if ((size & 0xC0) != 0 || cursor + 1 + size > end) throw new FormatException();

                if (name.Length > 0) name.Append('.');

                for (int i = 0; i < size; i++)
                {
                    char c = (char)data[cursor + 1 + i];

                    name.Append(c >= 0x21 && c <= 0x7E ? c : '?');
                }

                cursor += size + 1;
            }

            return name.ToString();
        }
    }

    public sealed class DnsLookupResult
    {
        public DnsMessage Response;
        public long ElapsedMilliseconds;
        public string Error;
    }

    /// <summary>
    /// A minimal DNS client used to try a lookup through the local Acrylic instance.
    /// </summary>
    public static class DnsClient
    {
        private static readonly Random random = new Random();

        public static DnsLookupResult Query(IPEndPoint server, string domainName, ushort type, int timeoutMilliseconds)
        {
            var result = new DnsLookupResult();

            byte[] query;

            ushort id;

            lock (random) id = (ushort)random.Next(1, 65536);

            try
            {
                query = DnsPacket.BuildQuery(id, domainName, type);
            }
            catch (FormatException e)
            {
                result.Error = e.Message;

                return result;
            }

            using (var socket = new UdpClient(server.AddressFamily))
            {
                socket.Client.ReceiveTimeout = timeoutMilliseconds;

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    socket.Send(query, query.Length, server);

                    while (true)
                    {
                        var remote = new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

                        byte[] reply = socket.Receive(ref remote);

                        DnsMessage message = DnsPacket.TryParse(reply, 0, reply.Length);

                        if (message != null && message.Id == id)
                        {
                            result.Response = message;

                            break;
                        }
                    }
                }
                catch (SocketException e)
                {
                    switch (e.SocketErrorCode)
                    {
                        case SocketError.TimedOut: result.Error = "No answer within " + (timeoutMilliseconds / 1000) + " seconds."; break;
                        case SocketError.ConnectionReset: result.Error = "Nothing is listening on " + server + ". Is Acrylic running?"; break;
                        default: result.Error = e.Message; break;
                    }
                }

                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }
    }
}
