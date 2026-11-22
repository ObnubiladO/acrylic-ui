using System;
using System.Collections.Generic;
using System.IO;

namespace AcrylicUI.Core
{
    public enum CacheResponseKind
    {
        Positive = 0,
        Negative = 1,
        Failure = 2
    }

    /// <summary>
    /// One item of the Acrylic address cache: the response to one specific DNS request.
    /// </summary>
    public sealed class CacheEntry
    {
        public int Offset;
        public int RecordLength;
        public CacheResponseKind Kind;
        public DateTime Time;

        /// <summary>The domain name the response refers to, as found in the response.</summary>
        public string DomainName = "";

        public ushort QueryType;
        public string Answer = "";
    }

    /// <summary>
    /// Reads and filters AcrylicCache.dat, the file the Acrylic service saves its address cache to when it stops.
    ///
    /// The layout (see AddressCache.pas) is a plain sequence of records, each made of the MD5 hash of the request
    /// (16 bytes), the options (1 byte), the time stamp in minutes since January 1st 1980 (4 bytes), the length of
    /// the response (4 bytes) and the response itself. The request is not stored, but the response repeats its
    /// question, which is how the domain name of each item is recovered here.
    /// </summary>
    public sealed class AddressCacheFile
    {
        private const int HeaderLength = 16 + 1 + 4 + 4;
        private const int MaxResponseLength = 65535;

        private static readonly DateTime Epoch = new DateTime(1980, 1, 1);

        public List<CacheEntry> Entries { get; } = new List<CacheEntry>();

        public bool Exists { get; private set; }
        public long FileSize { get; private set; }
        public DateTime LastWriteTime { get; private set; }

        /// <summary>False when the file ends with something that does not look like a cache record.</summary>
        public bool IsWellFormed { get; private set; } = true;

        public static AddressCacheFile Load(string path)
        {
            if (!File.Exists(path)) return new AddressCacheFile();

            return Parse(File.ReadAllBytes(path), File.GetLastWriteTime(path));
        }

        public static AddressCacheFile Parse(byte[] bytes, DateTime lastWriteTime)
        {
            var file = new AddressCacheFile { Exists = true, FileSize = bytes.Length, LastWriteTime = lastWriteTime };

            int position = 0;

            while (position < bytes.Length)
            {
                if (position + HeaderLength > bytes.Length) { file.IsWellFormed = false; break; }

                byte options = bytes[position + 16];

                int timeStamp = BitConverter.ToInt32(bytes, position + 17);
                int responseLength = BitConverter.ToInt32(bytes, position + 21);

                if (responseLength < 0 || responseLength > MaxResponseLength || position + HeaderLength + responseLength > bytes.Length) { file.IsWellFormed = false; break; }

                var entry = new CacheEntry
                {
                    Offset = position,
                    RecordLength = HeaderLength + responseLength,
                    Kind = (CacheResponseKind)(options & 0x03),
                    Time = Epoch.AddMinutes(timeStamp)
                };

                DnsMessage response = DnsPacket.TryParse(bytes, position + HeaderLength, responseLength);

                if (response != null)
                {
                    entry.DomainName = response.QuestionName;
                    entry.QueryType = response.QuestionType;
                    entry.Answer = response.Summarize();
                }

                file.Entries.Add(entry);

                position += entry.RecordLength;
            }

            return file;
        }

        /// <summary>
        /// Tells whether a cache item belongs to the specified domain name. Names are compared without regard to case
        /// because the cache holds a separate item for each spelling a client happened to use.
        /// </summary>
        public static bool DomainMatches(string entryDomainName, string domainName, bool includeSubdomains)
        {
            if (string.IsNullOrEmpty(entryDomainName) || string.IsNullOrEmpty(domainName)) return false;

            if (string.Equals(entryDomainName, domainName, StringComparison.OrdinalIgnoreCase)) return true;

            return includeSubdomains && entryDomainName.Length > domainName.Length && entryDomainName[entryDomainName.Length - domainName.Length - 1] == '.' && entryDomainName.EndsWith(domainName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes what the user typed (or pasted, possibly as a URL) into a bare domain name.
        /// </summary>
        public static string NormalizeDomainName(string text)
        {
            text = (text ?? "").Trim();

            int scheme = text.IndexOf("://", StringComparison.Ordinal);

            if (scheme >= 0) text = text.Substring(scheme + 3);

            int slash = text.IndexOfAny(new[] { '/', '?', '#' });

            if (slash >= 0) text = text.Substring(0, slash);

            int at = text.LastIndexOf('@');

            if (at >= 0) text = text.Substring(at + 1);

            int colon = text.LastIndexOf(':');

            if (colon >= 0 && text.IndexOf(':') == colon) text = text.Substring(0, colon);

            return DomainNames.ToAscii(text.Trim().TrimEnd('.').ToLowerInvariant());
        }

        /// <summary>
        /// Removes from the cache file the items selected by the predicate and returns them. The remaining records
        /// are copied byte for byte. Must be called while Acrylic is stopped, otherwise the service overwrites the
        /// file with its in-memory cache as soon as it stops.
        /// </summary>
        public static List<CacheEntry> Remove(string path, Predicate<CacheEntry> shouldRemove)
        {
            var removed = new List<CacheEntry>();

            if (!File.Exists(path)) return removed;

            byte[] bytes = File.ReadAllBytes(path);

            AddressCacheFile file = Parse(bytes, DateTime.Now);

            if (!file.IsWellFormed) throw new InvalidDataException("The cache file \"" + path + "\" does not have the expected layout, so it has been left untouched.");

            using (var output = new MemoryStream(bytes.Length))
            {
                foreach (CacheEntry entry in file.Entries)
                {
                    if (shouldRemove(entry)) removed.Add(entry); else output.Write(bytes, entry.Offset, entry.RecordLength);
                }

                if (removed.Count > 0) TextFile.WriteAllBytesAtomically(path, output.ToArray());
            }

            return removed;
        }
    }
}
