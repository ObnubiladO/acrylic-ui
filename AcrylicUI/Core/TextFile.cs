using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AcrylicUI.Core
{
    /// <summary>
    /// Loads and saves the Acrylic text files without altering anything the user did not edit: the original encoding
    /// (ANSI or UTF-8, with or without a byte order mark) and the original line terminator are remembered and reused.
    /// </summary>
    public sealed class TextFile
    {
        public string Text { get; set; } = "";
        public Encoding Encoding { get; private set; } = Encoding.Default;
        public bool HasByteOrderMark { get; private set; }

        public static TextFile Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            var result = new TextFile();

            int offset = 0;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                result.Encoding = new UTF8Encoding(false);
                result.HasByteOrderMark = true;
                offset = 3;
            }
            else if (LooksLikeMultiByteUtf8(bytes))
            {
                result.Encoding = new UTF8Encoding(false);
            }
            else
            {
                // The Acrylic service and the previous UI both treat these files as ANSI text.
                result.Encoding = Encoding.Default;
            }

            result.Text = result.Encoding.GetString(bytes, offset, bytes.Length - offset);

            return result;
        }

        public void Save(string path)
        {
            byte[] body = Encoding.GetBytes(Text ?? "");

            byte[] bytes;

            if (HasByteOrderMark)
            {
                bytes = new byte[body.Length + 3];
                bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF;
                Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
            }
            else
            {
                bytes = body;
            }

            WriteAllBytesAtomically(path, bytes);
        }

        /// <summary>
        /// Writes a file through a temporary sibling so that a failure half way never leaves a truncated file behind.
        /// </summary>
        public static void WriteAllBytesAtomically(string path, byte[] bytes)
        {
            string temporaryPath = path + ".tmp";

            File.WriteAllBytes(temporaryPath, bytes);

            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch
            {
                try { File.Delete(temporaryPath); } catch { }

                throw;
            }
        }

        /// <summary>
        /// Returns the line terminator used by the text, defaulting to the Windows one.
        /// </summary>
        public static string DetectNewLine(string text)
        {
            int index = text.IndexOf('\n');

            if (index < 0) return "\r\n";

            return (index > 0 && text[index - 1] == '\r') ? "\r\n" : "\n";
        }

        /// <summary>
        /// Splits a text into lines, keeping for each line the terminator it had ("\r\n", "\n" or, for a last line
        /// without terminator, an empty string), so that the text can be put back together exactly as it was even
        /// when its line terminators are mixed.
        /// </summary>
        public static List<KeyValuePair<string, string>> SplitLines(string text)
        {
            var lines = new List<KeyValuePair<string, string>>();

            int start = 0;

            while (start < text.Length)
            {
                int end = text.IndexOf('\n', start);

                if (end < 0)
                {
                    lines.Add(new KeyValuePair<string, string>(text.Substring(start), ""));

                    break;
                }

                if (end > start && text[end - 1] == '\r') lines.Add(new KeyValuePair<string, string>(text.Substring(start, end - start - 1), "\r\n"));
                else lines.Add(new KeyValuePair<string, string>(text.Substring(start, end - start), "\n"));

                start = end + 1;
            }

            return lines;
        }

        /// <summary>
        /// Returns the terminator to write after a line: its own one, except that a line added by the UI (which has
        /// none) or a formerly last line that is last no more get the terminator the file prefers.
        /// </summary>
        public static string TerminatorFor(string own, bool isLastLine, string preferred, bool fileEndsWithTerminator)
        {
            if (!isLastLine) return string.IsNullOrEmpty(own) ? preferred : own;

            return own ?? (fileEndsWithTerminator ? preferred : "");
        }

        private static bool LooksLikeMultiByteUtf8(byte[] bytes)
        {
            bool anyMultiByte = false;

            int i = 0;

            while (i < bytes.Length)
            {
                byte b = bytes[i];

                int trailing;

                if (b < 0x80) { i++; continue; }
                else if (b >= 0xC2 && b <= 0xDF) trailing = 1;
                else if (b >= 0xE0 && b <= 0xEF) trailing = 2;
                else if (b >= 0xF0 && b <= 0xF4) trailing = 3;
                else return false;

                if (i + trailing >= bytes.Length) return false;

                for (int k = 1; k <= trailing; k++)
                {
                    if ((bytes[i + k] & 0xC0) != 0x80) return false;
                }

                anyMultiByte = true;

                i += trailing + 1;
            }

            return anyMultiByte;
        }
    }
}
